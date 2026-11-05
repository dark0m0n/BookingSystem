# Seat ownership, price history, and legal state transitions

Status: **accepted; partially implemented**. This record consolidates decisions represented by the current working tree. Schema constraints, local domain guards, snapshots and version-checked writes exist; complete booking transactions and durable allocation ownership remain unfinished.

## Context and enforcement boundaries

The application remains a modular monolith with Identity, Catalogue, Venue, Reservation, Booking, Payment and Notification boundaries. Physical seats describe a venue; SessionSeat describes sellable inventory for one session. Holds, bookings and payments represent distinct lifecycles with their own historical data.

Use database constraints for relational identity and uniqueness, domain methods for local rules, and application transactions for changes spanning aggregates. Foreign keys and integer status columns alone cannot establish a legal booking workflow.

## D-01: Separate seat identity, customer ownership and write authority

| Decision | Enforcement and consequence |
| --- | --- |
| Coordinates are unique within an auditorium. | Unique `(AuditoriumId, Row, Number)`; coordinates can repeat elsewhere. Row comparison is exact and case-sensitive, without normalization. |
| Inventory belongs to a session/physical-seat pair. | Unique `(SessionId, SeatId)`; physical seats can appear in multiple sessions. |
| Holds/bookings identify a customer through UserId. | Domain owner checks guard hold conversion/cancellation and booking cancellation. JWT authentication now exists; an Identity user FK and authenticated booking workflow are absent. |
| Items preserve reusable inventory history. | Composite keys `(SeatHoldId, SessionSeatId)` and `(BookingId, SessionSeatId)` prevent duplicates within a parent while allowing historical reuse. No global unique item-to-seat constraint exists. |
| Every allocation change checks the observed version. | SessionSeat.Version is a nonnegative bigint EF concurrency token. The allocation writer matches ID, version and expected status, then increments Version. Tracked updates also check/advance it; deletes check it. |

The allocation writer supports Available→Held, Held→Held transfer, Held→Booked confirmation, and Held/Booked→Available/Unavailable release. Transfer must advance the token even while still Held, fencing out stale expiry/release actors. A stale write fails; reloading a new token and blindly retrying discards this protection.

**Remaining requirement:** persist which hold/booking owns the allocation and correlate its claim version. Version is not an owner ID or authorization credential. Booking has no source HoldId link; neither item stores a claim version. Application transactions must coordinate all seats, hold conversion and booking state. The booking factory leaves its source hold unchanged.

Required FKs use restricted deletion to preserve referenced records. They prove existence, not that a seat shares its session's auditorium or that an item shares its parent's session. See [schema constraints](../features/local-persistence/data-model.md), [allocation writer](../../src/TicketBooking.Infrastructure/Persistence/SessionSeatAllocationWriter.cs), and [concurrency](../features/session-seat-concurrency/FEATURE.md).

## D-02: Capture prices rather than recomputing history

Use Money with explicit UAH/EUR/USD currency and nonnegative decimal(18,2) amounts. Reject excess fractional precision, mixed currencies and out-of-range totals; do not round or convert implicitly. Zero is representable without defining free checkout.

The capture chain is `SessionSeat.Price → SeatHoldItem.PriceSnapshot → BookingItem.PriceSnapshot`. Hold/booking totals sum their own items; Payment.Amount copies the booking total. Callers cannot supply independent booking totals or payment amounts.

Every capture is an independent Money instance, including single-item totals. Money is publicly immutable, but EF can edit its mapped complex values through private setters. Copying prevents tracked catalogue edits from changing history through shared references.

Separate amount/currency columns store snapshots, totals and payment amounts. History queries must read these columns rather than current inventory prices. Catalogue repricing or currency changes cannot alter existing history through normal domain operations. An expired hold cannot create a new booking; a new hold captures current prices.

SQL precision does not enforce every Money rule, cross-row sums or immutability. Raw SQL and deliberate EF history edits can alter these values; no immutability trigger exists. Authoritative inventory loading remains application work. See [snapshot rationale and verification](../features/money-and-pricing/price-snapshots.md) and [Money](../../src/TicketBooking.Domain/Money.cs).

## D-03: Change lifecycle state through guarded operations

| Entity | Legal changes | Guard |
| --- | --- | --- |
| SeatHold | Active→Converted or Cancelled | Owner; strictly before ExpiresAt. |
| SeatHold | Active→Expired | At or after ExpiresAt. |
| Booking | PendingPayment→Confirmed | Strictly before PaymentDeadline; application must also verify payment and allocation. |
| Booking | PendingPayment/Confirmed→Cancelled | Owner; strictly before CancellationDeadline. |
| Booking | PendingPayment→Expired | At or after PaymentDeadline. |
| Payment | Pending→Succeeded or Failed | Nonblank provider reference matching any recorded reference. |
| Payment | Succeeded/Failed→same outcome | Same reference; idempotent no-op. |

Other public transitions reject without changing state. Terminal holds/bookings reject repeated transitions. Completed payments cannot change outcome/reference. Failed payments do not reopen; another attempt requires a still-eligible booking. Refunded enum values are reserved; refund and messaging entities/transitions remain deferred.

Use DomainRuleException/DomainError for expected rejections and stable wire codes such as seat_not_available, hold_expired and payment_already_processed. Reference mismatch is distinct from a conflicting outcome. See [error codes](../features/api-diagnostics/error-codes.md) and the [lifecycle flow](../features/reservation-booking-payment/end-to-end-flow.md).

Booking numbers are globally unique. Payment idempotency keys are unique within a booking; nonnull provider references are globally unique. Storage uniqueness does not implement request replay or authenticated callbacks. Integer status columns have no SQL transition checks; domain guards must not be bypassed.

## D-04: Capture UTC deadlines and retain seats until the booking cutoff

Use UTC DateTimeOffset and injected TimeProvider. Capture hold expiry as creation plus hold duration, payment deadline as booking creation plus payment window, both capped at session start; cancellation deadline is session start minus lead time. Defaults are 10 minutes, 15 minutes and one hour. Equality closes conversion/payment/cancellation and permits expiry. Retries and configuration changes do not extend captured deadlines.

After conversion, the booking retains Held inventory until PaymentDeadline; original hold expiry must not release it. **Planned workflow:** atomically expire/release only the booking's own allocations, leaving inventory Unavailable at/after session start. Late verified success never revives an expired booking or reclaims seats; durable reconciliation and idempotent compensation are required. Duplicate success for the payment that validly confirmed a booking must not trigger a refund. Workers, callbacks and compensation remain unimplemented. See the detailed [retention and late-payment decision](../features/booking-timing/seat-retention-and-late-payments.md).

## Evidence and consequences

[Domain tests](../../tests/TicketBooking.Domain.Tests) cover transition/error identity, exact totals, independent snapshots and one-tick deadline boundaries; the last domain run passed 159 tests. [Persistence tests](../../tests/TicketBooking.IntegrationTests/PostgresPersistenceTests.cs) cover FK/uniqueness enforcement and persisted repricing isolation; [concurrency tests](../../tests/TicketBooking.IntegrationTests/SessionSeatConcurrencyTests.cs) cover competing version-checked writes. They establish local invariants, not authenticated end-to-end prevention of duplicate sales. Tests were not rerun for this documentation-only update.

Schema evidence: [domain migration](../../src/TicketBooking.Infrastructure/Persistence/Migrations/20261105143133_AddDomainForeignKeysAndUniqueConstraints.cs) and [concurrency migration](../../src/TicketBooking.Infrastructure/Persistence/Migrations/20261105144851_AddSessionSeatOptimisticConcurrency.cs). Applied development/deployment state is not verified. This documentation change applies no migrations and changes no runtime behavior.

Future work must add durable allocation/source-hold/confirming-payment links, atomic workflows, provider verification/deduplication, expiry triggers and compensation before claiming complete ownership enforcement. Add domain tests alongside new rules and database/application tests for cross-aggregate guarantees.

## Identity persistence follow-up

[ASP.NET Core Identity storage](../features/identity-persistence/FEATURE.md) now provides GUID users/roles in the shared context. Domain remains independent of framework types. Registration/password login and JWT authentication now exist, and normalized-email uniqueness/profile storage have a follow-up migration. D-01 still requires business UserId foreign keys/backfill, durable allocation ownership and account-deletion coordination; authenticated booking workflows remain unfinished. Identity dependent FKs cascade; the restrictive deletion decision above applies to business references.

## Refresh-rotation follow-up

[Refresh families](../features/identity-persistence/refresh-token-rotation.md) now persist high-entropy token hashes and consumed generations. Each login captures an absolute UTC renewal deadline; family-row locking serializes rotation and replay revocation. This adds user-linked auth state without changing booking ownership/FKs. Public logout now uses the same family-row lock and revokes the family associated with any retained generation, idempotently preserving its first revocation time. Immediate access JWT revocation and history cleanup remain future work.

## Role authorization follow-up

[Customer/Admin roles and policies](../features/identity-persistence/roles-and-policies.md) now define customer-or-admin and admin-only access. Role definitions are upserted without admin users/grants, preserving existing memberships. Public registration commits its user and Customer membership together. Resource ownership remains independent of role authorization and requires the future booking/hold application workflows.
