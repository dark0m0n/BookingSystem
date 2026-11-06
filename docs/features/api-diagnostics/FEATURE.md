# API diagnostics

Status: **implemented** (API foundation and error contract). Working tree inspected.

Developers can inspect liveness and Development OpenAPI. Clients receive stable machine-readable failures independently of message wording. `AddApiErrors`, `ApiErrorCatalog`, and the shared `ErrorCodes` constants implement the error contract; see [backend structure](backend-structure.md).

Scope: `/health`, Development OpenAPI, HTTPS redirection, and request error formatting. Booking/payment business endpoints, authentication, database readiness, and Swagger UI remain outside this feature. Startup still requires database configuration and a JWT signing key.

## Acceptance and verification

- Domain conflicts map to stable codes and safe ProblemDetails: verified by domain and real HTTP integration tests.
- Exception/status responses include `code`, `instance`, `traceId`, and a code-specific `type`: verified, including unsupported Accept headers, missing routes, and wrong methods.
- Identical payment outcomes remain idempotent: verified; conflicting outcomes return `payment_already_processed`.
- Generated catalogue/Admin OpenAPI ProblemDetails schemas, status codes and safe examples are HTTP-tested in the isolated fixture. Real production startup, Development-only exposure, liveness, TLS and redirects remain source-inspected rather than runtime-verified.

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

Catalogue/Admin validation now uses FluentValidation with the existing stable error envelope; see [validation and ProblemDetails](../venue-catalogue-model/validation-and-problems.md). Authentication validation and business error code meanings are unchanged.

Latest validation: all 640 solution tests passed with zero skips against disposable PostgreSQL, including generated ProblemDetails schemas/examples and malformed/null body/media-type cases. Formatting and affected documentation reference/link checks passed; no migration is required.
