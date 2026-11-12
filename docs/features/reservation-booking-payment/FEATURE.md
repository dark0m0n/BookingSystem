# Reservation, booking, and payment lifecycle

An explicit Admin-policy GET /api/admin/bookings now returns paginated summaries across owners/statuses, including stored UserId. Customer details/history remain owner-only. It reuses bounded pagination and one count/page snapshot without lifecycle mutations. See [admin contract](booking-http-contract.md#authorized-admin-booking-list).

Status: **partial**. Customer hold creation, owner-only GET/DELETE, transactional expiry, domain lifecycle rules and EF persistence are implemented. Customer POST /api/bookings now exposes the transactional conversion/transfer command; booking cancellation and payment workflows, confirmation coordination, refunds and messaging remain absent. Working tree inspected; intended rules are recorded from the externally supplied TicketBooking_Project_Plan.md and subsequent task requirements.

Customers create holds using authoritative session inventory and historical Money snapshots, retrieve their own history, and cancel eligible holds before their captured deadline. The API's HoldExpiryWorker finds due Active holds in bounded batches and expires/releases each one transactionally. Converted holds are excluded so original hold expiry cannot release a pending booking's allocation.

## Behavior and verification scope

- Customer routes use ICurrentUser and ResourceOwnershipService; Admin has no ownership bypass.
- Creation verifies session/auditorium membership, availability and version tokens; every claim and snapshot commits together or rolls back.
- GET returns stored history. DELETE atomically cancels/releases eligible holds using retained claim versions; an owner repeat after Cancelled returns 204 without further inventory writes.
- Expiry runs immediately then polls with bounded work, fresh scopes/transactions, shared session locks and ordered inventory locks. It rechecks state/time after acquiring access and preserves immutable deadlines/prices. Failed/locked holds do not starve later batches.
- Expired Active allocations release to Available only before a Scheduled session starts; otherwise they become Unavailable. Unknown/stale ownership tokens fail closed and are logged.
- Apply the checked-in migrations through [local startup](../../../README.md#local-startup); the worker's filtered active-expiry index does not automatically migrate a development/deployed database. Tests use isolated PostgreSQL schemas.

Run `dotnet test TicketBooking.slnx` with TICKETBOOKING_TEST_CONNECTION_STRING targeting a disposable PostgreSQL database to enable integration tests. Domain tests use a controllable clock without database/provider dependencies. See [timing](../booking-timing/FEATURE.md), [money/pricing](../money-and-pricing/FEATURE.md) and [allocation fencing](../session-seat-concurrency/FEATURE.md).

## Navigation

- [Business context and rules](business-context.md)
- [End-to-end flow](end-to-end-flow.md)
- [Entry points](entry-points.json)
- [Application responsibilities](per-app-responsibility.md)
- [Frontend structure](frontend-structure.md)
- [Backend structure](backend-structure.md)
- [Data model](data-model.md)
- [Integration points](integration-points.md)
- [Known gotchas](known-gotchas.md)
- [Hold HTTP contract](hold-http-contract.md)
- [Booking creation HTTP contract](booking-http-contract.md)
- [Expiry worker](hold-expiry-worker.md)
- [Pending booking retention and expiry](booking-expiry-worker.md)
- [Repeated cleanup and conversion races](hold-cleanup-conversion.md)
- [Integration test coverage](testing.md)
- [Ownership/price/transition decision](../../decisions/0001-seat-ownership-price-history-and-transitions.md)

## Current verification

Booking regression follow-up: **148 targeted integration cases with PostgreSQL enabled and all 199 domain cases passed with zero failures/skips**, including expired holds after reacquisition, wrong-owner rejection with no inserted booking/items, sequential and simultaneous conversion replay, independent price snapshots, and unpaid expiry using a captured seven-minute window despite a fifteen-minute host policy. Domain rejection tests assert specific DomainError values and unchanged state. See [the regression matrix and rerun commands](testing.md#booking-regression-matrix). This follow-up changes tests/documentation only; production behavior/schema are unchanged.

All **155 targeted integration cases passed with zero skips** against disposable PostgreSQL, including cleanup/conversion lock races, replay after expiry/reacquisition, stale ownership tokens, stale tracked-status saves, full rollback on save/deadline failures, booking-token SQL constraints and existing hold/worker/persistence regressions. All **199 domain tests** and **18 Application ownership tests** passed, including owner-safe cleanup repeats and independent transfer-token capture. Model/snapshot consistency and feature JSON/code/link checks passed.

Booking creation is exposed through POST /api/bookings. Owner-only booking details and caller-paginated history are implemented; payment endpoints and refunds remain outside this implementation. Pending-booking expiry is implemented by the bounded worker. Apply pending migrations including AddBookingItemAllocationVersion. Database migration verification is limited to isolated test schemas; development/deployed schema state remains unverified.

The final solution build passed with **zero warnings/errors**. Documentation bundle structure, entry-point JSON, code symbols and local links validated.

Integration-test follow-up added four cases: a two-hold lookup/cancellation/expiry flow with different prices and currencies, owner GET/DELETE denial after removing Customer membership and issuing a fresh JWT, and multi-seat rollback caused by an actual PostgreSQL child-insert trigger failure followed by a successful independent request. The focused 155-case run passed without skips; production behavior and schema are unchanged by this test-only follow-up. See [coverage and run instructions](testing.md).

Twenty-user contention verification: the new HTTP test registered/logged in twenty distinct customers, used separate HTTP/1.1 handlers, and paused twenty inventory-query readers on twenty distinct PostgreSQL backend PIDs/transactions before permitting allocation. It observed exactly **one 201 Created and nineteen 409 seat_not_available** responses, one persisted hold/item owned by the winning caller, one inventory version increment, and unchanged unselected seats. The new case and two existing concurrency regressions passed (three cases, zero skips) against disposable PostgreSQL. See [coverage](testing.md).

Overlap/expiry race follow-up added five deterministic PostgreSQL cases: {A,B}/{B,C} under either winner and either caller input order (four variants), plus a due worker overlapping a validated conversion's commit. Each overlap asserts one complete winning hold, a 409 loser without partial claims/version increments, and a successful retry of the free exclusive seat. The commit race verifies that the worker examines but skips the locked Active row, then ignores Converted and retains booking tokens. All **11 focused race/concurrency cases passed with zero skips**, including prior expiry-first, late-conversion rollback and twenty-user regressions. This test-only follow-up changes no production behavior or schema.

## Booking creation HTTP verification

Duplicate-prevention follow-up: every new booking records SourceHoldId; EnforceUniqueBookingSourceHold adds a unique index and restrictive hold foreign key. The hold lock serializes simultaneous requests, and the index independently rejects a second persisted booking for a known hold. Legacy source IDs remain null. Repeated POST continues to return 409 hold_not_active. Apply the migration using README startup before running the updated model.

All **89 focused persistence/booking/cleanup tests passed with zero skips** against disposable PostgreSQL after correcting EF optional one-to-one fixup to preserve existing source IDs. The full run exposed that duplicate-insert regression (subsequently fixed and passed in the focused run); its other 697 integration cases passed and three live Redis cases skipped. All 199 domain, 18 Application and one Architecture test passed. Solution build, formatting and feature-bundle checks passed. Development/deployed migration state remains unverified.

POST /api/bookings is registered in Program with Customer authorization and HoldId validation. [The HTTP contract](booking-http-contract.md) documents its server-generated fields, 201 response, error codes and retry limitation. Conversion race/cache tests now exercise this production route; the test-only conversion adapter was removed. The subsequent EnforceUniqueBookingSourceHold migration adds a persisted unique source-hold reference; see [data model](data-model.md).

Validation: the solution build passed with zero warnings/errors. The full run passed 908 cases and skipped three live Redis cases; four cache cases initially expected the removed adapter's 200 response. After correcting those assertions to 201, all 25 focused booking/cache cases passed with zero skips. Formatting and bundle JSON/code/link checks passed. Only disposable PostgreSQL was migrated; development/deployed state remains unverified.

## Active hold cache

Implemented in the inspected working tree: active responses use Redis keys `hold:{holdId}` with fixed expiry at the stored hold deadline. Creation caches only after commit; owner GET checks the database header before cache access and reloads snapshots on a miss. Cancellation, conversion and expiry invalidate after commit. Redis failures do not roll back PostgreSQL work. See [cache behavior and configuration](hold-caching.md). HTTP contracts and historical states are unchanged.

Cache mutations now consistently follow successful database commits, including hold lookup snapshot commits. Invalid cache lookups are read-only. See [commit ordering and failure coverage](hold-caching.md#database-commit-boundary).

Expiration correctness is independent of Redis key presence, TTL delivery and notifications: database guards enforce the deadline, and the PostgreSQL polling worker commits retained-version releases. See [authority and outage coverage](hold-expiry-worker.md#postgresql-authority-and-redis-independence).

## Pending-booking retention verification

The complete solution run passed **947 tests**, with **zero failures** and three live Redis cases skipped. PostgreSQL cases used disposable migrated storage. This includes the new exact payment-deadline boundaries, original-hold expiry independence, retry retention, session-start/non-Scheduled release behavior, terminal/confirmed preservation, retained-token/save/commit rollback, concurrent-worker fencing, stale tracked confirmation, bounded poison-row progress and hosted-worker lifecycle. The Release solution build passed with zero warnings/errors; formatting, model/snapshot consistency, feature JSON/code/link checks passed. Apply pending migrations including IndexPendingBookingExpirations through README startup; development/deployed state was not verified.

## Owner-only booking details and history

Implemented: GET /api/bookings/{bookingId} returns owner-authorized stored details; GET /api/me/bookings returns caller-filtered summaries ordered by CreatedAt/Id descending. Both require the Customer policy, exclude internal ownership/token fields, use no-store responses and perform no lifecycle changes. Creation returns the details Location. Pagination defaults to page 1/size 20, maximum 100, and count/page share one RepeatableRead snapshot. See [HTTP contract](booking-http-contract.md).

The full solution run passed **975 tests**, zero failures, with three live Redis cases skipped. New PostgreSQL coverage includes all stored statuses, snapshot prices, caller/Admin/roleless access, owner spoofing, bounds/repeated/overflow inputs, empty/beyond-end pages, stable tied ordering and concurrent insertion between count and page. Creation time now captures PostgreSQL microsecond precision so responses and history agree. Release build passed with zero warnings/errors; formatting, model/snapshot and documentation checks passed. Apply IndexBookingHistoryByOwner through README startup; development/deployed migration state remains unverified.

## Admin booking overview verification

GET /api/admin/bookings is registered in Program with the named Admin policy. It returns global newest-first summaries including stored UserId, bounded by shared pagination validation, with one RepeatableRead count/page snapshot and no lifecycle side effects. Customer routes still apply their existing ownership rules. Apply IndexAdminBookingHistory through README startup for the global (CreatedAt, Id) index.

All **76 focused admin/owner-read/pagination/model cases passed**, plus the updated all-admin-route authorization regression (**77 cases total, zero failures/skips**) against disposable PostgreSQL. The Release solution build passed with zero warnings/errors. Formatting, model/snapshot consistency and feature JSON/code/link checks passed. Development/deployed migration state remains unverified. See [the HTTP contract](booking-http-contract.md#authorized-admin-booking-list).
