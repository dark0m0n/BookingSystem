# Data model

Pending-booking cleanup queries stored PaymentDeadline and PendingPayment status through the filtered (PaymentDeadline, Id) index from IndexPendingBookingExpirations. Booking.Status is an EF concurrency token. Expiry preserves all captured timestamps/prices and historical allocation tokens; only status and inventory version/status change.

| Value | Type | Meaning |
| --- | --- | --- |
| BookingTimingPolicy.HoldDuration | TimeSpan, get-only | Positive maximum hold lifetime. |
| PaymentCompletionWindow | TimeSpan, get-only | Positive maximum time from booking creation to confirmation. |
| CancellationLeadTime | TimeSpan, get-only | Nonnegative interval before session start at which cancellation closes. |
| SeatHold.ExpiresAt | UTC DateTimeOffset, get-only | Captured policy result, capped at session start. |
| Booking.SessionStartsAt | UTC DateTimeOffset, get-only | Authoritative session-start snapshot supplied at creation. |
| Booking.PaymentDeadline | UTC DateTimeOffset, get-only | Captured booking creation plus payment window, capped at session start. |
| Booking.CancellationDeadline | UTC DateTimeOffset, get-only | Captured session start minus cancellation lead time. May precede CreatedAt. |

BookingTimingOptions uses the same three duration names with setters only for startup binding. No policy/clock reference is retained on entities. Existing monetary values, IDs, statuses, and snapshot collections remain unchanged.

The [retention decision](seat-retention-and-late-payments.md) additionally requires future durable hold-to-booking correlation, allocation ownership checks, a confirming-payment association, and a reconciliation record/action. These mappings are requirements, not existing fields or entities.

The [persistence migration](../local-persistence/data-model.md) now stores absolute deadline/session snapshots rather than recomputing them. It was applied only to an isolated PostgreSQL test database; development/deployed state is Not verified. UserId ownership and data-retention policies are unchanged; timing configuration is not secret data.
