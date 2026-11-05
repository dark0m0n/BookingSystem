# Integration points

| Boundary | Interface and direction | Authority, authentication, and failures |
| --- | --- | --- |
| Developer → Compose | Local CLI and `.env` interpolation → `postgres` service. | Compose owns container/volume configuration; missing password blocks provisioning. |
| Host API → PostgreSQL | Npgsql DbContext configured with `ConnectionStrings:DefaultConnection`. | PostgreSQL owns stored data; connection-string credentials authenticate database access. No query or application retry policy is implemented. |
| Infrastructure → EF Core | In-process `OnModelCreating` and migration classes. | Code owns declared model; runtime schema is Not verified. No automatic migration execution. |
| API diagnostics → persistence setup | Shared API startup configuration guards. | [API diagnostics](../api-diagnostics/FEATURE.md) requires startup to succeed, but `/health` does not test PostgreSQL. |

Evidence: [Program.cs](../../../src/TicketBooking.Api/Program.cs), [Compose](../../../docker-compose.yml), and [DbContext](../../../src/TicketBooking.Infrastructure/Persistence/TicketBookingDbContext.cs). Compose currently contains only PostgreSQL. A host-run API uses localhost and the published port; a future API container would need service DNS (`postgres`) and container port 5432, per README.

Changing credentials, port mappings, or future schema contracts can break callers. Entity/event payloads, cross-feature data contracts, webhooks, and background jobs: Not applicable because none exist.
