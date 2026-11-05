# Data model

No diagnostic entities, relations, database writes, or status lifecycle are declared. [Program.cs](../../../src/TicketBooking.Api/Program.cs) configures transient response metadata:

| Field | Source | Semantics |
| --- | --- | --- |
| `instance` | Request path, when not already supplied | Identifies the request associated with a problem. |
| `traceId` | Current activity ID, falling back to HTTP trace identifier | Correlates the response with diagnostics. |

Other ProblemDetails fields and generated OpenAPI content are framework-produced; exact runtime payloads are Not verified. README describes a `Healthy` liveness response; it is not evidence of database readiness.

Migration involvement: Not applicable; diagnostics have no persistence schema. Trace retention, logging storage, and PII policies are not established by the inspected code. Avoid assuming trace correlation implies persistent logging.
