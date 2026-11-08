# End-to-end flow and transitions

Trigger: trusted code creates a hold using authoritative user/session IDs, selected server-resolved SessionSeat objects, authoritative session start, and injected TimeProvider/BookingTimingPolicy. Here `now` means the operation's single UTC clock reading (BR-09). `hold-http-create` is the implemented customer HTTP entry. `hold-expiry-worker` now drives Active hold expiry; pending-booking expiry remains absent. The [seat-retention decision](../booking-timing/seat-retention-and-late-payments.md) defines planned allocation transfer at conversion, expiry release, and late-payment handling.

1. `hold-create` starts Active with a captured policy-derived expiration capped at session start and read-only items (BR-01/04). The constructor checks local availability/session membership, snapshots prices, and calculates its Money total. The HTTP workflow loads inventory, snapshots prices, claims seats in stable ID order through the fenced writer, stores returned allocation versions, inserts the hold/items and commits atomically. Shared session/event locks prevent schedule or publication edits during validation and allocation. Failure rolls back the whole request; no automatic retry occurs (BR-10/11/12).
2. `booking-from-hold` reads the clock once, checks ownership, Active status, and now < ExpiresAt, copies SessionSeatId/PriceSnapshot, calculates TotalAmount, and captures SessionStartsAt/PaymentDeadline/CancellationDeadline (BR-02/04). It leaves the hold unchanged. HoldConversionService now orchestrates factory creation, retained-version Held → Held transfer, token capture and hold conversion in one transaction; no public booking route is added.
3. `payment-create` uses CreateForBooking to start Pending from a PendingPayment booking, deriving Amount/currency from its total; callers supply payment ID, IdempotencyKey, and TimeProvider. The booking must still be before its captured PaymentDeadline. `payment-reference` can record a provider operation while waiting. No provider is called.
4. `payment-succeed` or `payment-fail` records the outcome (BR-07). Identical terminal outcomes for the same reference are no-ops. Application must validate the provider, amount, booking linkage, and timeout before invoking `booking-confirm` and committing seat updates; no such workflow exists yet.
5. `hold-expire`, `hold-cancel`, `booking-expire`, and `booking-cancel` implement local alternatives (BR-03/05/06/08). The owner DELETE workflow now atomically releases a cancellable hold’s seats. The expiry worker also releases due Active hold allocations transactionally. Other local transitions do not release inventory, issue refunds or send events. Planned release must be atomic with booking expiry and check booking ownership; original hold expiry never releases a converted booking's seats.

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

All other public changes throw DomainRuleException carrying a DomainError (argument violations use ArgumentException/ArgumentOutOfRangeException). Matching hold cancellation/expiry repeats are no-ops; other terminal hold transitions and repeated booking transitions reject. Pending provider-reference updates must match any reference already recorded. Failed payments do not reopen; another attempt requires a still-pending booking before its payment deadline. Durable retry orchestration remains application work.

Booking/Payment Refunded values are reserved; refund transitions and entities are deferred. Hold creation uses one asynchronous PostgreSQL transaction. Booking/payment transactions, booking expiry/cancellation release, event publication and human handoff remain absent. See [entry points](entry-points.json) for source anchors.

Owner HTTP flow (`hold-http-get`, BR-13/15): load stored hold/items → require ownership → return the historical HoldResponse. Missing holds return 404; another owner, including Admin, receives 403 resource_not_owned.

Owner cancellation (`hold-http-delete`, BR-13/14): lock hold → load/check ownership → lock session → validate state/deadline and retained tokens → release all matching Held allocations → mark Cancelled after lock waits → save/recheck expiry → commit 204. Conflict or save failure rolls back releases and status. Repeated owner cancellation of Cancelled returns 204 without inventory writes; expiry equality returns hold_expired. GET continues to return retained cancellation history.

System expiry (`hold-expiry-worker` → `hold-expiry-batch` → `hold-expiry-release`, BR-16/17/18): select a bounded due batch → advance ordered cursor → fresh scope/transaction per candidate → skip-locked hold/state/time check → session and ordered inventory locks → retained-version release plus Expired status → save/final start-boundary check → commit. On failure only this candidate rolls back; later candidates continue. Polling wraps to retry failed/locked candidates and stops promptly on shutdown. See [worker details](hold-expiry-worker.md).

Server conversion (`hold-persisted-conversion`, BR-21/22) locks the same hold, revalidates current owner/state/time, transfers each saved allocation to a new booking token, stores snapshots/booking and marks Converted atomically. Cleanup that loses the hold lock observes Converted and cannot release those tokens. If cleanup wins, conversion rejects; if a conversion crosses expiry or fails, all transfers and booking rows roll back so later cleanup can proceed. See [race and replay rules](hold-cleanup-conversion.md).
