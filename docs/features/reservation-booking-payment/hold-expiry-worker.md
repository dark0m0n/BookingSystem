# Active hold expiry worker

Program registers HoldExpiryWorker as a BackgroundService. It starts an immediate pass, then waits using injected TimeProvider and the shutdown token. Configuration is validated once at startup:

| Setting | Default | Bounds |
| --- | --- | --- |
| HoldExpiry:BatchSize | 100 | 1–1000 holds per pass |
| HoldExpiry:PollInterval | 00:00:05 | One second through one hour, inclusive |

Environment overrides are HoldExpiry__BatchSize and HoldExpiry__PollInterval. Values do not reload at runtime. Migrations must be applied before API startup; a missing database/schema is logged and retried after the interval rather than silently stopping the host.

HoldExpiryBatchProcessor queries only Active holds with ExpiresAt <= server UTC time, ordered by (ExpiresAt, Id), using Take(BatchSize). A keyset cursor advances past every examined candidate, including failed/locked holds; reaching the end wraps the next pass to the beginning. The cursor is process-local, so restarts resume at the beginning. This prevents poison legacy/stale rows from indefinitely blocking later expirations without an unbounded drain loop.

Each candidate uses a new async DI scope and HoldExpiryReleaser transaction:

1. Lock the Active hold using FOR UPDATE SKIP LOCKED. Locked or no-longer-Active holds are skipped. Load items and recheck captured expiry against injected time.
2. Lock the session FOR SHARE. Reject empty items, null/invalid claim tokens or inventory from another session. Lock selected inventory IDs in stable order before deciding release state.
3. Apply SeatHold.Expire with current injected time; release each expected Held allocation using its retained version through SessionSeatAllocationWriter. Never refresh the expected token from current inventory.
4. Save Expired status and releases together. Future Scheduled sessions release to Available; started, Cancelled or Completed sessions release to Unavailable. If a session starts during processing after choosing Available, roll back and reconsider on a later pass.
5. Commit, retaining hold/items, historical prices, total, original expiry and original claim tokens. Inventory versions advance on release. Failure rolls back this candidate; other holds keep independent transactions and continue.

The worker is a system lifecycle operation and has no HTTP caller/ownership bypass. Owner cancellation and HoldConversionService serialize transitions using the same hold lock. Converted/Cancelled/Expired holds and not-yet-due Active holds are not expired. A newer claim/booking transfer is fenced by its advanced inventory version even if a stale header still says Active.

Allocation conflicts (including unknown legacy tokens) are logged as warnings; unexpected per-hold failures or scan failures are logged as errors. Conflicting holds remain Active with seats unchanged for safe reconciliation/retry. The polling cursor continues past them; the worker never invents ownership or extends deadlines. Shutdown cancels scans, waits and in-flight SQL; incomplete transactions roll back.

IndexActiveHoldExpirations adds IX_SeatHolds_ExpiresAt_Id over (ExpiresAt, Id), filtered to Status = 0. It changes no hold data or ownership tokens. Apply it with the existing README migration procedure. Owner GET may observe an elapsed Active hold before a worker pass commits; availability stays allocated until the release transaction commits. A server conversion service now uses that protocol; public booking endpoints, pending-booking expiry, refunds and providers remain separate work.

Tests: AuthEndpointsTests.HoldExpiry uses real PostgreSQL plus manual time for exact expiry/session-start edges, bounded tied-deadline cursor batches, failed-record isolation/starvation, stale tokens, multi-worker skip-locked behavior, reacquisition fencing, shutdown and rollback. HoldExpiryConfigurationTests validates registration, bounded settings and scope lifetimes; PersistenceModelTests checks index/model metadata.
