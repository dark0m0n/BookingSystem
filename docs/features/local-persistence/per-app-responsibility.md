# Application responsibilities

- **postgres** owns schema enforcement and stored rows. Development Compose retains its named volume; constraint tests and CI use a separate database.
- **TicketBooking.Infrastructure** owns the shared IdentityDbContext, Identity user/role stores, eleven business entity configurations, the shared complex Money mapping, and migrations. It defines relational identity/reference rules without moving domain logic into EF.
- **TicketBooking.Domain** owns validated creation/lifecycle methods. Private parameterless constructors/setters support persistence; application-facing properties remain read-only. Private List backing fields support EF item navigation fixup without exposing mutable lists.
- **TicketBooking.Api** owns Npgsql registration and local startup configuration; it has no business persistence endpoints yet.
- **Application/Contracts** have no new database workflow/DTO in this change. Future Application must coordinate allocation ownership and transactions.

Identity account storage now lives in Infrastructure without a service extraction. No refund/messaging entity, migration worker or external provider adapter is introduced. Tests apply the migration only to an isolated PostgreSQL test database. Human ownership assignments are not specified.
