# Known gotchas

- **Observed:** Startup settings do not automatically expire entities or release seats. Explicit methods enforce eligibility; the API now runs bounded transactional Active-hold expiry; pending-booking expiry remains future work.
- **Observed:** The payment window begins at booking creation. Starting another attempt or receiving a delayed outcome never extends PaymentDeadline. At equality, initiation/confirmation reject and expiry becomes valid.
- **Observed:** A payment result can be Succeeded after the booking deadline, while booking confirmation rejects. The [late-success policy](seat-retention-and-late-payments.md) requires durable reconciliation and later idempotent compensation. Execution is deferred; no automatic correction is claimed.
- **Observed:** Cancellation is strictly before the captured cutoff. Bookings created after that cutoff are not cancellable even if still awaiting payment. Zero lead time allows cancellation until, but not at, session start.
- **Observed:** Existing deadlines are object snapshots. Persistence mappings now retain those snapshots, and round-trip tests verify them in an isolated database. Session rescheduling or policy migration needs an explicit future decision.
- **Observed:** Configuration is bound and validated once. Reloading settings does not update the singleton; restart is required for new policy values.
- **Inferred-risk:** A wrong session-start value from a future caller produces wrong deadlines. Application must load authoritative session data and check ownership/session linkage.
- **Observed:** Invalid duration text and invalid signs fail startup. Extremely large cancellation lead times can underflow the supported DateTimeOffset range during booking creation and are rejected by date arithmetic; no silent saturation occurs.

The decided retention/release policy is not an implemented allocation guarantee; stale hold jobs and late callbacks must never release another owner's seats. No real API startup, provider, database round-trip, scheduler, authorization flow, or concurrency behavior is proven by configuration/domain tests. Refund and messaging entities remain deferred.
