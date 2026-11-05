# Ticket Booking

## Domain organization

The existing Domain project contains seven logical boundaries: **Identity**, **Catalogue**, **Venue**, **Reservation**, **Booking**, **Payment**, and **Notification**. They remain in one assembly within the existing application. See the [domain boundary map](src/TicketBooking.Domain/README.md) for ownership and namespace conventions, and the [feature documentation](docs/features/domain-boundaries/FEATURE.md) for implementation status. Venue and Catalogue contain the initial [domain entities](docs/features/venue-catalogue-model/FEATURE.md). Reservation, Booking, and Payment provide [hold, booking, and payment entities with guarded transitions](docs/features/reservation-booking-payment/FEATURE.md). Application workflows and persistence mappings are not implemented yet; refund and messaging entities are deferred to their planned phases.

Time-sensitive rules use an injectable `TimeProvider` and UTC-normalized `DateTimeOffset` instants. See [time conventions](docs/time-conventions.md) for storage, deadline boundaries, and testing.

## Local startup

### One-time setup

Install the .NET 10 SDK selected by `global.json` and Docker with the Compose plugin. From the repository root, copy `.env.example` to `.env` and set `POSTGRES_PASSWORD` (see [Local PostgreSQL](#local-postgresql)). Configure `ConnectionStrings:DefaultConnection` using the [API connection settings](#api-connection-settings), and set a local signing key:

```bash
dotnet user-secrets set 'Jwt:SigningKey' 'YOUR_LOCAL_SIGNING_KEY' --project src/TicketBooking.Api
dotnet dev-certs https --trust
```

Replace the signing-key placeholder with your own local secret. The HTTPS command trusts the development certificate on supported platforms; Linux may require additional trust configuration for your browser or HTTP client.

### Start locally

After setup, run this single Bash command from the repository root:

```bash
dotnet tool restore --tool-manifest dotnet-tools.json && \
  docker compose up -d --wait postgres && \
  dotnet ef database update \
    --project src/TicketBooking.Infrastructure \
    --startup-project src/TicketBooking.Api \
    --context TicketBookingDbContext \
    -- --environment Development && \
  dotnet run --project src/TicketBooking.Api --launch-profile https
```

This restores the pinned EF Core tool, waits for PostgreSQL readiness, applies migrations, and starts the API at `https://localhost:7281`. Each step runs only if the previous step succeeds. Package restore/build happens through the EF and API commands. Stop the API with Ctrl+C; PostgreSQL stays running until `docker compose down`.

### How migrations are applied

`dotnet ef database update` applies pending migrations from `src/TicketBooking.Infrastructure/Persistence/Migrations/` to the database configured by the API's `ConnectionStrings:DefaultConnection`. The API startup project supplies configuration and the DbContext registration; forwarding `--environment Development` enables development configuration and User Secrets. See the [EF Core CLI reference](https://learn.microsoft.com/en-us/ef/core/cli/dotnet) for these options.

EF records applied migrations in `__EFMigrationsHistory`; rerunning the startup command leaves already-applied migrations in place. The current `InitialCreate` migration has empty `Up`/`Down` methods and creates no business tables. The command applies existing migrations; it does not generate new ones. Running `dotnet run` alone does **not** apply migrations.

Compose reads `.env`, but the host API does not automatically read it. Ensure the API connection string matches the local database settings, including any changed port. Both the EF command and API startup require the connection string and `Jwt:SigningKey`. If migration application fails, resolve the reported connection, credential, or migration error before retrying; the API is not started by the chain on failure.

## Local PostgreSQL

Install Docker with the Docker Compose plugin, then run from the repository root:

```bash
cp .env.example .env
# Set POSTGRES_PASSWORD in .env to your local development password.
docker compose up -d --wait postgres
```

Compose reads `.env` automatically. This file is ignored by Git; `.env.example` documents the required settings.

The database uses PostgreSQL 17 and is exposed only on localhost. By default, its database and username are `ticketbooking`, and its port is `5432`. Change `POSTGRES_PORT` in `.env` if that port is already in use.

Check status, read logs, or open a database shell:

```bash
docker compose ps
docker compose logs postgres
docker compose exec postgres sh -c 'psql -U "$POSTGRES_USER" -d "$POSTGRES_DB"'
```

The health check uses `pg_isready` to check whether PostgreSQL accepts connections. `--wait` waits for the service to become healthy.

### API connection settings

For an API running on the host, use this connection string, substituting the values from `.env`:

```text
Host=localhost;Port=5432;Database=ticketbooking;Username=ticketbooking;Password=YOUR_LOCAL_PASSWORD
```

Store it with .NET User Secrets rather than committing it to `appsettings.json`:

```bash
dotnet user-secrets init --project src/TicketBooking.Api
dotnet user-secrets set 'ConnectionStrings:DefaultConnection' 'Host=localhost;Port=5432;Database=ticketbooking;Username=ticketbooking;Password=YOUR_LOCAL_PASSWORD' --project src/TicketBooking.Api
```

The API reads this connection string when registering its EF Core context. If the API is later added to Compose, its database host will be `postgres` and its database port will be `5432`.

## API documentation, health, and errors

Use the [local startup command](#start-locally) to start PostgreSQL, apply migrations, and run the API in Development.

- `GET https://localhost:7281/openapi/v1.json` returns the OpenAPI document in Development. An interactive Swagger UI is not installed.
- `GET https://localhost:7281/health` returns `200 OK` with `Healthy` when the API is running. This is a basic liveness check; it does not check PostgreSQL connectivity.
- Errors without an existing response body, such as unknown routes, return ProblemDetails JSON when the client accepts JSON. Unhandled request exceptions use a generic `500` response without exposing exception details, including in Development.

For example, request an unknown route with `Accept: application/problem+json`. Its response includes `type`, `title`, `status`, `instance`, and a `traceId` for correlating errors with server diagnostics. Future endpoint handlers should use `Results.Problem` or `Results.ValidationProblem` for explicit errors to follow the same format.

### Stop and restart

```bash
docker compose down
docker compose up -d --wait postgres
```

The named `postgres_data` volume keeps database data across container removal and recreation. Database initialization settings apply only when the volume is empty; editing `.env` does not change credentials in an existing database.

To intentionally delete all local database data and initialize again:

```bash
docker compose down --volumes
docker compose up -d --wait postgres
```
