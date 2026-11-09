# Active hold caching

Status: implemented in the inspected working tree. PostgreSQL is the source of truth; Redis caches only the response snapshots of Active holds whose `ExpiresAt` is strictly after the injected `TimeProvider` reading. No schema migration is required.

## Flow and authority

- Creation (`HoldEndpoints.CreateAsync`) commits the hold, item snapshots and fenced allocations before calling `IHoldCache.SetAsync`. A failed database transaction never fills Redis.
- Owner GET loads the persisted hold header and item snapshots within RepeatableRead, checking `ResourceOwnershipService.EnsureHoldOwner` before consulting Redis for an Active unelapsed hold. A cache hit avoids loading item rows and commits before returning; a miss loads those rows, commits, then fills or removes the cache entry. A failed read commit performs no cache mutation. Missing resources remain 404 and another owner, including Admin, still receives 403 before cache access.
- Cancellation, `HoldConversionService.ConvertAsync` and `HoldExpiryReleaser.ExpireAsync` remove the key after their transaction commits. Authorized cancellation replay also retries removal. Failed transitions do not invalidate valid Active snapshots.
- An elapsed or terminal hold is never cached or returned from Redis. GET still returns its historical database state, including Active with an elapsed deadline before worker cleanup. Redis TTL expiration does not release seats or mutate PostgreSQL.

## Key, payload and lifetime

`RedisHoldCache` uses `hold:{holdId:D}`, without a configurable prefix. The .NET Redis provider stores serialized UTF-8 `HoldResponse` JSON in the `data` field of a Redis hash with expiry metadata. It includes hold/session IDs, original expiry, stored status, total/currency and ordered seat price snapshots. It excludes owner IDs, credentials and allocation tokens.

Remaining lifetime is `ExpiresAt - clock.GetUtcNow()`. Nonpositive lifetimes remove rather than write. The provider receives `AbsoluteExpiration = ExpiresAt`, with no relative or sliding expiration; cache hits and refills cannot restart the hold duration. The Redis provider rounds key TTL down to whole seconds, so near-deadline reads may miss early. Expiration during connection establishment is treated as a skipped cache write. Read-only lookups reject expired, non-Active, mismatched-ID or malformed payloads as misses without deleting them. Repair/removal happens through the caller after its successful database commit.

Redis errors are logged and treated as cache misses/skipped writes or invalidations. Request cancellation still propagates. Invalidation and database commit are not one distributed transaction: a slow Active reader can refill after invalidation, or removal can fail. Every GET rechecks the database header and will bypass/remove such stale data; command authorization and seat mutations never use cached state. There is no durable invalidation queue or cross-request cache-fill lock.

## Configuration and verification

`Program` calls `AddHoldCaching`. Configure `ConnectionStrings:Redis` through User Secrets or `ConnectionStrings__Redis`. The default is `localhost:6379`. Host settings are independent of Compose `.env`; start the local service using [README instructions](../../../README.md#local-redis). The singleton cache holds no DbContext or caller state. API `/health` stays liveness-only.

`RedisHoldCacheTests` uses deterministic manual time and a recording distributed cache, including one tick before/at/after expiry, unchanged deadlines on refill, terminal-state removal, malformed JSON, Redis failure and cancellation. `AuthEndpointsTests.HoldCaching` exercises database-backed ownership, cache hits/misses, commit order, stale terminal refills and outage fallback. `RedisHoldCacheLiveTests` checks actual Redis payloads, TTL, corruption recovery, eviction and removal; set `TICKETBOOKING_TEST_REDIS_CONNECTION_STRING` to an isolated instance. PostgreSQL HTTP tests separately require `TICKETBOOKING_TEST_CONNECTION_STRING`. CI supplies both services.

Verification: the final targeted run passed all 31 cache/configuration cases with zero skips against disposable PostgreSQL 17 and Redis 8. The broader run passed 806 tests; its sole failure was the initial refill-TTL assertion, corrected to account for provider whole-second rounding and verified by the final targeted run. Build, changed-file formatting, Compose configuration, documentation links and entry-point validation passed. Developer database contents were not changed.

Seat availability uses a separate [revisioned public-map cache](../venue-catalogue-model/seat-availability-caching.md). Hold creation, cancellation, expiry and conversion invalidate availability through committed inventory version changes; no extra Redis deletion is required. Hold-response keys and their original-deadline TTL are unchanged.

## Database commit boundary

All application cache population/removal follows a successful `CommitAsync`: hold creation; hold lookup repair/miss/terminal-history cleanup; cancellation and cancellation replay; conversion; expiry; and public seat-map fills. Hold GET now uses a consistent read transaction, matching the existing seat-map read protocol. Cache lookup methods never mutate entries. A failed commit propagates through the existing HTTP error handling (or per-hold worker failure handling) and leaves application-managed cache data unchanged. Redis TTL expiration remains independent of database transactions.

`AuthEndpointsTests.CacheCommits` injects failures immediately before transaction commit, after saves have succeeded. It verifies no cache writes/removals for creation, hold lookup hits/misses/invalid payloads/terminal history, cancellation/replay, conversion, expiry and seat-map hits/misses; failed aggregate writes roll back as well. Seat-availability invalidation continues to follow committed database revisions, so rollback cannot publish a new key. Cache availability failures after commit remain best-effort and do not undo database work.

Post-commit verification: all 85 targeted commit-failure, hold-cache, ownership/deadline-use, seat-cache and cleanup regression cases passed with zero skips against disposable PostgreSQL 17. Test compilation, changed-file formatting, documentation anchors/links and whitespace checks passed.

## Expiration authority

Redis TTLs only discard cached copies. PostgreSQL deadline/ownership/state checks and the [polling expiration worker](hold-expiry-worker.md#postgresql-authority-and-redis-independence) remain authoritative. Missing or retained keys and Redis outages cannot shorten/extend a hold, authorize conversion, release seats or prevent committed database cleanup. Redis keyspace/expiry notifications are not used or required.

Both caches share the [bounded Redis timeout policy](../local-persistence/redis-timeouts.md): 250 ms per cache await and 500 ms connection/command timeouts by default. A timed-out read reloads PostgreSQL, post-commit updates remain best-effort, and request cancellation propagates. Custom connection strings cannot bypass these validated limits. Late cache operations remain subordinate to database header/revision checks.

Additional stale-data verification covers a live Active cache response with a mismatched stored deadline and altered price snapshots: owner GET rejects it and restores PostgreSQL snapshots without extending expiry. Failed removal after cancellation/conversion/expiry leaves the original warm Active payload present, but database history still wins; successful subsequent reads remove it. See [coverage and commands](../local-persistence/cache-testing.md).
