# Local infrastructure, PostgreSQL persistence and constraints

Local Redis provisioning is implemented in the working tree: [Compose](../../../docker-compose.yml) defines Redis 8, a loopback-only configurable port, a named snapshot volume and a `redis-cli ping` health check. See [README Redis startup](../../../README.md#local-redis). Active hold response caching is implemented; see [cache behavior](../reservation-booking-payment/hold-caching.md); API health remains liveness-only. Compose configuration validation passed; container startup has not been verified.

Status: **partial**. Local PostgreSQL provisioning, EF mappings, migration, foreign keys, unique indexes, and database round trips are implemented. Admin setup writes are implemented; customer booking workflows, durable allocation ownership and transactional booking operations remain absent. [Row-level concurrency](../session-seat-concurrency/FEATURE.md) now fences SessionSeat changes.

Working tree inspected on top of `93f4f16`. Developers can use the existing Npgsql DbContext to persist eleven business entity types plus seven framework Identity types and two [refresh entities](../identity-persistence/refresh-token-rotation.md). The database rejects duplicate seat coordinates, duplicate session inventory, orphan references, repeated booking numbers, and duplicate payment identities.

## Acceptance and verification

- Eleven business entity types and thirteen required foreign keys use restrictive deletion; seven Identity entities add six cascading dependent FKs, and two refresh entities add two more cascading FKs.
- Unique inventory keys: Seats(AuditoriumId, Row, Number) and SessionSeats(SessionId, SeatId).
- Composite item keys preserve history across holds/bookings; booking numbers and payment keys/references have defined uniqueness scopes.
- Money columns use numeric(18,2) and required currency strings; UTC deadline snapshots persist as timestamp with time zone.
- Metadata tests and real PostgreSQL 17 tests passed, including migration application, aggregate round trips, rejection paths, and allowed key reuse. Only the disposable test database was migrated; developer/deployed database state is Not verified.

Follow [README local startup](../../../README.md#local-startup) to configure secrets, start PostgreSQL, apply migrations, and run the API. The original InitialCreate is empty; [AddDomainForeignKeysAndUniqueConstraints](../../../src/TicketBooking.Infrastructure/Persistence/Migrations/20261105143133_AddDomainForeignKeysAndUniqueConstraints.cs) creates the business schema. [AddAspNetCoreIdentity](../../../src/TicketBooking.Infrastructure/Persistence/Migrations/20261105162749_AddAspNetCoreIdentity.cs) adds seven account/role tables without changing those business tables. The API itself still does not apply migrations at runtime.

Run `dotnet test TicketBooking.slnx --configuration Release`. Database tests require `TICKETBOOKING_TEST_CONNECTION_STRING` targeting an isolated PostgreSQL database, and otherwise skip; metadata/configuration tests still run. CI provisions a dedicated PostgreSQL service and sets the variable. Database tests apply migrations before each test, then roll back test rows.

- [Business context and rules](business-context.md)
- [End-to-end flow](end-to-end-flow.md)
- [Verified entry points](entry-points.json)
- [Application responsibilities](per-app-responsibility.md)
- [Frontend structure](frontend-structure.md)
- [Backend structure](backend-structure.md)
- [Data model and constraints](data-model.md)
- [Integration points](integration-points.md)
- [Known gotchas](known-gotchas.md)

## Related decision

See the [seat ownership, price history and transition decision record](../../decisions/0001-seat-ownership-price-history-and-transitions.md) for rationale, enforcement boundaries and remaining transaction work.

The [registration/profile migration](../../../src/TicketBooking.Infrastructure/Persistence/Migrations/20261105163800_AddRegistrationProfileAndUniqueEmail.cs) now adds DisplayName and a unique normalized-email index. See [account HTTP contracts](../identity-persistence/http-contract.md); public registration handles concurrent account conflicts without exposing database details.

[AddRefreshTokenRotation](../../../src/TicketBooking.Infrastructure/Persistence/Migrations/20261105165909_AddRefreshTokenRotation.cs) creates refresh families and hash-only generation history. Rotation/replay revocation use an explicit PostgreSQL family-row lock and transaction; see the [rotation contract](../identity-persistence/refresh-token-rotation.md). The full 325-test suite passed against disposable PostgreSQL, including rollback and concurrency cases.

The data-only [SeedCustomerAndAdminRoles](../../../src/TicketBooking.Infrastructure/Persistence/Migrations/20261105211207_SeedCustomerAndAdminRoles.cs) migration ensures canonical role definitions and retains existing IDs/memberships. It creates no admin account. Registration now wraps account creation and Customer membership in one transaction; see [roles and policies](../identity-persistence/roles-and-policies.md).

## Venue and catalogue mapping verification

All six requested entities already have registered EF configurations: Venue, Auditorium, Seat, Event, Session and SessionSeat. The existing AddDomainForeignKeysAndUniqueConstraints migration creates these tables; AddSessionSeatOptimisticConcurrency adds the inventory version check constraint. No additional schema migration is needed when the current model matches its snapshot. See [the mapping locations](backend-structure.md) and [stored fields](data-model.md).

PersistenceModelTests.Migrations_ModelSnapshotMatchesCurrentModel detects model/snapshot drift. PostgresPersistenceTests.VenueAndCatalogueRoundTrip_PreservesEveryFieldAndInventoryStatus reloads all six entity types from PostgreSQL, checking every field, parent link, UTC timestamps, EUR price precision, Unicode text and all four inventory states. The test fixture applies the migration chain to the isolated database first; it does not apply migrations to the development database.

Verification result: **429 tests passed, zero failed/skipped**, using fresh disposable PostgreSQL 17. The model-snapshot check and all four catalogue/inventory round trips passed. Formatting passed for the changed tests; documentation JSON, code anchors and local links validated.

Admin venue/layout/event/session setup now uses this schema through [Admin-only HTTP operations](../venue-catalogue-model/admin-http-contract.md). Auditorium row locks serialize layout changes and atomic session/inventory creation. No new migration is required; customer booking/allocation application transactions remain separate work.

## Hold mapping verification

SeatHold and SeatHoldItem mappings and table creation already exist in the checked-in migration chain. The working-tree verification adds a focused four-state, two-item PostgreSQL round trip and indexes the configurations in [entry-points.json](entry-points.json). See [backend structure](backend-structure.md#hold-configurations) and [data model](data-model.md#hold-mapping-verification-scope). This scope does not add customer hold endpoints or change allocation workflows.

Verification: all 44 targeted PersistenceModelTests and PostgresPersistenceTests cases passed with zero skips against fresh disposable PostgreSQL 17. This includes migration application, snapshot consistency, the four hold-state round trips and existing constraint/snapshot tests. Documentation entries, code anchors and local links validated.

## Bounded Redis access

Both caches now share validated per-operation, connection and command timeouts with typed database fallback. Defaults are 250 ms per cache await and 500 ms for transport timeouts. See [configuration, cancellation and late-operation behavior](redis-timeouts.md).

The [cache verification matrix](cache-testing.md) records TTL, stale-data, invalidation, commit-order and outage coverage, including live Redis stale-payload checks and a real disconnected Redis client.
