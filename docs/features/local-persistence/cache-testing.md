# Cache verification

Scope: implemented cache behavior in the inspected working tree. Tests cover both the active-hold response cache and the revision-keyed public seat-map cache. PostgreSQL deadlines/state/version checks and the polling expiry worker remain authoritative.

| Area | Cases | Evidence |
| --- | --- | --- |
| Hold TTL | Remaining lifetime on refill; no renewal on hit; one tick before/at/after expiry; elapsed/terminal values not cached. | RedisHoldCacheTests; AuthEndpointsTests.HoldCaching. |
| Seat-map TTL | Short absolute, non-sliding expiry; exact cutoff; small/disabled maps bypass; real Redis TTL bounds. | SeatAvailabilityCacheTests; SeatAvailabilityCacheLiveTests. |
| Live TTL versus payload | Redis keys remain live while payload deadlines have elapsed; expired payloads are misses without pre-commit deletion. | RedisStalePayloadLiveTests.LiveKeysCannotOverrideExpiredPayloadsOrMismatchedInventoryRevisions. |
| Stale hold data | Wrong ID, malformed JSON, mismatched original deadline/prices, terminal Active snapshots, failed warm-cache removal and recovery. | RedisHoldCacheTests; AuthEndpointsTests.HoldCaching. |
| Stale seat maps | Old revision keys, wrong revision stored under the current key, inventory replacement, retained-version transfer, delayed old fills after a claim. | SeatAvailabilityCacheTests; SeatAvailabilityCacheLiveTests; RedisStalePayloadLiveTests; AuthEndpointsTests.SeatAvailabilityCaching. |
| Invalidation | Hold creation/cancellation/expiry/conversion; confirmation and held/booked release primitives; tracked repricing; unchanged revision on rollback. | AuthEndpointsTests.SeatAvailabilityCaching; AuthEndpointsTests.HoldCaching. |
| Commit order | Failed creation/read/cancellation/replay/conversion/expiry/seat-map commits cause no application cache mutation. | AuthEndpointsTests.CacheCommits. |
| Outage fallback | Real disconnected StackExchange.Redis client returns misses/skips updates; simulated I/O/socket failures; non-completing reads/writes/removals; caller cancellation and recovery. | RedisUnavailableTests; RedisTimeoutTests; AuthEndpointsTests.RedisTimeouts; AuthEndpointsTests.HoldCaching. |
| Expiry authority | Evicted/retained/unavailable cache data cannot release early or extend eligibility; worker startup/release succeeds without Redis notifications. | AuthEndpointsTests.CacheAuthority; AuthEndpointsTests.HoldExpiry; AuthEndpointsTests.HoldUse. |

## Run requirements

Run from the repository root using the selected .NET SDK. Set `TICKETBOOKING_TEST_CONNECTION_STRING` to an isolated PostgreSQL database with schema-creation privileges, and `TICKETBOOKING_TEST_REDIS_CONNECTION_STRING` to an isolated Redis instance. PostgreSQL tests create/drop unique schemas. Live Redis tests use random keys and delete only their own keys. Without the corresponding variables those integration cases skip; CI supplies both.

`RedisUnavailableTests` requires only loopback socket access. It reserves an ephemeral port without listening, so its real client is guaranteed a non-serving endpoint for the test; it does not stop or reconfigure any running Redis service. Database fallback is separately checked through HTTP workflows with failing/non-completing cache backends.

```bash
dotnet test tests/TicketBooking.IntegrationTests/TicketBooking.IntegrationTests.csproj --filter 'FullyQualifiedName~Redis|FullyQualifiedName~Cache|FullyQualifiedName~HoldExpiry|FullyQualifiedName~HoldCleanup|FullyQualifiedName~HoldUse_'
```

Deadline/timeout boundaries use controllable time and no sleeps. Live Redis tests inspect TTL, force key expiry through Redis commands and inject intentionally stale payloads; they do not depend on expiry notifications or delayed wall-clock sleeps. Generous watchdogs detect network/provider hangs, while exact budget cutoffs are verified with the timer-controllable test clock. Confirmation/release tests cover the existing fenced inventory primitives; full booking/payment confirmation workflows remain unimplemented.

Behavior references: [Redis timeout/fallback contract](redis-timeouts.md), [hold cache](../reservation-booking-payment/hold-caching.md), [seat-map cache and invalidation](../venue-catalogue-model/seat-availability-caching.md).

Verification result: all **157 selected tests passed**, with zero failures or skips, against disposable PostgreSQL 17 and Redis 8. This includes the new real disconnected-client, live stale-payload, wrong-deadline repair and failed warm-cache invalidation cases. Compilation, changed-file formatting, documentation source anchors/links and whitespace checks passed. A TRX report was generated for the verification run; developer database contents were untouched.
