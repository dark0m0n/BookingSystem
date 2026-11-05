# PostgreSQL persistence and constraints

Status: **partial**. Local PostgreSQL provisioning, EF mappings, migration, foreign keys, unique indexes, and database round trips are implemented. Customer/admin database workflows, durable allocation ownership, and transactional booking operations remain absent. [Row-level concurrency](../session-seat-concurrency/FEATURE.md) now fences SessionSeat changes.

Working tree inspected on top of `5ba7f54`, including uncommitted domain/Money/timing work. Developers can use the existing Npgsql DbContext to persist eleven entity types. The database rejects duplicate seat coordinates, duplicate session inventory, orphan references, repeated booking numbers, and duplicate payment identities.

## Acceptance and verification

- Eleven mapped entity types and thirteen required foreign keys use restrictive deletion.
- Unique inventory keys: Seats(AuditoriumId, Row, Number) and SessionSeats(SessionId, SeatId).
- Composite item keys preserve history across holds/bookings; booking numbers and payment keys/references have defined uniqueness scopes.
- Money columns use numeric(18,2) and required currency strings; UTC deadline snapshots persist as timestamp with time zone.
- Metadata tests and real PostgreSQL 17 tests passed, including migration application, aggregate round trips, rejection paths, and allowed key reuse. Only the disposable test database was migrated; developer/deployed database state is Not verified.

Follow [README local startup](../../../README.md#local-startup) to configure secrets, start PostgreSQL, apply migrations, and run the API. The original InitialCreate is empty; [AddDomainForeignKeysAndUniqueConstraints](../../../src/TicketBooking.Infrastructure/Persistence/Migrations/20261105143133_AddDomainForeignKeysAndUniqueConstraints.cs) creates the business schema. The API itself still does not apply migrations at runtime.

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
