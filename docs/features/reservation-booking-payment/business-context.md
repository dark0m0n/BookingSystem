# Business context and rules

The supplied plan requires temporary holds, owner-only conversion before expiration, historical prices, bookings, idempotent payment requests, and deadline-aware cancellation. It does not define a complete state table, cancellation window, or payment timeout. The domain uses an injected BookingTimingPolicy and TimeProvider, captures UTC deadlines during construction, and implements the conservative interpretation below. Configurable defaults are documented in the [timing bundle](../booking-timing/FEATURE.md).

| Rule | Requirement / choice | Enforcement and scope |
| --- | --- | --- |
| BR-01 | A new hold is Active, nonempty, expires in the future, and contains unique Available seats from its session. | `SeatHold` constructor derives expiry using policy and future session start; nonempty GUIDs, inventory status/session, and Money snapshots validated. The HTTP endpoint loads authoritative inventory and claims it with version/status predicates in one transaction. |
| BR-02 | Only the owner may convert an Active hold strictly before ExpiresAt. | `EnsureCanConvert`, `Convert`, `Booking.CreateFromHold`; directly from the plan. |
| BR-03 | Active holds expire at or after ExpiresAt; owner cancellation must occur before expiry. | `Expire`/`Cancel`; exact-boundary and cancellation behavior are implementation choices. |
| BR-04 | Hold and booking item prices are immutable snapshots; TotalAmount is their sum. | SeatHold snapshots server-resolved inventory; Money.Sum enforces one currency and decimal(18,2) limits. See the money/pricing bundle. |
| BR-05 | Pending bookings may confirm, cancel, or expire; confirmed bookings may cancel. | Booking methods and transition tests. Confirmation also requires now < captured PaymentDeadline; payment/seat evidence remains Application responsibility. |
| BR-06 | Cancellation requires ownership, an eligible state, and now strictly before the captured deadline, calculated from session start minus configured lead time. | `Booking.Cancel` uses the immutable captured cutoff. Application must supply the real session at creation. |
| BR-07 | Only Pending payments succeed/fail; identical result/reference replays are harmless, conflicting results/references rejected. | Payment completion methods; local replay semantics. No cross-request key deduplication yet. |
| BR-08 | Booking expiry requires reaching the captured payment deadline after CreatedAt. | `Booking.Expire`; the configured payment window begins at booking creation and is capped at session start. |
| BR-09 | Stored instants are UTC-normalized; rules obtain current time once per operation from an injected TimeProvider. | Session/SeatHold construction, Booking factory, and deadline methods; API registers TimeProvider.System. |

Identifiers and required text are validated; zero-price Money values and derived zero-amount payments are representable, without defining a free-checkout workflow. The payment key is preserved verbatim, not normalized.

Booking numbers are globally unique in the schema; payment keys are unique within each booking and nonnull provider references are globally unique. These storage constraints do not implement idempotent request replay. Customer hold creation now authenticates the caller, loads authoritative inventory, and persists hold-item allocation versions atomically with exclusive seat claims. Unimplemented requirements include booking-sale completion, pending-booking release and public booking/payment workflows, atomic payment/booking/seat confirmation, refunds and reliable messaging. Entity methods alone cannot enforce these across concurrent requests. The [decision record](../../decisions/0001-seat-ownership-price-history-and-transitions.md) separates schema guarantees, domain rules and pending application transactions.

| Rule | Requirement | Enforcement |
| --- | --- | --- |
| BR-10 | Only the Customer policy may create holds; identity comes from authenticated `sub`. | HoldEndpoints uses ICurrentUser and ResourceOwnershipService; the established policy also allows Admin acting as their own customer. No ownership bypass. |
| BR-11 | Select 1–100 distinct nonempty session-seat IDs from a future Scheduled session of a Published event. | FluentValidation bounds the input; server lookups verify membership, auditorium and eligibility. Missing/foreign inventory and Draft/Archived events return 404; Cancelled/Completed/started sessions return 409. Empty or duplicate selections return 400 with sessionSeatIds errors before any claims. |
| BR-12 | Save all seat claims and the hold together, retaining returned fencing versions. | Shared session/event locks exclude admin edits; ordered SessionSeatAllocationWriter claims and hold inserts share one transaction. Conflicts and save errors roll back every claim. |

| Rule | Requirement | Enforcement |
| --- | --- | --- |
| BR-13 | GET/DELETE require the stored hold owner under the Customer policy; Admin has no ownership bypass. | ResourceOwnershipService after loading; another owner receives 403 resource_not_owned, missing resources 404. |
| BR-14 | DELETE cancels only Active holds strictly before captured expiry and releases only matching retained claim versions. | Hold row lock, SeatHold lifecycle guards and version-fenced Held releases in one transaction; Converted/Expired/stale/legacy cases return 409 without partial changes; an owner replay of Cancelled returns 204 without inventory writes. |
| BR-15 | GET returns stored snapshots/state without lifecycle side effects; cancellation retains history. | Read-only DTO mapping, no entity deletion and no implicit expiry on GET. |

| Rule | Requirement | Enforcement |
| --- | --- | --- |
| BR-16 | System expiry selects only due Active holds with bounded work. | HoldExpiryBatchProcessor orders (ExpiresAt, Id), limits the batch and advances/wraps its cursor; HoldExpiryReleaser rechecks under a skip-locked hold lock. |
| BR-17 | Expiry and every owned seat release commit together without refreshing tokens. | Dedicated scoped transaction per hold; session/inventory locks, retained-token writer predicates and SeatHold.Expire; invalid/legacy/stale cases roll back and log. |
| BR-18 | Expiry never releases Converted/Cancelled holds or makes started/non-Scheduled sessions Available. | Active status predicate/recheck, injected time after locks and final start-boundary recheck. |

| Rule | Requirement | Enforcement |
| --- | --- | --- |
| BR-19 | Eligibility for a new Active-hold transition closes at ExpiresAt even if the worker has not run. | SeatHold.EnsureCanConvertAt compares current UTC time directly; validation, conversion, cancellation and Booking.CreateFromHold reuse it. GET is history, not proof of current eligibility. |

| Rule | Requirement | Enforcement |
| --- | --- | --- |
| BR-20 | Completed cancellation/expiry may repeat without another release or deadline change. | Owner-checked Cancelled replay returns 204; system expiry skips non-Active holds; domain matching cleanup repeats are no-ops. |
| BR-21 | Conversion and cleanup serialize on one hold and retain allocation ownership evidence. | Hold-first locks, version-fenced Held transfers/releases, atomic booking/hold updates and separate BookingItem allocation tokens. |
| BR-22 | Stale tracked status saves cannot overwrite the winning transition. | SeatHold.Status is an EF concurrency token; a failed update rolls back the save batch. |
