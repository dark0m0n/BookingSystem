# Ticket Booking

## Domain organization

The [domain and schema decision record](docs/decisions/0001-seat-ownership-price-history-and-transitions.md) explains seat ownership, price history, legal transitions, deadline policy and remaining enforcement gaps.

The existing Domain project contains seven logical boundaries: **Identity**, **Catalogue**, **Venue**, **Reservation**, **Booking**, **Payment**, and **Notification**. They remain in one assembly within the existing application. See the [domain boundary map](src/TicketBooking.Domain/README.md) for ownership and namespace conventions, and the [feature documentation](docs/features/domain-boundaries/FEATURE.md) for implementation status. Venue and Catalogue contain the initial [domain entities](docs/features/venue-catalogue-model/FEATURE.md). Reservation, Booking, and Payment provide [hold, booking, and payment entities with guarded transitions](docs/features/reservation-booking-payment/FEATURE.md). Application workflows are not implemented yet; mapped storage and constraints are described in [persistence](docs/features/local-persistence/FEATURE.md); refund and messaging entities are deferred to their planned phases.

Time-sensitive rules use an injectable `TimeProvider` and UTC-normalized `DateTimeOffset` instants. See [time conventions](docs/time-conventions.md) for storage, deadline boundaries, and testing.

Prices and totals use immutable `Money` values with explicit currency and decimal(18,2) limits. Hold/booking item snapshots capture independent amount/currency copies, so catalogue repricing cannot alter existing totals. Hold/booking totals and payment amounts are derived in domain code; see [money and pricing](docs/features/money-and-pricing/FEATURE.md) for the flow and remaining server integration work.

## Booking timing

The API binds `BookingTiming` from configuration once at startup and registers an immutable policy. Defaults in `src/TicketBooking.Api/appsettings.json` are:

| Setting | Default | Deadline calculation |
| --- | --- | --- |
| `BookingTiming:HoldDuration` | `00:10:00` | Hold creation time + duration, capped at session start. |
| `BookingTiming:PaymentCompletionWindow` | `00:15:00` | Booking creation time + window, capped at session start. |
| `BookingTiming:CancellationLeadTime` | `01:00:00` | Session start minus lead time. |

Values use `TimeSpan` syntax (`hh:mm:ss`). Hold/payment windows must be positive; cancellation lead time must be nonnegative. A zero lead time permits cancellation until session start. Invalid text or signs prevent API startup, including EF design-time startup.

Override settings through environment-specific configuration, User Secrets, or environment variables, for example before running the local startup command:

```bash
export BookingTiming__HoldDuration='00:05:00'
export BookingTiming__PaymentCompletionWindow='00:10:00'
export BookingTiming__CancellationLeadTime='00:30:00'
```

The host API does not automatically load Compose `.env`. Restart the API after changing timing settings. Each entity captures its absolute UTC deadlines at creation; later policy changes do not move existing deadlines. Booking payment initiation/confirmation and cancellation require current time strictly before their cutoffs; expiry is allowed at the cutoff. The payment window starts when the booking is created, not when payment is attempted.

Domain checks are implemented, but no booking endpoints or expiry worker exists yet. Deadline snapshots are mapped by the persistence migration. See [booking timing](docs/features/booking-timing/FEATURE.md) for policy details, tests, and late-payment limitations.

After hold conversion, a pending booking keeps its seats until its captured payment deadline (15 minutes after booking creation by default, capped at session start). The original hold expiry no longer releases them. At the cutoff, the planned expiry transaction marks the booking Expired and releases only its own allocations. Late successful payments never restore the booking or reclaim seats; they require durable reconciliation and an idempotent refund when refund processing is implemented. See the [seat-retention and late-payment policy](docs/features/booking-timing/seat-retention-and-late-payments.md); allocation transfer/release and compensation are not implemented yet.

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

EF records applied migrations in `__EFMigrationsHistory`; rerunning the startup command leaves already-applied migrations in place. The original `InitialCreate` is empty; `20261105143133_AddDomainForeignKeysAndUniqueConstraints` creates the eleven business tables, required foreign keys, and unique indexes (including auditorium seat coordinates and session/seat inventory pairs). The command applies existing migrations; it does not generate new ones. Running `dotnet run` alone does **not** apply migrations.

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
- Errors without an existing response body, such as unknown routes, and handled request exceptions return ProblemDetails JSON, including for unsupported `Accept` headers. Unexpected exceptions use a generic `500` response without exposing exception details, including in Development.

For example, request an unknown route with `Accept: application/problem+json`. Its response includes `type`, `title`, `status`, `detail`, `instance`, `traceId`, and a stable `code` (`not_found`). Clients should branch on `code`, never on message text. Domain conflicts include `seat_not_available`, `hold_expired`, and `payment_already_processed` (HTTP 409); ownership failures use `resource_not_owned` (403). See the [error code contract](docs/features/api-diagnostics/error-codes.md). Future endpoint handlers should use `Results.Problem` or `Results.ValidationProblem`, with constants from `TicketBooking.Contracts.Errors.ErrorCodes` for explicit business codes; status-based codes are added when omitted.

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

## Persistence tests

Metadata/configuration tests run with `dotnet test TicketBooking.slnx`. Actual PostgreSQL migration/constraint tests also run when `TICKETBOOKING_TEST_CONNECTION_STRING` points to a dedicated test database; otherwise they are skipped. These tests apply migrations, then roll back test rows, so never target a development or production database containing real data. CI supplies a disposable PostgreSQL 17 service and this variable.

The [persistence guide](docs/features/local-persistence/data-model.md) lists foreign keys and uniqueness scopes. Constraints protect relational references and inventory identity. [Session-seat concurrency](docs/features/session-seat-concurrency/FEATURE.md) now fences tracked writes/deletes and explicit allocation writes with Version checks, including Held→Held transfer. Full owner-linked hold/booking transactions and authenticated booking endpoints remain future work.
