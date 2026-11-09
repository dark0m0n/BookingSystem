# Seat-availability caching and invalidation

Status: implemented in the inspected working tree. The public `GET /api/sessions/{id}/seats` can reuse the expensive inventory/layout projection for larger, repeatedly read maps. PostgreSQL remains authoritative; no availability check, reservation command, ownership check, event listing or session metadata is cached by this feature. No migration or new provider is required.

## Scope and usefulness

`SeatAvailabilityCache` defaults to `Enabled: true`, `MinimumSeatCount: 64`, `Lifetime: 00:00:05`. The lifetime must be one to thirty seconds and the minimum count positive. These are conservative workload settings, not a measured break-even point. Set `SeatAvailabilityCache__Enabled=false` when repeat reads are rare or Redis/hash overhead outweighs the projection savings; tune the threshold using actual workload measurements. No production load benchmark has been performed.

Small/empty maps use the existing SQL projection directly without Redis calls or the additional inventory-revision query. Missing/hidden sessions and inconsistent maps are not cached. Cached public responses retain `Cache-Control: no-store`, existing ordering, exact snapshot prices/status/version and the same DTO shape. Redis copies contain public seat data only.

## Read sequence and key

1. Begin the existing PostgreSQL RepeatableRead transaction; check Scheduled session and Published event visibility. Read the current auditorium's physical-seat count.
2. Only for an eligible map, read `(SessionSeat.Id, Version)` for that session. If its count matches the physical layout, construct a SHA-256 revision from the sorted inventory IDs and 64-bit version tokens. IDs detect inventory replacement even when versions restart at zero; per-item hashing avoids summed-version overflow and detects changes to individual inventory rows.
3. Lookup `seat-availability:v1:{sessionId:D}:{auditoriumId:D}:{revision}`. Redis payloads include an absolute expiry and the SessionSeatMapResponse. The adapter verifies session/auditorium IDs, inventory revision and injected-clock expiry before returning a hit. A hit skips the inventory-to-layout join, sorting and projection, then commits the read transaction.
4. On a miss, query the complete priced map in the same database snapshot, validate physical count/auditorium consistency, commit and fill Redis with a five-second absolute expiry by default. Corrupt or unavailable Redis falls back to this query. Request cancellation propagates; expiration during connection establishment skips the write.

Every cached read still needs PostgreSQL visibility/layout-count and compact inventory-version reads. This is a projection cache, not a database-free source of availability. A read concurrent with a commit can return its consistent earlier snapshot, as before; subsequent snapshots select the new revision. Cache hits never refresh the TTL. The .NET Redis provider rounds TTL down to whole seconds. Expired generations are reclaimed by Redis; no scans or key enumeration are used.

## Invalidation contract

Invalidation is logical and transactional: the first database snapshot after a successful inventory commit selects a different key. Old keys may remain until their short TTL, but are not used by a reader of the new revision. There is no Redis deletion in business transactions and no commit/invalidation network gap. A delayed reader can fill only its original revision, so it cannot repopulate the key read after a newer commit.

| Event | Authoritative inventory write | Invalidation boundary |
| --- | --- | --- |
| Hold creation | `SessionSeatAllocationWriter.HoldAsync`, Available → Held, version +1 for each selected seat. | Commit of hold/items and all claims in `HoldEndpoints.CreateAsync`. Failed/rolled-back claims retain the old key. |
| Owner hold cancellation | Retained-version `ReleaseAsync`, Held → Available or Unavailable. | Commit of Cancelled state and all releases. Cancellation replay changes no inventory and preserves the current key. |
| Hold expiration | `HoldExpiryReleaser` calls retained-version `ReleaseAsync` with Expired state. | Release transaction commit, not the clock deadline or Redis key expiration. A due hold remains allocated while worker cleanup lags. |
| Booking confirmation | `ConfirmAsync`, Held → Booked, version +1. | Commit of confirmation and all retained-version inventory writes. The fenced primitive exists and is covered; the full production booking/payment-confirmation workflow is not implemented. Future workflows must coordinate booking state with these writes in one transaction. |
| Seat release, including booked seats | `ReleaseAsync`, Held/Booked → Available/Unavailable, version +1. | The caller's aggregate/release transaction commit; direct autocommit writes become visible when their SQL statement commits. Future booking expiry/cancellation release workflows must reuse this primitive. |
| Hold-to-booking transfer | `TransferToBookingAsync`, Held → Held, version +1. | Conversion commit, even though visible status remains Held: the returned fencing token must change. |
| Tracked repricing/status edits | `TicketBookingDbContext.AdvanceSessionSeatVersions`. | Save/transaction commit; price/status/version projections use the new revision. |
| Admin reschedule or auditorium move | Reschedules advance retained inventory versions; moves rebuild inventory with new IDs. | Admin session/inventory transaction commit; auditorium ID is also part of the key. |

Never add raw/bulk status, price or ownership changes without advancing the expected version as required by the [concurrency contract](../session-seat-concurrency/FEATURE.md). Layout rows are immutable while referenced by a session; supported layout changes cannot silently invalidate a cached map. Out-of-band SQL that bypasses versions/layout rules is outside this cache's consistency contract.

## Ownership and evidence

Application owns `ISeatAvailabilityCache` and its key record. Infrastructure owns `RedisSeatAvailabilityCache`, `SeatAvailabilityRevision` and bounded options/registration. Program registers them using the existing `IDistributedCache` Redis connection (`ConnectionStrings:Redis`) from hold caching. `CatalogueEndpoints.GetSessionSeatsAsync` owns the consistent snapshot and decides whether a map is worth caching. Allocation writers have no cache dependency and never trust cached tokens.

`SeatAvailabilityCacheTests` covers revision changes/reordering/replacement, maximum version values, payload validation, exact expiry boundaries, bounds, bypass and failures. `AuthEndpointsTests.SeatAvailabilityCaching` covers actual PostgreSQL lifecycle invalidation, rollback/uncommitted writes, delayed old fills, Redis failure, privacy/visibility and inconsistent layouts. Its SQL interceptor verifies that repeated eligible map reads skip the inventory/layout join. `SeatAvailabilityCacheLiveTests` checks real Redis TTL and revision isolation. Use `TICKETBOOKING_TEST_CONNECTION_STRING` and `TICKETBOOKING_TEST_REDIS_CONNECTION_STRING` against isolated instances; CI supplies both. These checks establish behavior and the skipped query, not a measured latency improvement.

Verification: 86 targeted cache, public-catalogue, response-contract, hold-cleanup and allocation tests passed with zero skips against disposable PostgreSQL 17 and Redis 8. Solution build passed with zero warnings/errors; changed-file formatting, documentation entry-point schemas/source anchors/local links and whitespace checks passed. Developer database contents were not changed.

Commit-order regression coverage: `AuthEndpointsTests.CacheCommits` injects seat-map read commit failures on hits and misses and asserts no population/removal. Cached lookups are read-only; fills follow the existing read-snapshot commit. The shared hold cache follows the same [post-commit mutation contract](../reservation-booking-payment/hold-caching.md#database-commit-boundary).

Expiration remains PostgreSQL-driven: key expiry never implies seat release. The [worker](../reservation-booking-payment/hold-expiry-worker.md#postgresql-authority-and-redis-independence) polls persisted deadlines and commits versioned releases without Redis notifications; cached Held maps cannot override the new database revision after commit.

Both caches share the [bounded Redis timeout policy](../local-persistence/redis-timeouts.md): 250 ms per cache await and 500 ms connection/command timeouts by default. A timed-out read reloads PostgreSQL, post-commit updates remain best-effort, and request cancellation propagates. Custom connection strings cannot bypass these validated limits. Late cache operations remain subordinate to database header/revision checks.

Live stale-payload verification also injects expired envelopes and a different inventory revision under an otherwise current live Redis key. Both are misses, and a valid fill repairs the cached map. See [cache test coverage](../local-persistence/cache-testing.md).
