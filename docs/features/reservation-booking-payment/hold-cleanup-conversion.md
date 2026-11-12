# Repeated cleanup and conversion races

Customer cancellation, system expiry and HoldConversionService share the same hold-first locking protocol. Repeated cleanup acknowledges completed work without releasing inventory a second time. New eligibility checks still reject an Active hold at/after its captured expiry.

| Stored hold state | Owner DELETE | System expiry | Server conversion |
| --- | --- | --- | --- |
| Active, before ExpiresAt | Cancel and release atomically, 204 | Not due; no change | Create booking, transfer and mark Converted atomically |
| Active, at/after ExpiresAt | 409 hold_expired | Expire/release once | hold_expired |
| Cancelled | 204, no inventory writes, including after original expiry | No-op | hold_not_active |
| Expired | 409 hold_expired | No-op | hold_expired |
| Converted | 409 hold_not_active | No-op | hold_not_active |

DELETE always checks the persisted owner before replaying 204, including for an Admin. A different owner receives 403 resource_not_owned even for Cancelled holds. Direct domain Cancel repeats also require ownership; domain Expire repeats are harmless only for Expired. Neither replay changes historical prices, totals, expiry or claim tokens.

## Transaction and ownership protocol

1. Start a transaction in a fresh scoped DbContext. Lock the hold FOR UPDATE before loading state; expiry uses the Active predicate and SKIP LOCKED.
2. Resolve stored ownership for customer commands and validate the current state/deadline after obtaining access. Lock the session FOR SHARE.
3. Operate on inventory in stable ID order. Each mutation matches SessionSeatId, expected Held status and the saved allocation token, and advances Version. Never replace the expected token with a newer inventory version.
4. Save every aggregate/seat change in that transaction. A partial transfer/release, stale token, save failure or deadline crossed during processing rolls back the entire command. Each expiry candidate has its own transaction.

Cancellation and expiry retain original SeatHoldItem.AllocationVersion values as history. Conversion uses SessionSeatAllocationWriter.TransferToBookingAsync (Held → Held) to advance each token, and captures the returned value on the corresponding BookingItem. New booking tokens are independent of the original hold tokens: a hold with token 1 cannot release a booking's token 2 or a later reacquired token 3. Unknown legacy tokens fail closed.

SeatHold.Status is also an EF concurrency token. A tracked save includes its original status in the UPDATE predicate. A stale caller cannot overwrite a completed cancellation/expiry/conversion or insert another booking in the same SaveChanges batch; the implicit save transaction rolls back on DbUpdateConcurrencyException. Do not blindly retry with a refreshed status/token. Row locks remain the primary command serialization protocol.

## Server conversion primitive

[HoldConversionService.ConvertAsync](../../../src/TicketBooking.Infrastructure/Persistence/HoldConversionService.cs) resolves the caller through ICurrentUser/ResourceOwnershipService, locks/revalidates the hold/session/inventory, creates the pending booking from historical hold prices and the authoritative session start, transfers all retained allocations, marks Converted and commits together. It rechecks hold expiry and the captured payment deadline before commit. Missing holds return null; invalid owner/state/deadline or allocation conflicts reject without changes. Concurrent conversions create at most one booking; repeated conversion conflicts rather than returning an existing booking.

The Customer-authorized [POST /api/bookings](booking-http-contract.md) now invokes this registered service, generates booking IDs/numbers on the server and maps an explicit BookingResponse DTO. Do not persist the domain booking factory result and consume/transfer a hold in separate transactions. New bookings store SourceHoldId with a unique index and restrictive hold reference. Legacy bookings retain null source IDs. Successful-response replay lookup, booking cancellation, payment flows and refunds remain separate work. Owner-only booking details and paginated history are implemented. [Pending-booking expiry](booking-expiry-worker.md) now releases only the booking transfer tokens at its captured deadline.

AddBookingItemAllocationVersion adds nullable bigint BookingItems.AllocationVersion with a positive-or-null check; old rows remain null/unknown. The migration snapshot also records the application-only concurrency metadata for the existing SeatHold.Status column. Apply pending migrations through README startup; no existing history or ownership is backfilled.

Tests use PostgreSQL and deterministic command gates for conversion-first, cancellation-first, expiry-first and deadline-crossing outcomes. They cover repeat cleanup after reacquisition, concurrent conversions, stale tracked saves, unknown/stale tokens, save rollback, owner checks and booking-token constraints. Conversion tests now call the production POST /api/bookings route, including its validation, authorization and DTO handling; the test-only conversion HTTP adapter has been removed.
