# Integration points

| Boundary | Direction and contract | Authority and failure behavior |
| --- | --- | --- |
| HTTP caller → API | Health request, Development OpenAPI request, or failing request; registration anchors in Program.cs. | API/framework owns responses; no authentication policy or client retry contract is configured. |
| API → ASP.NET Core diagnostics | In-process `AddProblemDetails`, `AddHealthChecks`, `AddOpenApi`, and middleware. | Framework integrations, not external network services; configuration is owned by TicketBooking.Api. |
| API → persistence setup | Startup resolves required configuration and registers DbContext. | See [local persistence](../local-persistence/FEATURE.md); missing configuration prevents startup. Health does not query PostgreSQL. |

Response payloads are described in [data-model.md](data-model.md). Moving routes, changing environment gating or removing trace metadata would affect diagnostic clients. Runtime compatibility and exception negotiation were not tested. No webhooks, event bus, or third-party diagnostic service is configured.
