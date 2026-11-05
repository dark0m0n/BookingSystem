# Data model

No diagnostic tables, migrations or persisted error lifecycle exist. DomainRuleException carries a DomainError enum plus an internal diagnostic message. API owns the translation to ApiErrorDefinition(Code, Status, Title, Detail); Contracts owns stable wire strings.

| ProblemDetails field | Source / semantics |
| --- | --- |
| code | Stable snake_case constant; clients branch on this field. |
| type | `urn:ticketbooking:error:<code>` for exception/status writer responses. |
| status | HTTP status repeated in JSON. |
| title, detail | Fixed public descriptions from ApiErrorCatalog; wording is not a machine contract. |
| instance | Request path if not explicitly supplied. |
| traceId | Current activity ID or HTTP trace identifier. |

[Enrich](../../../src/TicketBooking.Api/Errors/ApiProblemDetails.cs) also supplies status-based codes to framework ProblemDetails when omitted; explicit caller codes/types are preserved. Existing arbitrary response bodies are outside this enrichment contract.

No exception messages, provider references, SQL or stack traces are copied into error payloads. Unexpected exceptions are logged server-side. Log retention and storage policy are not established here. [error-codes.md](error-codes.md) lists code semantics and statuses.
