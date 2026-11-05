# API diagnostics

Status: **implemented** (API foundation and error contract). Working tree inspected.

Developers can inspect liveness and Development OpenAPI. Clients receive stable machine-readable failures independently of message wording. `AddApiErrors`, `ApiErrorCatalog`, and the shared `ErrorCodes` constants implement the error contract; see [backend structure](backend-structure.md).

Scope: `/health`, Development OpenAPI, HTTPS redirection, and request error formatting. Booking/payment business endpoints, authentication, database readiness, and Swagger UI remain outside this feature. Startup still requires database configuration and a JWT signing key.

## Acceptance and verification

- Domain conflicts map to stable codes and safe ProblemDetails: verified by domain and real HTTP integration tests.
- Exception/status responses include `code`, `instance`, `traceId`, and a code-specific `type`: verified, including unsupported Accept headers, missing routes, and wrong methods.
- Identical payment outcomes remain idempotent: verified; conflicting outcomes return `payment_already_processed`.
- Liveness, OpenAPI, production host startup, TLS and redirects: source inspected; runtime Not verified by these tests.

Run `dotnet test TicketBooking.slnx --filter FullyQualifiedName~ApiErrorContractTests` for the HTTP contract. Tests start an isolated local HTTP server with production error registrations and synthetic routes; they do not require PostgreSQL or API secrets. Configure the real API using [README](../../../README.md).

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
- [Error code contract](error-codes.md)
