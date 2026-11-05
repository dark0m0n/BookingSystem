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

## Identity Storage

Keep ASP.NET Core Identity types and EF stores in Infrastructure. Use `UserManager<ApplicationUser>` and `RoleManager<IdentityRole<Guid>>` for account operations; never persist a plaintext password or bypass manager validation/normalization. Identity uses the shared DbContext with GUID keys. The API adds SignInManager, registration/login routes and JWT bearer authentication. Keep JWT expiry on injected TimeProvider, roles server-controlled and public errors stable; never accept a request role or return Identity entities. See `docs/features/identity-persistence/FEATURE.md`.

Refresh credentials must use `RefreshTokenService`: persist only hashes, rotate/consume within its family-lock transaction, and retain consumed hashes for replay detection. Use RevokeAsync for logout/revocation, including consumed ancestors; preserve the first RevokedAt. Do not bypass that lock for revocation or retry a consumed token. Family expiry is fixed at login; clients serialize refreshes. See `docs/features/identity-persistence/refresh-token-rotation.md`.

Use `RoleNames` and named `AuthorizationPolicies.Customer`/`Admin` for role access. Public registration must grant only Customer and atomically commit membership with user creation; never bind request roles or automatically seed admin accounts. Explicit development provisioning must use the Development-only `--initialize-admin` command with credentials from User Secrets/environment variables; never commit credentials or promote existing non-admin accounts through it. Admin assignment is trusted server-side work. Role definitions are supplied by migration; see `docs/features/identity-persistence/roles-and-policies.md`.

## Resource Ownership

Use scoped `ResourceOwnershipService` in customer workflows after loading resources/parents from storage and before returning data or performing side effects. Caller IDs come only from `ICurrentUser` (authenticated GUID `sub`), never request owner fields. Holds/bookings use stored UserId; payments require a matching BookingId and booking owner. Admin has no implicit ownership bypass. Domain hold/booking rules reuse `ResourceOwnership.EnsureOwner`; ownership never overrides state/deadline rules. Refund-specific traversal is deferred until its entity exists. See `docs/features/identity-persistence/resource-ownership.md`.

## Time Conventions

Use UTC `DateTimeOffset` instants, normalized with `ToUniversalTime()`. Inject `TimeProvider` for current time and pass it to expiration/deadline rules; never read static wall clocks in business logic. The API registers `TimeProvider.System`; tests use a controllable clock without sleeps. Use the injected `BookingTimingPolicy` for hold/payment windows and cancellation lead time. Capture deadlines at creation; do not recalculate them during transitions. See `docs/time-conventions.md` and `docs/features/booking-timing/FEATURE.md`.

## Money Conventions

Use immutable `Money` values with explicit UAH/EUR/USD currency and decimal(18,2) limits. Reject excess fractional precision and mixed currencies. Derive hold/booking totals from server-resolved seat prices and payment amounts from booking totals; never trust client totals. Hold/booking items must capture independent amount/currency copies; never reprice existing history from current inventory or share EF-mutable Money instances across capture stages. See `docs/features/money-and-pricing/FEATURE.md`.

## Inventory Concurrency

SessionSeat.Version is an application-managed concurrency token. Use `SessionSeatAllocationWriter` for hold/transfer/confirm/release writes and retain the returned token, including Held→Held transfers. Tracked saves also check/advance Version. Never add ownership-changing bulk/raw SQL without expected-version predicates and increments, or blindly retry with a refreshed token. Coordinate aggregate changes in one transaction; see `docs/features/session-seat-concurrency/FEATURE.md`.

## Error Conventions

Use `DomainRuleException` with `DomainError` for expected business failures. Keep public snake_case values in `TicketBooking.Contracts.Errors.ErrorCodes` stable and map them through `ApiErrorCatalog`; never classify failures by exception message text. See `docs/features/api-diagnostics/error-codes.md`.

## Testing Guidelines

Tests use xUnit and `coverlet.collector`. Domain tests cover entity construction, price snapshots, ownership/deadline checks, and hold/booking/payment transitions; integration tests cover configuration/EF metadata and PostgreSQL constraints when `TICKETBOOKING_TEST_CONNECTION_STRING` targets an isolated database (CI configures it); Application tests cover reusable resource ownership; Architecture remains an empty scaffold. No coverage threshold is configured. Add meaningful behavior tests to the appropriate project, using descriptive names such as `BookTicket_WhenSoldOut_ReturnsFailure`. Use `dotnet test TicketBooking.slnx --collect:"XPlat Code Coverage"` to collect coverage. Add required project references when implementing the scaffolded test projects.

Add domain tests alongside new rules: cover allowed and rejected transitions, exact monetary totals and limits, and one tick before/at/after expiration or deadline cutoffs. Assert the specific `DomainError` and unchanged state on rejection. Use `ManualTimeProvider` for deterministic time and test interactions such as retries retaining the original deadline.

## Feature Documentation

When adding or changing a feature, use the `document-feature` skill
throughout the task:

1. **Before implementation:** Read existing documentation under
   `docs/features/` for the affected feature and relevant dependencies.
   Trace the relevant code to identify business rules, ownership
   boundaries, integrations, and implementation gaps.

2. **During implementation:** Consult this documentation when making
   design and behavior decisions. Verify assumptions against the code.
   Treat code as evidence of current behavior and task requirements
   as intended behavior; resolve discrepancies rather than blindly
   following outdated documentation.

3. **After implementation:** Create or update documentation under
   `docs/features/<feature>/` using the skill's ten-file contract.
   For existing documentation, update only affected content while
   preserving the required structure. Verify it against the final code
   and clearly distinguish implemented, incomplete, and unverified behavior.

Keep documentation concise and relevant to the task.
Documentation does not replace implementation or testing.

## Commit & Pull Request Guidelines

History currently contains only `Initial commit`, so no established message convention exists. Use short, imperative summaries and keep commits focused. PRs should describe the behavior change, related issues, validation performed, and any migration or configuration steps.

## Security & Configuration

Keep `.env` and credentials out of Git. Store `ConnectionStrings:DefaultConnection` and `Jwt:SigningKey` in API User Secrets. Both are required at startup. See `README.md` for database setup; deleting the Compose volume destroys local data.
