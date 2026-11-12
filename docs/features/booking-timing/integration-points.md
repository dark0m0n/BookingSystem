# Integration points

BookingTimingPolicy supplies the immutable payment deadline during conversion. BookingExpiryBatchProcessor reads that persisted cutoff; BookingExpiryReleaser rechecks time/state under booking/session/inventory locks and releases using retained BookingItem tokens. See [worker contract](../reservation-booking-payment/booking-expiry-worker.md). Payment provider callbacks remain separate work.

Operator configuration → API AddBookingTiming → singleton BookingTimingPolicy → domain factories is entirely in-process. ASP.NET configuration precedence applies: appsettings.json defaults can be overridden by environment-specific settings, User Secrets in Development, environment variables, or command-line configuration. Environment keys are BookingTiming__HoldDuration, BookingTiming__PaymentCompletionWindow, BookingTiming__CancellationLeadTime. Values use invariant TimeSpan syntax such as 00:10:00.

The host API does not automatically read Compose .env. Export timing variables to the API/EF process environment or use supported configuration sources. Invalid duration text or ranges fail before serving requests. Config reload does not replace the registered instance; restart to change policy for new objects.

[Lifecycle entities](../reservation-booking-payment/FEATURE.md), [Money/pricing](../money-and-pricing/FEATURE.md), and [UTC conventions](../../time-conventions.md) define neighboring contracts. Future Application must validate that sessionStartsAt belongs to the hold/booking session, and persist/coordinate state atomically. The timing policy performs no lookup or network call.

Payment result recording remains distinct from booking confirmation. Late successful provider reports follow the [decided reconciliation/compensation policy](seat-retention-and-late-payments.md); implementation remains future work. The expiry worker performs only database lifecycle cleanup; no webhook, refund, event, message broker or external retry behavior is implemented.
