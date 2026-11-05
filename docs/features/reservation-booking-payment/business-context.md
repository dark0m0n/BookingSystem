# Business context and rules

The supplied plan requires temporary holds, owner-only conversion before expiration, historical prices, bookings, idempotent payment requests, and deadline-aware cancellation. It does not define a complete state table, cancellation window, or payment timeout. The domain uses an injected BookingTimingPolicy and TimeProvider, captures UTC deadlines during construction, and implements the conservative interpretation below. Configurable defaults are documented in the [timing bundle](../booking-timing/FEATURE.md).

| Rule | Requirement / choice | Enforcement and scope |
| --- | --- | --- |
| BR-01 | A new hold is Active, nonempty, expires in the future, and contains unique Available seats from its session. | `SeatHold` constructor derives expiry using policy and future session start; nonempty GUIDs, inventory status/session, and Money snapshots validated. No database availability check. |
| BR-02 | Only the owner may convert an Active hold strictly before ExpiresAt. | `EnsureCanConvert`, `Convert`, `Booking.CreateFromHold`; directly from the plan. |
| BR-03 | Active holds expire at or after ExpiresAt; owner cancellation must occur before expiry. | `Expire`/`Cancel`; exact-boundary and cancellation behavior are implementation choices. |
| BR-04 | Hold and booking item prices are immutable snapshots; TotalAmount is their sum. | SeatHold snapshots server-resolved inventory; Money.Sum enforces one currency and decimal(18,2) limits. See the money/pricing bundle. |
| BR-05 | Pending bookings may confirm, cancel, or expire; confirmed bookings may cancel. | Booking methods and transition tests. Confirmation also requires now < captured PaymentDeadline; payment/seat evidence remains Application responsibility. |
| BR-06 | Cancellation requires ownership, an eligible state, and now strictly before the captured deadline, calculated from session start minus configured lead time. | `Booking.Cancel` uses the immutable captured cutoff. Application must supply the real session at creation. |
| BR-07 | Only Pending payments succeed/fail; identical result/reference replays are harmless, conflicting results/references rejected. | Payment completion methods; local replay semantics. No cross-request key deduplication yet. |
| BR-08 | Booking expiry requires reaching the captured payment deadline after CreatedAt. | `Booking.Expire`; the configured payment window begins at booking creation and is capped at session start. |
| BR-09 | Stored instants are UTC-normalized; rules obtain current time once per operation from an injected TimeProvider. | Session/SeatHold construction, Booking factory, and deadline methods; API registers TimeProvider.System. |

Identifiers and required text are validated; zero-price Money values and derived zero-amount payments are representable, without defining a free-checkout workflow. The payment key is preserved verbatim, not normalized.

Booking numbers are globally unique in the schema; payment keys are unique within each booking and nonnull provider references are globally unique. These storage constraints do not implement idempotent request replay. Unimplemented requirements include exclusive active allocation ownership, complete prevention of duplicate sales, authoritative database inventory loading, persisted allocation-owner checks, authentication, atomic payment/booking/seat confirmation, refunds and reliable messaging. Entity methods alone cannot enforce these across concurrent requests. The [decision record](../../decisions/0001-seat-ownership-price-history-and-transitions.md) separates schema guarantees, domain rules and pending application transactions.
