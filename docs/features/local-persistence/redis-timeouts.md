# Redis timeouts and database fallback

Status: implemented in the inspected working tree. Both active-hold and public seat-map caches share a bounded Redis I/O policy. PostgreSQL remains authoritative; cache availability cannot authorize allocations, extend deadlines or undo committed work. No database migration or additional package is required.

## Settings and enforcement

| Setting | Default | Accepted bounds |
| --- | --- | --- |
| `RedisCache:OperationTimeout` | `00:00:00.250` | 10 ms through 2 s. |
| `RedisCache:ConnectionTimeout` | `00:00:00.500` | 50 ms through 5 s. |
| `RedisCache:CommandTimeout` | `00:00:00.500` | 50 ms through 5 s. |

Defaults appear in API appsettings.json and RedisCachePolicyOptions. Bind/validate them once during AddHoldCaching; overrides use User Secrets, environment-specific configuration or `RedisCache__OperationTimeout`, `RedisCache__ConnectionTimeout`, `RedisCache__CommandTimeout`. Invalid values fail registration. Restart the API to apply changes.

`ConnectionStrings:Redis` defaults to `localhost:6379`. Host, port, credentials, database and TLS settings still come from this connection string. HoldCacheRegistration parses it and overrides connection/async/sync timeouts with the validated policy, including for custom strings that specify longer timeouts. It enforces one initial connection attempt (`ConnectRetry=1`), `AbortOnConnectFail=false` and `BacklogPolicy.FailFast` to reject disconnected commands instead of buffering them for later replay. The shared multiplexer can reconnect in the background; application cache attempts are not retried.

RedisCacheTimeoutPolicy uses Task.WaitAsync with the injected TimeProvider to bound each awaited cache operation, independently of connection setup, the provider's cancellation support or its timeout-check heartbeat. This is a per-attempt cache budget, not a total HTTP/database deadline. A read miss can spend one budget on lookup and another on its post-commit fill. The default cache overhead for those two waits is approximately 500 ms, in addition to database/serialization/scheduling work.

## Failure and cancellation behavior

- Timed-out/unavailable hold or seat-map reads become cache misses; existing PostgreSQL snapshot/ownership/revision checks supply the response.
- Timed-out/unavailable fills/removals are logged and skipped after the successful database commit. Database work is not rolled back or reported as a cache-related 500.
- Availability failures are typed Redis, timeout, I/O or socket exceptions. A provider cancellation without caller cancellation is normalized to a timeout. Caller/request/worker cancellation propagates; programming errors are not hidden as cache misses.
- Each attempt gets a fresh budget, so a later healthy provider operation can succeed after an earlier timeout. Cache reads remain read-only; application mutations remain post-commit.

A timed-out await does not guarantee the underlying provider operation stopped. The policy observes late faults without waiting for them. A late write still uses the original hold deadline or original seat-map revision; a late removal can only cause a cache miss. PostgreSQL header/revision checks remain necessary. Driver connection/command limits and fail-fast backlog behavior constrain provider work, while the outer operation deadline bounds callers even when driver timeout evaluation is coarser. No durable invalidation queue or global circuit breaker is introduced.

## Ownership and verification

Infrastructure owns RedisCachePolicyOptions, RedisCacheTimeoutPolicy and transport configuration in HoldCacheRegistration. Both RedisHoldCache and RedisSeatAvailabilityCache call the shared policy around all provider reads/writes/removals. The policy holds no DbContext/caller state; API and worker workflows keep their existing authority and [post-commit cache contract](../reservation-booking-payment/hold-caching.md#database-commit-boundary). Redis expiry notifications remain unused.

RedisTimeoutTests uses a timer-controllable TimeProvider and providers that ignore cancellation to cover exact operation-budget cutoffs, read fallback, bounded writes/removals, late faults/recovery, caller cancellation, internal provider cancellation, typed transport errors, invalid bounds and custom connection-string enforcement. AuthEndpointsTests.RedisTimeouts verifies PostgreSQL-backed hold/seat reads plus committed creation, cancellation, conversion and expiration while all cache calls remain pending. Existing commit-order, authority and delayed-fill tests protect database correctness; live Redis tests verify healthy transport behavior. See [hold caching](../reservation-booking-payment/hold-caching.md) and [seat-map caching](../venue-catalogue-model/seat-availability-caching.md).

Verification: the broader targeted regression run passed 138 cases with zero skips. After the final synchronous-cancellation/late-fault handling refinements, all 63 focused timeout, adapter, PostgreSQL fallback and healthy Redis cases passed with zero skips against disposable PostgreSQL 17 and Redis 8. Solution build, final test compilation, changed-file formatting, application JSON, documentation source anchors/links and whitespace checks passed. Developer database contents were not changed.

RedisUnavailableTests additionally uses the real Redis provider against a reserved non-listening loopback endpoint to verify bounded cache misses and skipped mutations without disturbing a running service. Live stale-payload and PostgreSQL fallback cases are listed in the [verification matrix](cache-testing.md).
