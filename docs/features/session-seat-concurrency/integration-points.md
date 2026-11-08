# Integration points

| Boundary | Interface | Failure/authority |
| --- | --- | --- |
| Customer hold endpoint / future booking workflows → allocation writer | ID, retained expectedVersion, operation and policy-approved release states; returns next version. | One atomic conditional row update; stale/missing/version-or-state mismatch throws DbUpdateConcurrencyException. Caller owns authorization/business deadlines and transaction. |
| Tracked domain row → DbContext | Modified/deleted SessionSeat with original Version. | SaveChanges checks the original token; updates stamp next version. Other rows in the same SaveChanges batch roll back on conflict. |
| Writer → PostgreSQL | Explicit predicate-based ExecuteUpdateAsync. | No implicit EF concurrency behavior; expected version/state and increment are encoded in SQL. |
| API → DI/errors | Scoped SessionSeatAllocationWriter and TicketBookingDbContext; ApiErrorCatalog. | Allocation conflicts become 409 `seat_not_available`; other EF conflicts become `concurrency_conflict`. Customer hold creation uses the writer inside its aggregate transaction; no automatic retry exists. |

[Persistence constraints](../local-persistence/FEATURE.md), [timing](../booking-timing/FEATURE.md), [retention decisions](../booking-timing/seat-retention-and-late-payments.md), and [lifecycle](../reservation-booking-payment/FEATURE.md) remain complementary. Full workflows must combine token checks with actual allocation ownership, clock-based deadlines, and aggregate transitions in one transaction. Release does not itself check cancellation deadlines or perform refunds.

The [API error contract](../api-diagnostics/error-codes.md) maps `SeatAllocationConflictException` (a DbUpdateConcurrencyException subclass) and EF conflicts whose entries are all SessionSeat to 409 `seat_not_available`. Callers must not rerun the operation with a fresh token without revalidating ownership and business state. No Redis, RabbitMQ, webhooks, or external provider calls are introduced.

The anonymous [public session seat map](../venue-catalogue-model/public-http-contract.md) now exposes the stored SessionSeat ID, physical seat ID, coordinates/type, price/currency, availability and Version from one consistent snapshot, with Cache-Control: no-store. This enables clients to retain expected tokens without exposing allocation owners. A map remains a read snapshot; authenticated allocation workflows must still revalidate ownership/state/deadlines and never blindly retry using refreshed versions.
