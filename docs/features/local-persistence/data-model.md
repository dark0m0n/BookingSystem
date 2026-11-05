# Data model

The EF persistence model is empty: no mapped entities, fields, relations, constraints, or business tables. The separate [domain model](../venue-catalogue-model/data-model.md) now declares Venue/Catalogue entities and statuses, but no EF mappings are implemented.

Evidence:

- [TicketBookingDbContext](../../../src/TicketBooking.Infrastructure/Persistence/TicketBookingDbContext.cs): assembly scanning, no DbSet properties.
- [InitialCreate](../../../src/TicketBooking.Infrastructure/Persistence/Migrations/20261026113308_InitialCreate.cs): empty `Up` and `Down` methods.
- [Model snapshot](../../../src/TicketBooking.Infrastructure/Persistence/Migrations/TicketBookingDbContextModelSnapshot.cs): provider/model annotations, no entity definitions.

PostgreSQL owns persistent files in Compose volume `postgres_data`. `POSTGRES_DB` and `POSTGRES_USER` default to `ticketbooking`; `POSTGRES_PORT` defaults to host port 5432. The password is required and is not documented here. These are provisioning settings, not business model fields.

Actual database contents, EF migration-history state, and applied schema are **Not verified**. An empty migration file does not prove a live database is initialized. No customer data storage, retention policy, or PII lifecycle is implemented in the inspected model.
