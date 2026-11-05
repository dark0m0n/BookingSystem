# Known gotchas

- **Observed:** decimal(18,2) is enforced in domain constructors/results and now mapped in PostgreSQL alongside required currency. Direct SQL can still bypass domain supported-currency/nonnegative checks and numeric can coerce fractional precision; see [database constraints](../local-persistence/data-model.md).
- **Observed:** Only UAH, EUR, USD are supported. Supporting currencies with different minor-unit precision requires a deliberate policy change; merely accepting arbitrary three-letter codes is insufficient.
- **Observed:** Inputs such as 1.235 are rejected rather than rounded. Future tax/discount/allocation features must define an explicit rounding policy before producing Money values.
- **Observed:** Even valid item prices can sum beyond MaxAmount. Hold construction rejects such totals before a booking/payment is created.
- **Observed:** Constructors consume trusted server objects, not a repository. No HTTP endpoint or authoritative lookup exists; future handlers must load inventory/bookings from storage and reject or ignore client totals.
- **Observed:** Publicly immutable Money references could previously be shared across catalogue/snapshot/total fields and altered through EF complex-property edits. Capture boundaries and even single-item sums now make independent copies; real PostgreSQL repricing regressions cover this. Arbitrary deliberate historical writes are not prohibited by a database trigger.
- **Inferred-risk:** Local Available/session checks do not prevent concurrent holds or double sales. Database transactions, unique constraints, and version checks remain required.
- **Unverified integration:** Provider payloads, amount comparison in callbacks, and real checkout behavior have not been exercised. Stored amount/currency and price-history isolation are covered by PostgreSQL tests. Money unit tests do not prove charging or settlement correctness.

No exchange conversion, taxes, promotional pricing, fee allocation, refund processing, or frontend total calculation is implemented. Existing booking confirmation and idempotency limitations remain documented in the lifecycle bundle.
