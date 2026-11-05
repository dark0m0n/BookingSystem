# Business context and rules

The project plan requires expiring holds, a bounded payment flow, and cancellation before a deadline, without prescribing fixed policy values. Defaults here are configurable product choices, not requirements inferred from external rules.

| Rule | Policy | Enforcement/evidence |
| --- | --- | --- |
| BR-01 | HoldDuration > 0; default 00:10:00. | BookingTimingPolicy constructor; API startup registration binds options. |
| BR-02 | PaymentCompletionWindow > 0; default 00:15:00 from booking creation. | Policy constructor/GetPaymentDeadline. |
| BR-03 | CancellationLeadTime >= 0; default 01:00:00 before session start. | Policy constructor/GetCancellationDeadline. Zero closes at session start. |
| BR-04 | Hold/payment deadlines cannot exceed session start; creation requires a future session. | Policy CappedDeadline. |
| BR-05 | Absolute deadlines are captured at creation and are not recomputed from later configuration. | SeatHold.ExpiresAt and Booking timing properties; get-only snapshots. |
| BR-06 | Payment initiation and confirmation require now < PaymentDeadline. Expiration requires now >= PaymentDeadline. | Payment factory, Booking.Confirm/Expire, shared internal guard. |
| BR-07 | Owner cancellation requires eligible status and now < CancellationDeadline. | Booking.Cancel uses captured cutoff. |
| BR-08 | After conversion, the pending booking retains its allocation until PaymentDeadline; original hold expiry cannot release it. | Decided policy; ownership transfer/release transaction is planned. |
| BR-09 | At the cutoff, expire/release only allocations still owned by this pending booking, atomically. | Decided policy; worker/lazy-expiry/persistence implementation is absent. |
| BR-10 | Verified late success cannot restore an expired booking or reclaim seats; durable reconciliation and idempotent compensation are required. | Decided policy; callback/refund workflows are planned. |

All time comparisons use UTC instants sampled once per operation from TimeProvider. A booking may be created inside its cancellation window; it is simply not cancellable. Payment time begins at booking creation, not at payment attempt creation; retries do not extend it. There is no grace period. The [seat-retention and late-payment decision](seat-retention-and-late-payments.md) defines ownership, expiry races, and late-success compensation; execution and refund/messaging entities remain deferred. Session rescheduling still requires a future policy.
