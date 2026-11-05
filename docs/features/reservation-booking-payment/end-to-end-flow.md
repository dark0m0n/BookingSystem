# End-to-end flow and transitions

Trigger: trusted code creates a hold using authoritative user/session IDs, selected price snapshots, and an injected TimeProvider. Here `now` means the operation's single UTC clock reading (BR-09). There is no HTTP/worker entry yet.

1. `hold-create` starts Active with a future expiration and read-only items (BR-01/04). Selected-seat availability and membership in the session must be checked by a future application transaction.
2. `booking-from-hold` reads the clock once, checks ownership, Active status, and now < ExpiresAt, copies SessionSeatId/PriceSnapshot, and calculates TotalAmount (BR-02/04). It leaves the hold unchanged. Application must persist the booking and call `hold-convert` atomically; this orchestration is not implemented.
3. `payment-create` starts Pending for BookingId, Amount, and IdempotencyKey. `payment-reference` can record a provider operation while waiting. No provider is called.
4. `payment-succeed` or `payment-fail` records the outcome (BR-07). Identical terminal outcomes for the same reference are no-ops. Application must validate the provider, amount, booking linkage, and timeout before invoking `booking-confirm` and committing seat updates; no such workflow exists yet.
5. `hold-expire`, `hold-cancel`, `booking-expire`, and `booking-cancel` implement local alternatives (BR-03/05/06/08). They do not release seats, issue refunds, or send events.

## Allowed transitions

| Entity | From | To | Guard |
| --- | --- | --- | --- |
| SeatHold | Active | Converted | Owner; now < ExpiresAt. |
| SeatHold | Active | Cancelled | Owner; now < ExpiresAt. |
| SeatHold | Active | Expired | now >= ExpiresAt. |
| Booking | PendingPayment | Confirmed | Pending state; caller must establish successful payment and seat allocation. |
| Booking | PendingPayment | Cancelled | Owner; now < cancellationDeadline <= sessionStartsAt. |
| Booking | Confirmed | Cancelled | Same cancellation guard; no refund side effect yet. |
| Booking | PendingPayment | Expired | CreatedAt < paymentDeadline <= now. |
| Payment | Pending | Succeeded or Failed | Nonblank, matching provider reference. |
| Payment | Succeeded / Failed | Same state | Same reference; no-op replay, not a new transition. |

All other public changes throw InvalidOperationException (argument violations use ArgumentException/ArgumentOutOfRangeException). Terminal hold and booking operations reject repeated calls. Pending provider-reference updates must match any reference already recorded. Failed payments do not reopen; a new attempt, if allowed later, requires application policy.

Booking/Payment Refunded values are reserved; refund transitions and entities are deferred. No transaction, async boundary, cancellation-release policy, event publication, or human handoff is implemented. See [entry points](entry-points.json) for source anchors.
