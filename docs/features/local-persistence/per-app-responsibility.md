# Application responsibilities

- **postgres** is an independently running Compose service. It owns database files in `postgres_data`, accepts PostgreSQL connections, and reports readiness through `pg_isready`. Its configuration comes from Compose environment interpolation.
- **TicketBooking.Api** is the host-run deployable app. It owns connection-string lookup and Npgsql DbContext registration, but performs no business database operations in current source.
- **TicketBooking.Infrastructure** is a library owning `TicketBookingDbContext` and migrations. It scans its own assembly for EF entity configurations.
- **TicketBooking.Application**, **TicketBooking.Domain**, and **TicketBooking.Contracts** are libraries with no implemented persistence workflows or entities.

Evidence: [Compose](../../../docker-compose.yml), [Program.cs](../../../src/TicketBooking.Api/Program.cs), [DbContext](../../../src/TicketBooking.Infrastructure/Persistence/TicketBookingDbContext.cs), and project references. The API is not a Compose service. No separate migration worker or human ownership assignment is defined.
