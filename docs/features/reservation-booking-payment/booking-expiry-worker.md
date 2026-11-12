# Pending booking retention and expiry

Implemented in the working tree. Program registers BookingExpiryWorker automatically. Converted hold allocations remain Held under the booking's captured transfer tokens until confirmation or expiry; original hold expiry cannot release them. PaymentDeadline is captured from BookingTiming:PaymentCompletionWindow at conversion, capped at session start. Neither retries nor later configuration changes recalculate it.

## Processing and transaction

BookingExpiryBatchProcessor selects PendingPayment rows with PaymentDeadline <= TimeProvider.GetUtcNow(), ordered by (PaymentDeadline, Id), in bounded keyset batches. Each candidate gets a new scope and transaction. Locked/failed candidates advance the cursor so they cannot starve later work; scans wrap to retry them with their original tokens.

BookingExpiryReleaser locks the PendingPayment booking FOR UPDATE SKIP LOCKED, reloads items and checks the stored deadline. It locks the session FOR SHARE and inventory rows in stable ID order. Missing, unknown or stale item tokens fail closed. Booking.Expire checks fresh injected time after locks; retained-version Held → Available/Unavailable writes and Expired status commit in one transaction. Save/commit/conflict failures roll back every write. If the session starts during an Available release, the transaction rolls back and a later pass releases to Unavailable.

Seats become Available only for a future Scheduled session; started, Cancelled or Completed sessions release to Unavailable. Original booking/source-hold prices, totals, deadlines and allocation tokens remain history. Repeated expiry and competing workers never release a successor's allocation. Booking.Status is an EF concurrency token, preventing stale tracked confirmation from overwriting expiry. Future confirmation commands must also serialize on the booking and use the retained allocation evidence; provider callbacks remain unimplemented.

Confirmed, Cancelled, Expired and reserved Refunded states are ignored. Expiry does not change payment attempt outcomes, charge/refund anything, or modify the Converted source hold. Unknown legacy booking claim tokens remain pending and allocated, with a warning, until safely reconciled; tokens are never refreshed to force cleanup.

## Settings and operation

BookingExpiry:BatchSize defaults to 100 (allowed 1–1000). BookingExpiry:PollInterval defaults to 00:00:05 (allowed one second through one hour). Settings bind/validate once at startup; environment overrides use BookingExpiry__BatchSize and BookingExpiry__PollInterval. These control cleanup work, not the payment window.

The worker runs immediately, then waits using injected TimeProvider and cancellable delays. It captures no scoped DbContext or caller identity. PostgreSQL is authoritative; Redis TTLs/notifications do not schedule or authorize expiry. Seat-map revisions change with inventory versions, so existing revision checks invalidate stale availability. Cleanup may lag the deadline, during which seats remain unavailable and confirmation/payment initiation still reject at the cutoff. No lazy-expiry or provider reconciliation flow is implemented.

Apply IndexPendingBookingExpirations through README startup. It adds IX_Bookings_PaymentDeadline_Id filtered to Status = 0; the snapshot also records application-only Booking.Status concurrency metadata. No deadline or price backfill occurs. Development/deployed migration state is not verified by tests against disposable schemas.

## Evidence

- API: BookingExpiryWorker, BookingExpiryBatchProcessor and BookingExpiryOptions under src/TicketBooking.Api/Booking/.
- Persistence: BookingExpiryReleaser and BookingConfiguration under src/TicketBooking.Infrastructure/Persistence/.
- Tests: AuthEndpointsTests.BookingExpiry checks exact cutoffs, source-hold independence, retry retention, state guards, owned-token release, rollback, concurrent workers, successor fencing and hosted-service lifecycle. BookingExpiryConfigurationTests checks bounded settings and scoped lifetimes; PersistenceModelTests checks the index/concurrency metadata and snapshot consistency.
