# Backend structure

[Program.cs](../../../src/TicketBooking.Api/Program.cs) retrieves `ConnectionStrings:DefaultConnection`, throws if absent, and calls `AddDbContext<TicketBookingDbContext>` with `UseNpgsql`. It separately requires `Jwt:SigningKey`; no authentication flow consumes that key.

[TicketBookingDbContext.cs](../../../src/TicketBooking.Infrastructure/Persistence/TicketBookingDbContext.cs) defines a sealed context with injected `DbContextOptions<TicketBookingDbContext>`. `OnModelCreating` calls the base method and `ApplyConfigurationsFromAssembly`. No configurations, DbSet properties, entities, repositories, CRUD endpoints, workers, transaction boundaries, concurrency controls, or idempotency checks are implemented.

[Infrastructure project](../../../src/TicketBooking.Infrastructure/TicketBooking.Infrastructure.csproj) references the Npgsql EF Core provider. [InitialCreate](../../../src/TicketBooking.Infrastructure/Persistence/Migrations/20261026113308_InitialCreate.cs) contains empty `Up`/`Down`; startup does not call `Migrate` or `EnsureCreated`.

The [README local startup command](../../../README.md#start-locally) applies migrations explicitly through `dotnet ef database update` before launching the API. It restores the tool from [dotnet-tools.json](../../../dotnet-tools.json), targets Infrastructure, and uses API as the startup project in Development. This operator command does not add automatic migration execution to Program.cs. Actual migration application remains Not verified.

Compose variables: `POSTGRES_DB`, `POSTGRES_USER`, `POSTGRES_PASSWORD`, and `POSTGRES_PORT`. API connection configuration is separate. Existing [integration test](../../../tests/TicketBooking.IntegrationTests/UnitTest1.cs) is an empty xUnit fact; database behavior has no meaningful test coverage in the inspected source. Tests were not run.
