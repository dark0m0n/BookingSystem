# Seat retention after hold conversion

Decision: **retain seats until the booking's captured PaymentDeadline**. The policy is decided; allocation transfer, database expiry, and compensation are **planned**, not implemented. Working tree inspected on top of `5ba7f54`, including the existing uncommitted timing/Money work.

## Retention window

Successful conversion must atomically create one PendingPayment booking, mark its source hold Converted, and transfer all selected seat allocations from that hold to the booking. Seats remain Held and unavailable to other customers throughout conversion; there is no intermediate Available state. Here Held covers a temporary allocation owned by either an active hold or a pending booking. Booked means confirmed inventory.

The retention cutoff is:

```text
PaymentDeadline = min(Booking.CreatedAt + PaymentCompletionWindow, SessionStartsAt)
```

The default is 15 minutes from booking creation, capped at session start. It is independent of the original hold's ExpiresAt after conversion. There is no extra grace period. Starting/retrying a payment, receiving Pending, or changing configuration does not reset the deadline. A failed attempt also leaves the booking's allocation intact until its deadline, allowing a new attempt under the same cutoff; the failed attempt itself never reopens.

Example with a session at 20:00 UTC: a hold created at 18:00 expires at 18:10. Conversion and booking creation at 18:08 capture a payment deadline of 18:23. The booking retains the seats after 18:10, until confirmation or expiry at 18:23. Conversion must complete the validity check strictly before the hold cutoff. The existing separate domain methods do not implement this transaction.

## Expiry and allocation release

These are requirements for the future application/persistence workflow:

1. An expiry worker, or an explicit lazy-expiry path during an availability/reservation request, selects PendingPayment bookings with PaymentDeadline <= current server UTC time. Neither trigger exists yet.
2. In a database transaction, serialize access to the booking and its seat allocations using locks or concurrency/version checks. Re-read state and sample TimeProvider after acquiring access; never use a time sampled before waiting for a lock.
3. If still pending and due, change the booking to Expired and release only allocations still owned by this booking. Commit the status and inventory changes together. Never clear another hold/booking's allocation just because the seat is Held.
4. Released inventory may become Available only before session start. At/after session start it remains nonbookable (the planned inventory state is Unavailable).
5. If already Confirmed, Cancelled, or Expired, expiry is a no-op for that workflow. A stale hold-expiry job must ignore Converted holds and cannot release the pending booking's seats.

Worker delay does not extend eligibility: confirmation rejects at the deadline even if the stored booking still says PendingPayment. Inventory remains unavailable until its expiry/release transaction commits; availability responses must not advertise it as free merely because wall time passed. Lazy expiry may perform that transaction to avoid waiting for the worker.

The cancellation cutoff applies to customer cancellation, not system expiry. Expiry may release an unpaid booking even when its cancellation window has closed. Refund/release rules for cancelling a paid booking remain a separate phase.

## Payment result and deadline race

Before changing business state, future callback processing must authenticate/verify the provider result, identify the existing payment attempt/booking, compare amount and currency with the stored Money value, and deduplicate the provider operation. Provider timestamps do not override the booking deadline.

| Verified outcome and booking state | Required handling |
| --- | --- |
| Success, PendingPayment, decision time < PaymentDeadline, all allocations still owned | Record Succeeded and atomically confirm the booking and change its seats Held → Booked. Record which payment caused confirmation. |
| Pending or failure before the deadline | Record the attempt outcome when applicable; keep the booking/allocation pending until the same deadline or an eligible cancellation. No extension. |
| First success while PendingPayment at/after PaymentDeadline | Record Succeeded, expire/release the booking under the transaction rules, and require compensation. Never confirm. |
| First success for an Expired or Cancelled booking | Record the financial result and require compensation. Do not restore the booking or reacquire seats, even if they appear free. |
| Late pending/failure for an already expired booking | Record applicable attempt state; leave booking and inventory unchanged. Do not invent a failure just because the booking expired. |
| Duplicate success, booking still Confirmed, same confirming payment | Return the prior outcome without re-confirming, releasing seats, or refunding that valid payment, even after the deadline. |
| Repeated success already recorded as late/compensation-required | Replay the recorded outcome; resume any outstanding reconciliation/refund action idempotently, without creating another charge/refund or changing inventory. |
| Success for another payment when the booking is already confirmed | Treat as an extra charge requiring reconciliation/compensation; retain the original confirmed booking. |
| Contradictory terminal results, mismatched money/reference, or missing ownership | Do not overwrite a terminal result or confirm/release another owner's seats. Reconcile verified financial discrepancies; untrusted callbacks cannot authorize compensation. |

The decision time is the server clock reading after serialization, immediately before the conditional transition. At exact equality, confirmation is rejected and expiry is eligible. If valid confirmation wins before the cutoff, its transaction must prevent an expiry worker from subsequently releasing those seats, even if commit finishes after the cutoff. A request received before the deadline but processed after it cannot use its arrival time to claim eligibility.

An external provider call cannot be rolled back with a database transaction. Retain verified financial outcomes and the required next action durably; throwing away the callback when confirmation fails would hide a charge. Persistent callback deduplication and retry/reconciliation must survive process restarts. Same-instance Payment replay handling is not sufficient.

## Late successful payments

Decision: compensate a verified successful charge that did not confirm this booking. Until the refund phase exists, the payment integration must retain a durable reconciliation requirement for the attempt; logs alone are insufficient. This is a planned persistence/application obligation, not a new entity or working queue in the current code.

When refund processing begins, request the full verified charge back once per extra/late successful payment attempt, with an idempotent provider/refund key. Use the stored payment amount/currency in the normal matching-result case, not a newly calculated catalogue price. Resolve mismatched financial facts through reconciliation before acting. Do not create duplicate refund effects for repeated callbacks. Refund failure must remain retryable/visible and must never restore seat ownership.

For a booking previously confirmed and then cancelled, the later cancellation/refund workflow owns any refund; a duplicate success callback must not create an additional refund.

The booking remains Expired or Cancelled after compensating its late payment. A future Payment refund transition can record the financial correction; it does not turn an expired booking into a refunded, formerly confirmed booking. Refund, OutboxMessage, and ProcessedMessage entities remain deferred to their planned phases.

## Current evidence and implementation gaps

- [BookingTimingPolicy.GetPaymentDeadline](../../../src/TicketBooking.Domain/BookingTimingPolicy.cs) calculates the capped booking window; [Booking](../../../src/TicketBooking.Domain/Booking/Booking.cs) captures PaymentDeadline and prevents late Confirm/payment initiation, while Expire permits the exact cutoff.
- [SeatHold](../../../src/TicketBooking.Domain/Reservation/SeatHold.cs) allows only Active → Converted/Expired/Cancelled, so its Expire method rejects Converted. Booking.CreateFromHold alone does not convert the hold.
- [Payment](../../../src/TicketBooking.Domain/Payment/Payment.cs) records Succeeded/Failed and identical same-reference replays independently of booking state. It does not confirm, release, or refund.
- [BookingTimingPolicyTests](../../../tests/TicketBooking.Domain.Tests/BookingTimingPolicyTests.cs) covers deadline guards; [SeatHoldTests](../../../tests/TicketBooking.Domain.Tests/SeatHoldTests.cs) covers converted/terminal rejection. Test source was inspected, not executed for this documentation-only change.

No allocation-owner mapping, durable source-hold correlation, confirming-payment association, worker, lazy-expiry handler, transaction coordinator, webhook, reconciliation record, refund, or messaging implementation exists. SessionSeat now has private persistence setters, relational identity constraints, and [version-fenced allocation writes](../session-seat-concurrency/FEATURE.md). These provide row-level checks, including Held→Held transfer; owner links and the full transaction/worker remain absent. The planned race/release policy above is not an implemented double-booking guarantee.

Future acceptance checks must cover conversion surviving original hold expiry, retry without deadline extension, atomic expiry/release, simultaneous expiry/callback, slow lock acquisition, duplicate callbacks after confirmation, compensation exactly once, and a late callback after another customer acquired the released seat. These cross-request tests are not implemented.
