# Known gotchas

- **Observed:** Creating a Booking does not consume the hold. Repeated factories can create multiple in-memory bookings until conversion. Application must persist one booking and convert the hold atomically with durable correlation/uniqueness; that step is absent.
- **Observed:** Passing the deadline while a hold is still marked Active does not automatically change its status. Conversion and cancellation still reject at ExpiresAt; an explicit Expire call is needed to set Expired. No worker or seat release exists yet.
- **Observed:** Confirm checks only PendingPayment. It does not prove payment success, amount match, seat allocation, or an unexpired payment window. Future Application must validate these and commit seat changes atomically; do not expose the method as an unguarded endpoint.
- **Observed:** Payment result replay is safe only on the same in-memory attempt/reference. Two Payment objects can share an idempotency key; no database/provider/API deduplication exists yet.
- **Observed:** Cancellation reads current UTC time from the injected clock, accepts caller-supplied authoritative session time/deadline, and rejects at equality. No fixed cancellation/payment timeout is defined in the plan. Supplying incorrect policy inputs can yield incorrect eligibility; resolve and persist the policies in Application.
- **Observed:** Cancelling a paid booking does not refund a payment or release seats. Refunded enums are reserved, and Refund/messaging entities are deferred to their phases.
- **Inferred-risk:** Collection snapshots protect historical totals, not caller-supplied price authenticity or globally unique seat holds. Server-side lookups and database concurrency/transactions are still required.

The transition interpretation, exact equality boundaries, no-reopen failed payments, and duplicate callback policy are explicit design choices derived from the plan's flows. Detailed timing/compensation policies remain open. Clock-driven UTC and exact-deadline behavior is unit-tested. No real provider, database round-trip, HTTP authorization, background expiry, concurrent request, or webhook behavior is verified by domain tests.
