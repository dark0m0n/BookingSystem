# Known gotchas

- **Observed:** A version token is not an owner ID or authorization credential. Durable allocation owner/claim-version links and complete hold/booking transactions are still absent.
- **Observed:** Held→Held conversion needs TransferToBookingAsync or an explicit tracked Modified write. Assigning the same Status without marking a change produces no tracked write/token advance.
- **Observed:** Set-based writes bypass EF tracking. Use the allocation writer's explicit version/state predicate; raw SQL/ExecuteUpdate/ExecuteDelete elsewhere do not gain concurrency checks automatically.
- **Observed:** A stale expiry/release fails after transfer or after release/reacquire, even if the current status is again Held. Do not refresh its expected token to force it through.
- **Observed:** Price-only/other tracked edits also advance Version, potentially causing conservative conflicts for legitimate allocation callers. Revalidation, not blind retry, is required.
- **Observed:** ExecuteUpdate does not update tracked objects. Reload intentionally for a fresh business decision; do not later save stale tracked values and expect them to succeed.
- **Observed:** Multiple allocation calls are not automatically one transaction. Future use cases must wrap all seat and hold/booking changes together and roll back on any conflict.
- **Observed:** API error middleware maps allocation conflicts to HTTP 409 `seat_not_available`; generic EF conflicts use `concurrency_conflict`. Business endpoints, workers, owner-aware release workflows, payment coupling, and refunds remain absent. An error response does not roll back an application transaction; the caller must do that.

PostgreSQL tests establish row-level fencing and race outcomes, not authenticated end-to-end bookings. Development data was not migrated or queried, and no production transaction was executed for verification.
