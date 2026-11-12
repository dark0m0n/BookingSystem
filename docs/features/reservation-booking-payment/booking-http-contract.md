# Booking HTTP contract

Implemented in the working tree by `BookingEndpoints.MapBookingEndpoints`, registered in Program. `POST /api/bookings` requires bearer authentication and the Customer policy (which also permits Admin acting for their own account). Admin has no ownership bypass.

Request: `{"holdId":"<nonempty GUID>"}`. Missing/empty identifiers and malformed bodies return 400 validation_failed; field validation uses `holdId`. Only HoldId is bound: owner, number, amounts, currency, state and deadlines are server-controlled. Unsupported media types return 415.

Success returns **201 Created** with BookingResponse: id, number (`BK-` plus the generated booking GUID in N format), sessionId, numeric status (PendingPayment=0), createdAt, sessionStartsAt, paymentDeadline, cancellationDeadline, totalAmount, currency and items (sessionSeatId, price, currency). Location points to `/api/bookings/{bookingId}`. Creation time is normalized to PostgreSQL microsecond precision before deadline capture, matching stored history. The response uses Cache-Control: no-store; UserId, SourceHoldId and allocation tokens are not exposed. No payment is created.

## Transaction and failures

The API invokes `HoldConversionService.ConvertAsync` in the request scope. PostgreSQL is authoritative: it locks the hold before loading/checking ownership, locks the session, validates Active state and now strictly before ExpiresAt, checks the future Scheduled session, and locks inventory in ID order. Booking prices come from stored hold snapshots; session time comes from storage, and deadlines come from BookingTimingPolicy and TimeProvider. No Redis read authorizes conversion.

The command transfers every retained allocation token, captures the returned booking-item versions, inserts the booking and marks the hold Converted in one transaction. Final hold/payment deadline checks precede commit; any conflict/save/commit failure rolls back all database changes. Hold cache invalidation follows commit. See [the shared locking protocol](hold-cleanup-conversion.md).

| Result | Status/code |
| --- | --- |
| Anonymous or policy denied | 401 unauthorized / 403 forbidden |
| Missing hold | 404 not_found |
| Another customer's hold, including when called by Admin | 403 resource_not_owned |
| Elapsed Active hold or stored Expired hold | 409 hold_expired |
| Cancelled or Converted hold, including repeated POST | 409 hold_not_active |
| Unbookable session, missing/stale/unknown allocation evidence | 409 seat_not_available |
| Payment deadline passed during processing | 409 payment_deadline_passed |
| Unexpected persistence failure | 500 internal_error |

Concurrent conversions create at most one booking. SourceHoldId and its unique index prevent duplicate persisted bookings even outside the normal lock protocol. This endpoint has no request idempotency key or successful-response replay lookup: an uncertain successful response cannot be recovered by repeating POST. Owners can inspect their history through the GET routes below. Payment/confirmation and booking cancellation remain separate work. Pending bookings retain seats until their captured payment deadline; [the expiry worker](booking-expiry-worker.md) releases due allocations atomically. Apply EnforceUniqueBookingSourceHold before running the updated endpoint; it adds nullable SourceHoldId, IX_Bookings_SourceHoldId and a restrictive hold foreign key without backfilling historical bookings.

## Owner-only details

`GET /api/bookings/{bookingId:guid}` requires the Customer policy and the stored booking UserId matching ICurrentUser. Admin has no ownership bypass. Missing resources return 404 not_found; a different owner receives 403 resource_not_owned. Anonymous/policy-denied requests return 401 unauthorized / 403 forbidden.

The handler loads the booking header, calls ResourceOwnershipService.EnsureBookingOwner before reading item rows, and returns the same BookingResponse used by creation. Items are ordered by SessionSeatId. Header and items share a RepeatableRead transaction and come from stored snapshots, independent of current inventory prices, publication state or the clock. Reads return stored lifecycle state, including elapsed PendingPayment and terminal history, without expiring, confirming or releasing anything. Responses use Cache-Control: no-store.

## Caller booking history

`GET /api/me/bookings?page=1&pageSize=20` requires the same Customer policy. The query filters by authenticated ICurrentUser.UserId in PostgreSQL before counting or paging; request owner fields are ignored. The API also checks ownership of loaded page rows before serialization. Admin sees only their own history.

The response is BookingListResponse with items, page, pageSize, totalCount, totalPages, hasPreviousPage and hasNextPage, matching the shared PagedResponse contract. Items are BookingSummaryResponse headers: the BookingResponse fields above without nested items. All stored statuses are included. Internal user/source-hold IDs and allocation tokens remain excluded.

Pages are one-based; defaults are 1 and 20, with pageSize in 1–100. Invalid, blank, repeated or overflowing page/pageSize values return 400 validation_failed with camelCase field errors; the offset must fit Int32. Empty and beyond-end pages return 200 with empty items and the caller-filtered total. Unknown query fields cannot override ownership.

Ordering is CreatedAt descending, then Id descending. Count and page share one RepeatableRead snapshot, so concurrent inserts cannot make them disagree within a response. New bookings between separate requests can shift offset pages; there is no cross-request snapshot cursor. Successful and handler-level error responses use Cache-Control: no-store. No Redis or inventory mutation occurs.

IndexBookingHistoryByOwner adds IX_Bookings_UserId_CreatedAt_Id with ascending UserId and descending CreatedAt/Id for this access pattern; no booking data is rewritten. Apply it through README local startup.

## Authorized admin booking list

`GET /api/admin/bookings?page=1&pageSize=20` is an explicit management workflow protected by AuthorizationPolicies.Admin. Anonymous/invalid bearer requests receive 401 unauthorized; authenticated callers without an Admin role receive 403 forbidden, even when they own a booking or submit role/owner query fields. Customer ownership checks on `/api/bookings/{bookingId}` and `/api/me/bookings` are unchanged: this route does not grant an implicit bypass elsewhere.

The endpoint lists all owners and stored statuses. AdminBookingListResponse uses the shared pagination metadata; each AdminBookingSummaryResponse contains Id, Number, UserId, SessionId, numeric Status, CreatedAt, SessionStartsAt, PaymentDeadline, CancellationDeadline, TotalAmount and Currency. UserId is the stored booking owner reference. No account entity/profile/credential, source-hold link, allocation token, payment reference/key or nested seat items are returned.

Pagination uses the same defaults, 1–100 page-size bound, repeated/invalid/overflow input rejection, stable CreatedAt/Id descending order, and empty/beyond-end behavior as caller history. Count and page share one RepeatableRead snapshot, use no-store responses and never mutate bookings, seats, holds or payment outcomes. Only page/pageSize are supported query parameters; this version adds no search/status/owner filters.

IndexAdminBookingHistory adds IX_Bookings_CreatedAt_Id to support global newest-first reads, without changing columns or rewriting rows. Apply pending migrations through README startup. There is no admin details or mutation route in this change.

## Verification

`AuthEndpointsTests.Bookings` covers HTTP contracts, generated fields, snapshots/deadlines, owner spoofing, Customer policy, malformed input, exact expiry edges and OpenAPI. Existing conversion/cleanup, cache commit and expiry race tests now call this production endpoint, covering atomic rollback, replay conflicts, retained tokens and concurrent transitions against isolated PostgreSQL schemas.

AuthEndpointsTests.BookingReads covers all stored statuses, historical prices, owner/Admin/policy checks, no internal-data exposure, caller-filtered count/order/pagination, malformed and repeated query values, supported large offsets, creation Location, and deterministic count/page consistency during a concurrent insert. PaginationContractTests shares navigation assertions across booking/catalogue envelopes. PersistenceModelTests checks the owner-history index and snapshot consistency.

AuthEndpointsTests.AdminBookings covers anonymous/invalid bearer/Customer denial, fresh tokens after Admin removal, cross-owner/all-status pagination and snapshots, sensitive-field exclusions, customer-route isolation, invalid pagination, empty/max pages, concurrent insertion during count/page and OpenAPI. Shared pagination tests include the admin envelope; the existing all-admin-route authorization test includes this route.
