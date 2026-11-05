# Repository Guidelines

## Project Structure & Module Organization

`TicketBooking.slnx` groups five projects under `src/`:
- `TicketBooking.Api`: ASP.NET Core entry point, configuration, OpenAPI, and health endpoint.
- `TicketBooking.Application`: application logic; references Domain and Contracts.
- `TicketBooking.Domain`: domain model organized into Identity, Catalogue, Venue, Reservation, Booking, Payment, and Notification folders. Keep these logical boundaries in the existing assembly and independent of infrastructure; follow `src/TicketBooking.Domain/README.md` for ownership and namespace conventions.
- `TicketBooking.Contracts`: shared request and response contracts.
- `TicketBooking.Infrastructure`: EF Core persistence and PostgreSQL migrations under `Persistence/Migrations/`.

`tests/` contains Domain, Application, Integration, and Architecture test projects. `docs/` is available for supporting documentation. Preserve the existing project dependency direction when adding features.

## Build, Test, and Development Commands

Run commands from the repository root using the .NET 10 SDK selected by `global.json`:

- `dotnet restore TicketBooking.slnx`: restore dependencies.
- `dotnet build TicketBooking.slnx`: build all projects; warnings are errors.
- `dotnet test TicketBooking.slnx`: run all test projects.
- `dotnet format TicketBooking.slnx --verify-no-changes`: check formatting without editing files.
- `docker compose up -d --wait postgres`: start local PostgreSQL after configuring `.env` as described in `README.md`.
- `dotnet run --project src/TicketBooking.Api --launch-profile https`: start the development API after configuring secrets.

For the complete local startup command, including database readiness and migration application, follow `README.md` → Local startup. Running the API alone does not apply migrations.

## Coding Style & Naming Conventions

Follow `.editorconfig`: UTF-8, LF endings, final newlines, four-space indentation for C#, and two spaces for JSON, YAML, and project files. Put C# opening braces on separate lines and sort `System` imports first. Match existing file-scoped namespaces. Use PascalCase for types and methods, camelCase for parameters and locals, and filenames matching their primary types. Nullable reference types and implicit usings are enabled globally.

## Time Conventions

Use UTC `DateTimeOffset` instants, normalized with `ToUniversalTime()`. Inject `TimeProvider` for current time and pass it to expiration/deadline rules; never read static wall clocks in business logic. The API registers `TimeProvider.System`; tests use a controllable clock without sleeps. See `docs/time-conventions.md`.

## Testing Guidelines

Tests use xUnit and `coverlet.collector`. Domain tests cover entity construction, price snapshots, ownership/deadline checks, and hold/booking/payment transitions; the other test projects remain empty scaffolds. No coverage threshold is configured. Add meaningful behavior tests to the appropriate project, using descriptive names such as `BookTicket_WhenSoldOut_ReturnsFailure`. Use `dotnet test TicketBooking.slnx --collect:"XPlat Code Coverage"` to collect coverage. Add required project references when implementing the scaffolded test projects.

## Feature Documentation

When adding a new feature, use the `document-feature` skill to create or update its documentation under `docs/features/<feature>/`. Follow the skill's ten-file contract, verify documentation against the implementation, and clearly mark incomplete or unverified behavior.

## Commit & Pull Request Guidelines

History currently contains only `Initial commit`, so no established message convention exists. Use short, imperative summaries and keep commits focused. PRs should describe the behavior change, related issues, validation performed, and any migration or configuration steps.

## Security & Configuration

Keep `.env` and credentials out of Git. Store `ConnectionStrings:DefaultConnection` and `Jwt:SigningKey` in API User Secrets. Both are required at startup. See `README.md` for database setup; deleting the Compose volume destroys local data.
