# Local persistence foundation

Status: **partial**. PostgreSQL provisioning and DbContext wiring exist; business persistence does not.

Inspected revision: `ea94ab900a3bc4b7b772e0f0cbcb20bb5d52532d`; working tree inspected. Implementation evidence is static source inspection; runtime behavior is **Not verified**.

Developers can provision a local PostgreSQL 17 service and configure API access. [Compose](../../../docker-compose.yml) defines the service and named volume; [TicketBookingDbContext](../../../src/TicketBooking.Infrastructure/Persistence/TicketBookingDbContext.cs) loads entity configurations from its assembly. No mapped entities or CRUD workflows exist; the initial domain types are documented in the [model bundle](../venue-catalogue-model/FEATURE.md).

Scope: local database lifecycle, configuration boundary, EF Core registration, and empty initial migration. Booking storage, deployed schema state, production provisioning, and authentication are outside implemented scope.

## Acceptance and verification

- Local-only port mapping, required password, named volume, and readiness probe: implemented in Compose; runtime Not verified.
- API registers Npgsql-backed DbContext: implemented; connection and queries Not verified.
- Mapped booking entities and database operations: absent; no supplied business specification to document as planned.

Follow [README: Local startup](../../../README.md#local-startup) for one-time configuration and the single startup command. It restores the EF tool, waits for PostgreSQL, applies pending migrations using Infrastructure as the target and API as the Development startup project, then runs the API. The command stops on failure. Use `docker compose ps` for database status. The API itself does not apply migrations. These commands were not run during documentation; no migrations were applied.

## Navigation

- [Business context and rules](business-context.md)
- [End-to-end flow](end-to-end-flow.md)
- [Verified entry points](entry-points.json)
- [Application responsibilities](per-app-responsibility.md)
- [Frontend structure](frontend-structure.md)
- [Backend structure](backend-structure.md)
- [Data model](data-model.md)
- [Integration points](integration-points.md)
- [Known gotchas](known-gotchas.md)
