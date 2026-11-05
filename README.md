# Ticket Booking

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

Start the API using the local Development profile after configuring its database connection string and `Jwt:SigningKey` in User Secrets:

```bash
dotnet run --project src/TicketBooking.Api --launch-profile https
```

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
