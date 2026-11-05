# End-to-end flow and transitions

Trigger: trusted code creates a hold using authoritative user/session IDs, selected server-resolved SessionSeat objects, authoritative session start, and injected TimeProvider/BookingTimingPolicy. Here `now` means the operation's single UTC clock reading (BR-09). There is no HTTP/worker entry yet. The [seat-retention decision](../booking-timing/seat-retention-and-late-payments.md) defines planned allocation transfer at conversion, expiry release, and late-payment handling.

1. `hold-create` starts Active with a captured policy-derived expiration capped at session start and read-only items (BR-01/04). The constructor checks local availability/session membership, snapshots prices, and calculates its Money total. A future application transaction must load authoritative inventory and revalidate concurrent availability.
2. `booking-from-hold` reads the clock once, checks ownership, Active status, and now < ExpiresAt, copies SessionSeatId/PriceSnapshot, calculates TotalAmount, and captures SessionStartsAt/PaymentDeadline/CancellationDeadline (BR-02/04). It leaves the hold unchanged. Application must persist the booking and call `hold-convert` atomically; this orchestration is not implemented.
3. `payment-create` uses CreateForBooking to start Pending from a PendingPayment booking, deriving Amount/currency from its total; callers supply payment ID, IdempotencyKey, and TimeProvider. The booking must still be before its captured PaymentDeadline. `payment-reference` can record a provider operation while waiting. No provider is called.
4. `payment-succeed` or `payment-fail` records the outcome (BR-07). Identical terminal outcomes for the same reference are no-ops. Application must validate the provider, amount, booking linkage, and timeout before invoking `booking-confirm` and committing seat updates; no such workflow exists yet.
5. `hold-expire`, `hold-cancel`, `booking-expire`, and `booking-cancel` implement local alternatives (BR-03/05/06/08). They do not release seats, issue refunds, or send events. Planned release must be atomic with booking expiry and check booking ownership; original hold expiry never releases a converted booking's seats.

## Allowed transitions

| Entity | From | To | Guard |
| --- | --- | --- | --- |
| SeatHold | Active | Converted | Owner; now < ExpiresAt. |
| SeatHold | Active | Cancelled | Owner; now < ExpiresAt. |
| SeatHold | Active | Expired | now >= ExpiresAt. |
| Booking | PendingPayment | Confirmed | Pending state and now < captured PaymentDeadline; caller must establish successful payment and seat allocation. |
| Booking | PendingPayment | Cancelled | Owner; now < captured CancellationDeadline <= SessionStartsAt. |
| Booking | Confirmed | Cancelled | Same cancellation guard; no refund side effect yet. |
| Booking | PendingPayment | Expired | CreatedAt < captured PaymentDeadline <= now. |
| Payment | Pending | Succeeded or Failed | Nonblank, matching provider reference. |
| Payment | Succeeded / Failed | Same state | Same reference; no-op replay, not a new transition. |

All other public changes throw DomainRuleException carrying a DomainError (argument violations use ArgumentException/ArgumentOutOfRangeException). Terminal hold and booking operations reject repeated calls. Pending provider-reference updates must match any reference already recorded. Failed payments do not reopen; another attempt requires a still-pending booking before its payment deadline. Durable retry orchestration remains application work.

Booking/Payment Refunded values are reserved; refund transitions and entities are deferred. No transaction, async boundary, cancellation-release policy, event publication, or human handoff is implemented. See [entry points](entry-points.json) for source anchors.
