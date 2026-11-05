# Backend structure

[Program.cs](../../../src/TicketBooking.Api/Program.cs) contains top-level registrations and inline configuration:

- `AddOpenApi` and Development-only `MapOpenApi` provide generated API documentation.
- `AddHealthChecks` and `MapHealthChecks("/health")` expose liveness without dependency checks. No method restriction is declared at this registration.
- `AddProblemDetails` customizes `Instance` and `traceId` using `Activity.Current?.Id` or `TraceIdentifier`.
- `UseExceptionHandler` and `UseStatusCodePages` register error middleware; `UseHttpsRedirection` precedes endpoint execution.
- Startup requires `ConnectionStrings:DefaultConnection` and `Jwt:SigningKey`. The key is read but no authentication/authorization services or middleware use it.

[launchSettings.json](../../../src/TicketBooking.Api/Properties/launchSettings.json) defines Development profiles: HTTPS port 7281 and HTTP port 5222. No controllers, application handlers, workers, transaction logic, or idempotency mechanism exist for diagnostics.

The four `tests/*/UnitTest1.cs` files contain empty xUnit facts, including [integration tests](../../../tests/TicketBooking.IntegrationTests/UnitTest1.cs). They do not verify these behaviors. No runtime tests were run for this documentation.
