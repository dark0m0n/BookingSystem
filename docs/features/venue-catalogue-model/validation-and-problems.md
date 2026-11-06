# Validation and ProblemDetails

Status: implemented; working tree inspected. API-local FluentValidation 12.2.0 rules cover catalogue query inputs and every Admin body-write contract. Contracts/Domain stay independent of FluentValidation. Authentication request validation is unchanged.

[Official FluentValidation guidance](https://docs.fluentvalidation.net/en/latest/aspnet.html) supports manual validation in Minimal APIs. The API registers validators explicitly and calls ValidateAsync through Admin endpoint filters or public query handlers, without MVC automatic validation. Version is pinned in the [API project](../../../src/TicketBooking.Api/TicketBooking.Api.csproj).

| Contract | Rules |
| --- | --- |
| CreateVenueRequest | Nonblank trimmed name ≤200/address ≤500. |
| CreateAuditoriumRequest / UpdateAuditoriumRequest | Required nonempty venue ID on create; nonblank trimmed name ≤200. |
| SeatRequest / AddSeatsRequest | Row nonblank/≤20, number positive, defined type; batch 1–1000 nonnull items with indexed field errors. |
| CreateEventRequest | Title nonblank/≤200, description nonnull/≤10000, duration positive, age nonnegative, defined status. |
| UpdateEventRequest | Validate supplied nonnull values under the same limits; require at least one field. Empty description clears it. |
| CreateSessionRequest | Nonempty event/auditorium IDs, future UTC start at PostgreSQL microsecond precision, validated Money price/currency. |
| UpdateSessionRequest | Require one supplied field; supplied IDs nonempty and supplied start future. |
| CatalogueQuery | Text ≤200 after trimming, exact UTC yyyy-MM-dd date, nonempty GUID, positive page, pageSize 1–100, Int32-safe offset, no repeated known parameters. |

Singleton validators are stateless and read injected TimeProvider when validating. They do not perform storage access. Parent existence, publication status, duplicate coordinates (409), reservation protections, layout consistency and time checks after waiting for locks remain in transactional handlers. Authorization precedes Admin binding/filter execution, preserving 401/403 even for invalid input.

FV failures return `application/problem+json`:

```json
{
  "type": "urn:ticketbooking:error:validation_failed",
  "title": "Invalid request",
  "status": 400,
  "instance": "/api/admin/auditoriums/00000000-0000-0000-0000-000000000001/seats",
  "code": "validation_failed",
  "traceId": "example-trace-id",
  "errors": { "seats[0].row": ["A row of at most 20 characters is required."] }
}
```

Clients branch on code/status and display errors by field key, without parsing wording. Indexed keys and root patch keys (`event`, `session`) are preserved. Examples are illustrative and contain no credentials, owner IDs or internal exception details.

| HTTP / code | Documented outcome |
| --- | --- |
| 400 validation_failed | FluentValidation field errors, malformed JSON, null required body or binding failure. The endpoint filter reports a null body under request; syntax/type-binding failures may omit errors. |
| 401 unauthorized | Missing/invalid Admin authentication. |
| 403 forbidden | Authenticated caller lacks Admin permission. |
| 404 not_found | Missing or publicly hidden resource/reference. |
| 409 conflict | Duplicate coordinates, fixed layout, unsafe session edit, invalid event state or inconsistent map. Existing seat_not_available/concurrency_conflict exception mappings remain supported. |
| 415 http_error | Unsupported content type on an Admin body write. |
| 500 internal_error | Unexpected failure; safe public envelope, internal details logged only. |

Public routes declare their applicable 400/404/409 outcomes and 500. Admin routes share 401/403/500, body writes add 400/415, and resource/state routes add 404/409. Development `/openapi/v1.json` includes ProblemDetails schemas, descriptions and safe examples through [ProblemDetailsOperationTransformer](../../../src/TicketBooking.Api/Errors/ProblemDetailsOperationTransformer.cs). Actual application exposure remains Development-only. FV rules do not automatically become JSON-schema range/length constraints; the limits above are authoritative documentation.

Evidence: [validators/filter registration](../../../src/TicketBooking.Api/Validation), [error adapter](../../../src/TicketBooking.Api/Errors/ApiProblemDetails.cs), [validator boundaries](../../../tests/TicketBooking.IntegrationTests/CatalogueValidatorTests.cs), [HTTP and generated OpenAPI assertions](../../../tests/TicketBooking.IntegrationTests/AuthEndpointsTests.Validation.cs). No database migration or new configuration is required.
