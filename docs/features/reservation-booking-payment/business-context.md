# Business context and rules

The supplied plan requires temporary holds, owner-only conversion before expiration, historical prices, bookings, idempotent payment requests, and deadline-aware cancellation. It does not define a complete state table, cancellation window, or payment timeout. The domain therefore accepts explicit policy deadlines and a caller-injected TimeProvider for current UTC time, implementing the conservative interpretation below.

| Rule | Requirement / choice | Enforcement and scope |
| --- | --- | --- |
| BR-01 | A new hold is Active, nonempty, expires in the future, and has no duplicate session seats. | `SeatHold` constructor; nonempty GUIDs and nonnegative snapshots validated. No database availability check. |
| BR-02 | Only the owner may convert an Active hold strictly before ExpiresAt. | `EnsureCanConvert`, `Convert`, `Booking.CreateFromHold`; directly from the plan. |
| BR-03 | Active holds expire at or after ExpiresAt; owner cancellation must occur before expiry. | `Expire`/`Cancel`; exact-boundary and cancellation behavior are implementation choices. |
| BR-04 | Hold and booking item prices are immutable snapshots; TotalAmount is their sum. | Item constructors, defensive read-only collections, booking factory. Currency/rounding unspecified. |
| BR-05 | Pending bookings may confirm, cancel, or expire; confirmed bookings may cancel. | Booking methods and transition tests. Payment/seat evidence for confirmation remains Application responsibility. |
| BR-06 | Cancellation requires ownership, an eligible state, and now strictly before the supplied deadline, which cannot exceed session start. | `Booking.Cancel`. No hard-coded window; Application must supply the real session and policy. |
| BR-07 | Only Pending payments succeed/fail; identical result/reference replays are harmless, conflicting results/references rejected. | Payment completion methods; local replay semantics. No cross-request key deduplication yet. |
| BR-08 | Booking expiry requires a reached payment deadline after CreatedAt. | `Booking.Expire`; explicit timeout policy is a design choice awaiting application configuration. |
| BR-09 | Stored instants are UTC-normalized; rules obtain current time once per operation from an injected TimeProvider. | Session/SeatHold construction, Booking factory, and deadline methods; API registers TimeProvider.System. |

Identifiers and required text are validated; zero-price items and zero-amount payments are representable, without defining a free-checkout workflow. The payment key is preserved verbatim, not normalized.

Unimplemented plan requirements: globally unique active holds, preventing duplicate sales, server-authoritative prices, unique booking numbers/payment keys, persisted ownership checks, authentication, atomic payment/booking/seat confirmation, refunds, and reliable messaging. Entity methods alone cannot enforce these across concurrent requests.
