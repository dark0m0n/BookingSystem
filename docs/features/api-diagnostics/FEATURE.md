# API diagnostics

Status: **implemented** (API foundation only).

Inspected revision: `ea94ab900a3bc4b7b772e0f0cbcb20bb5d52532d`; working tree inspected. Implementation evidence is static source inspection; runtime behavior is **Not verified**.

Developers and operators can inspect API liveness, retrieve development OpenAPI documentation, and receive consistent error responses. Evidence: [Program.cs](../../../src/TicketBooking.Api/Program.cs), `AddHealthChecks`, `MapOpenApi`, `AddProblemDetails`, and exception/status middleware.

Scope: `/health`, development OpenAPI, HTTPS redirection, and error formatting. Booking, login, database readiness, and interactive Swagger UI are outside the implemented scope. Startup depends on database configuration and a JWT signing key; requiring a key does not implement authentication.

## Acceptance and verification

- Liveness endpoint is mapped without database checks: implemented; runtime Not verified.
- OpenAPI mapping is conditional on Development: implemented; runtime Not verified.
- ProblemDetails includes request `instance` and `traceId`: implemented; response negotiation and exception output Not verified at runtime.

Configure API User Secrets as described in [README](../../../README.md), including `Jwt:SigningKey`. Run `dotnet run --project src/TicketBooking.Api --launch-profile https`. Manually request `https://localhost:7281/health`, `/openapi/v1.json`, and an unknown route with `Accept: application/problem+json`. These checks were not run during documentation.

## Navigation

- [Business context and rules](business-context.md)
- [End-to-end flow](end-to-end-flow.md)
- [Verified entry points](entry-points.json)
- [Application responsibilities](per-app-responsibility.md)
- [Frontend structure](frontend-structure.md)
- [Backend structure](backend-structure.md)
- [Data model](data-model.md)
- [Integration points](integration-points.md)
- [Known gotchas](known-gotchas.md)
