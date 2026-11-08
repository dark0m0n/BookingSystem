# Business context and rules

The project plan requires one winner when concurrent callers reserve the same seat. A status check alone is insufficient: a seat can be Held by a new allocation while a stale actor still remembers Held from an older one.

| Rule | Implemented behavior | Enforcement |
| --- | --- | --- |
| BR-01 | Version is nonnegative; initial domain default is 0. | Constructor validation and SQL nonnegative check; existing positive initial versions remain supported. |
| BR-02 | Updates/deletes must match the observed version. | EF concurrency metadata; allocation writer ID/version/status predicate. |
| BR-03 | Each tracked row update advances Version from original + 1, including non-ownership changes. | Both bool SaveChanges overrides; checked arithmetic. |
| BR-04 | Every ownership change advances the token, even Held → Held transfer. | TransferToBookingAsync; explicit tracked Modified writes also advance. |
| BR-05 | Only one allocation write succeeds for a given observed version/state. | Conditional SQL update; zero rows throws DbUpdateConcurrencyException. |
| BR-06 | Never silently retry with a newly read token. | No retry/reload in either write path; caller must revalidate owner/business rules before deciding on another operation. |

The version fences writes; it does not authorize callers or identify the current owner. Customer hold creation now persists the hold-item claim token and coordinates all claims with hold persistence. HoldConversionService now transfers retained tokens under the hold lock and captures the next versions on booking items. Pending-booking release still needs retained-token, ownership, deadline and payment-state checks. Payment refunds, cancellation eligibility, and release timing are not implemented by this writer.
