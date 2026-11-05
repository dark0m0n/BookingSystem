# End-to-end pricing flow

Trigger: trusted server code supplies selected SessionSeat inventory objects. A future API must accept seat IDs and load these objects from authoritative storage; that lookup is not implemented.

1. `money-create` validates a price and currency (BR-01/02/03). `SessionSeat.Price` stores this immutable value.
2. `hold-pricing` checks nonempty selected seats, matching SessionId, Available status, and no duplicate session-seat IDs. It creates SeatHoldItems with independent Money amount/currency copies from seat IDs/current prices; no request snapshot or total is accepted (BR-05).
3. The hold computes TotalAmount with `money-sum`. Mixed currencies or an out-of-range sum reject hold creation (BR-01/04). Input collections are copied.
4. `booking-pricing` checks hold ownership/expiry, copies historical Money amounts/currencies into independent snapshots, and recomputes its TotalAmount (BR-06). Later inventory representations do not change the captured values.
5. `payment-pricing` requires a PendingPayment booking before its captured PaymentDeadline and captures an independent copy of its calculated total, including currency. It accepts an idempotency key but no caller amount (BR-06).

Malformed amounts/currencies, missing Money, and invalid selections reject before a valid object is returned. Existing lifecycle rules and injected clocks remain in effect. Booking creation does not convert the hold; application coordination must still be atomic.

There are no HTTP handlers, stateful repricing jobs, transaction boundaries, provider calls, or automatic retries in this change. Provider-reported amount/currency validation, database locking, and payment idempotency remain unimplemented. See [entry points](entry-points.json) and [lifecycle flow](../reservation-booking-payment/end-to-end-flow.md).

See the [snapshot contract](price-snapshots.md) for capture timing, expired-hold handling, financial query rules, and real PostgreSQL repricing verification (BR-07).
