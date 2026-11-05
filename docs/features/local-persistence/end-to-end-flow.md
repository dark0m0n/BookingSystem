# End-to-end flow

Trigger: a developer provisions a local database and starts the API.

1. Create local `.env` from the example and set the password (BR-01). Compose configuration is the `postgres-service` entry point.
2. Run `docker compose up -d --wait postgres`. Compose configures PostgreSQL 17, maps the selected host port to container port 5432, and mounts `postgres_data` (BR-02/BR-03).
3. The configured `pg_isready` probe reports whether PostgreSQL accepts connections; it does not validate application queries or schema. README documents `--wait` behavior.
4. Configure `ConnectionStrings:DefaultConnection` in API User Secrets separately. Compose `.env` is not automatically the host API's connection configuration.
5. Run the [README startup command](../../../README.md#start-locally), which restores the EF tool and waits for PostgreSQL before invoking `dotnet ef database update`. The target project is Infrastructure, and the startup project is API with `--environment Development` to load development configuration and User Secrets. API startup registers `TicketBookingDbContext` using `UseNpgsql` (`persistence-registration`, BR-04); `OnModelCreating` scans its assembly (`db-context`).
6. EF applies pending migrations and records them in `__EFMigrationsHistory`. `initial-migration` has empty Up/Down methods and creates no business tables. This documented CLI step is separate from API runtime startup; no application `Migrate` call exists.
7. Only after successful migration application does the command run the API with the HTTPS Development profile. No booking query, write, or transaction exists in current source. A failed tool restore, Compose startup, or migration step prevents the subsequent steps from running.

Entry IDs resolve in [entry-points.json](entry-points.json). Missing password prevents Compose interpolation; missing API configuration prevents startup. Port conflict and credential mismatch are operational failure risks. Compose uses `restart: unless-stopped`; no application retry policy is configured.

Ordinary `docker compose down` preserves the named volume. Explicit volume deletion destroys local data; do not use it as a routine restart. No runtime provisioning, state transitions, or schema application were verified during documentation.
