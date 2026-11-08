# Reservation, booking, and payment lifecycle

Status: **partial**. Customer hold creation, owner-only GET/DELETE, transactional expiry, domain lifecycle rules and EF persistence are implemented. A server-side conversion/transfer command is implemented; public booking/payment workflows, confirmation coordination, refunds and messaging remain absent. Working tree inspected; intended rules are recorded from the externally supplied TicketBooking_Project_Plan.md and subsequent task requirements.

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
- [Expiry worker](hold-expiry-worker.md)
- [Repeated cleanup and conversion races](hold-cleanup-conversion.md)
- [Integration test coverage](testing.md)
- [Ownership/price/transition decision](../../decisions/0001-seat-ownership-price-history-and-transitions.md)

## Current verification

All **155 targeted integration cases passed with zero skips** against disposable PostgreSQL, including cleanup/conversion lock races, replay after expiry/reacquisition, stale ownership tokens, stale tracked-status saves, full rollback on save/deadline failures, booking-token SQL constraints and existing hold/worker/persistence regressions. All **199 domain tests** and **18 Application ownership tests** passed, including owner-safe cleanup repeats and independent transfer-token capture. Model/snapshot consistency and feature JSON/code/link checks passed.

Public booking/payment endpoints, pending-booking expiry and refunds remain outside this implementation; the conversion command is a registered server-side primitive. Apply pending migrations including AddBookingItemAllocationVersion. Database migration verification is limited to isolated test schemas; development/deployed schema state remains unverified.

The final solution build passed with **zero warnings/errors**. Documentation bundle structure, entry-point JSON, code symbols and local links validated.

Integration-test follow-up added four cases: a two-hold lookup/cancellation/expiry flow with different prices and currencies, owner GET/DELETE denial after removing Customer membership and issuing a fresh JWT, and multi-seat rollback caused by an actual PostgreSQL child-insert trigger failure followed by a successful independent request. The focused 155-case run passed without skips; production behavior and schema are unchanged by this test-only follow-up. See [coverage and run instructions](testing.md).

Twenty-user contention verification: the new HTTP test registered/logged in twenty distinct customers, used separate HTTP/1.1 handlers, and paused twenty inventory-query readers on twenty distinct PostgreSQL backend PIDs/transactions before permitting allocation. It observed exactly **one 201 Created and nineteen 409 seat_not_available** responses, one persisted hold/item owned by the winning caller, one inventory version increment, and unchanged unselected seats. The new case and two existing concurrency regressions passed (three cases, zero skips) against disposable PostgreSQL. See [coverage](testing.md).

Overlap/expiry race follow-up added five deterministic PostgreSQL cases: {A,B}/{B,C} under either winner and either caller input order (four variants), plus a due worker overlapping a validated conversion's commit. Each overlap asserts one complete winning hold, a 409 loser without partial claims/version increments, and a successful retry of the free exclusive seat. The commit race verifies that the worker examines but skips the locked Active row, then ignores Converted and retains booking tokens. All **11 focused race/concurrency cases passed with zero skips**, including prior expiry-first, late-conversion rollback and twenty-user regressions. This test-only follow-up changes no production behavior or schema.
