# Customer hold HTTP contract

`POST /api/sessions/{sessionId}/holds` requires a bearer token satisfying `AuthorizationPolicies.Customer` (Customer or Admin). The caller always owns their newly created hold; Admin has no ownership bypass.

```json
{
  "sessionSeatIds": ["f0a660f9-e830-43b8-8a02-5648315dd291"]
}
```

Use session-seat inventory IDs from `GET /api/sessions/{id}/seats`, rather than physical SeatId values. Select 1–100 distinct, nonempty IDs. The API accepts no authoritative customer ID, prices, totals, expiry or status from the request. Unknown JSON fields do not override server values. Empty selections return 400 with `sessionSeatIds` errors ("Select at least one seat."); duplicate IDs return 400 on the same field. All selected inventory must belong to the route session, even when every selected seat belongs to a different valid session. Bookable means a future Scheduled session with a Published event and Available inventory. Draft/Archived events are hidden with 404; Cancelled/Completed/started sessions return 409. Rejected selections create no hold/items and do not advance inventory versions.

Success returns 201 with `id`, `sessionId`, `expiresAt` (UTC), `status` (Active=0), `totalAmount`, `currency`, and `items` containing `sessionSeatId`, `price`, `currency`. Prices are independent historical snapshots and totalAmount is their exact sum. Responses use `Cache-Control: no-store`. Location points to the implemented owner-only GET /api/holds/{holdId} route.

Expiry is captured from BookingTimingPolicy.HoldDuration (default ten minutes), capped at session start. Request timing uses injected TimeProvider; a hold that expires before commit is rolled back. Shared session/event locks exclude administrative eligibility changes; all ordered, version-fenced seat claims and hold/item inserts share one database transaction. A competing claim or save failure rolls back the whole selection. Allocation versions returned by the writer are persisted on each item and are not exposed as owner credentials.

| HTTP | ProblemDetails code | Trigger |
| --- | --- | --- |
| 400 | validation_failed | Missing/malformed body or invalid seat selection. |
| 401 | unauthorized | Missing/invalid authentication or invalid authenticated subject. |
| 403 | forbidden | Authenticated caller lacks the Customer policy. |
| 404 | not_found | Missing session, unpublished event, or missing/foreign inventory selection. |
| 409 | seat_not_available | Any Held, Booked, Unavailable or concurrently changed selected seat; the entire request rolls back, regardless of selection order. |
| 409 | conflict | Non-Scheduled/started session, invalid auditorium relationship, mixed currencies or total outside Money limits. |
| 409 | hold_expired | The captured deadline is reached while allocating/saving. |
| 415 | http_error | Unsupported request media type. |
| 500 | internal_error | Unexpected persistence/server failure; transaction rolls back. |

Errors use application/problem+json with stable `type`, `title`, `status`, `instance`, `code`, `traceId`; validation errors include field errors when binding succeeds. Development OpenAPI documents the request/response and problem statuses.

Apply `20261107231200_AddHoldItemAllocationVersion` through the README local migration/startup procedure before running the endpoint. It adds nullable bigint AllocationVersion with a positive-or-null check. Existing holds remain intact with null/unknown allocation tokens; no backfill claims ownership. Tests migrate only isolated PostgreSQL schemas.

Verification command: set `TICKETBOOKING_TEST_CONNECTION_STRING` to a disposable PostgreSQL database and run `dotnet test TicketBooking.slnx`. The HTTP fixture creates/migrates/drops private schemas. AuthEndpointsTests.Holds covers customer/anonymous/roleless access, spoofed fields, persisted snapshots/tokens, invalid selections, future-session eligibility, exact start boundaries, whole-request rollback, concurrent claims, seat-map visibility, admin edit protection and OpenAPI. Domain tests cover one-time token capture and invalid-token rejection. Runtime results are recorded after execution in FEATURE.md.

Owner GET and DELETE cancellation are implemented. The API now runs [automatic Active-hold expiry](hold-expiry-worker.md). Booking conversion, payment and idempotent POST replay remain separate workflows. Repeating a successful POST for the same seats conflicts; it does not renew the original deadline. Passing the deadline alone does not release inventory; the worker must commit its transaction.

Atomic transaction checks pause a request after its first successful seat update. A separate connection must still see the original inventory until commit. On success, all requested seats, captured item prices, allocation versions, total and UTC expiry become visible together; later catalogue repricing cannot alter the hold. If a competing request wins a later seat while the first request is paused, the first request returns seat_not_available and its earlier seat update/version increment rolls back, while the winner's hold and claim remain intact. These scenarios are exercised by AuthEndpointsTests.HoldAtomicity.

## Owner-only GET and DELETE

GET /api/holds/{holdId} and DELETE /api/holds/{holdId} require the Customer policy and the persisted SeatHold.UserId matching ICurrentUser. Missing resources return 404 not_found; other owners (including Admin) receive 403 resource_not_owned. Query/body identity fields do not authorize access. GET returns 200 HoldResponse using stored snapshots for every lifecycle state. GET itself does not expire or release elapsed holds; the worker performs that transaction. Responses use Cache-Control: no-store.

DELETE cancels an Active hold only while server UTC time is strictly before ExpiresAt. It returns 204 with no body. It preserves the hold/items and original captured values as history. A hold row lock serializes transitions; every seat release checks the item's saved AllocationVersion and Held status. All releases and the Cancelled transition commit together or roll back together. Released seats become Available for future Scheduled sessions; otherwise Unavailable. No expected token is refreshed.

Cancellation errors for an Active hold: 409 hold_expired at/after expiry (including crossing the cutoff during release/save), 409 hold_not_active for Converted state, 409 seat_not_available for missing/legacy/stale claim tokens or mismatched inventory, and safe 500 internal_error on unexpected save failure. Every rejected cancellation leaves status and all seat allocations unchanged. An owner repeat after Cancelled returns 204 without inventory writes, even after the original expiry; ownership is rechecked and seats acquired by a later hold are never released. No new migration is required.

## Eligibility during worker lag

Hold use always checks injected current UTC time against the stored ExpiresAt, independently of persisted Status or the worker schedule. For an Active hold, EnsureCanConvert, Convert, Cancel and Booking.CreateFromHold reject now >= ExpiresAt with DomainError.HoldExpired even while the row remains Active. Owner DELETE maps this to 409 hold_expired and makes no state/allocation changes. One tick before the cutoff remains eligible.

GET remains a historical read: it may return stored Active with an elapsed expiresAt before the worker runs. That response does not authorize conversion or cancellation. Seats also remain Held until transactional cleanup commits, so a new POST selecting them receives 409 seat_not_available. The booking factory is a domain API; no booking HTTP endpoint is introduced by this check.

Completed cancellation replay is a read-only acknowledgement, not a new use of an expired Active hold. Converted holds still return 409 hold_not_active and cannot be cancelled through the hold route. See [cleanup/conversion coordination](hold-cleanup-conversion.md) for race, token-transfer and idempotency rules.

## Server-side cache

Active unelapsed responses are cached in Redis under `hold:{holdId}` until their original expiry. GET checks the persisted header and owner before cache access and reloads item snapshots on misses. HTTP `Cache-Control: no-store` remains unchanged. Expired/terminal history comes from PostgreSQL, and lifecycle commands never trust cached state. See [TTL, invalidation and outage behavior](hold-caching.md).
