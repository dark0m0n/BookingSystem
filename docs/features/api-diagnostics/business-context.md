# Business context

Actors: developers inspecting the API and operators checking process liveness. Prerequisites: a runnable .NET 10 API, required configuration, and a trusted local HTTPS certificate for HTTPS requests.

| Rule | Implemented policy | Enforcement and evidence |
| --- | --- | --- |
| BR-01 | Expose liveness independently of database readiness. | [Program.cs](../../../src/TicketBooking.Api/Program.cs), `AddHealthChecks` and `MapHealthChecks`; no database check registered. |
| BR-02 | Map OpenAPI only in Development. | `IsDevelopment` around `MapOpenApi` in Program.cs. |
| BR-03 | Customize ProblemDetails with request path and trace correlation. | `AddProblemDetails`, `Instance`, and `Extensions["traceId"]` in Program.cs. |
| BR-04 | Use exception and status-code handlers in every environment. | `UseExceptionHandler` and `UseStatusCodePages` before the environment branch. |

All rules are supported by source; runtime verification is absent. No endpoint authorization or login flow is registered. Confirmation, pricing, refund, and eligibility policies: Not applicable because there are no booking operations. Availability targets and database-readiness requirements are not specified.
