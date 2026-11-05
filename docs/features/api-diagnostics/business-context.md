# Business context

Actors: diagnostic clients, future booking clients, developers and operators. The real API requires startup configuration; HTTPS clients need a trusted certificate.

| Rule | Policy | Enforcement and evidence |
| --- | --- | --- |
| BR-01 | Liveness is independent of database readiness. | Program.cs `AddHealthChecks`/`MapHealthChecks`; source inspected. |
| BR-02 | OpenAPI exists only in Development. | Program.cs `IsDevelopment`; source inspected. |
| BR-03 | Errors carry request path, trace correlation and stable codes. | ApiProblemDetails `Enrich`/`WriteAsync`; HTTP tested. |
| BR-04 | Use safe exception/status formatting in every environment. | Program.cs middleware; HTTP tests run in Production. |
| BR-05 | Expected domain failures have explicit identities; never parse messages. | DomainRuleException.Error → ApiErrorCatalog → ErrorCodes; tested. |
| BR-06 | Required state conflicts return 409; ownership failures return 403. | ApiErrorCatalog.ForDomainError; tested. No authentication implementation implied. |
| BR-07 | Identical payment result/reference replay succeeds; conflicting outcome fails. | Payment.Complete; domain and HTTP tested. Reference mismatch is a separate failure. |

Detailed codes and guard precedence: [error-codes.md](error-codes.md). Failed guards do not mutate aggregate state. HTTP middleware does not coordinate database transactions, releases, refunds or retries; these application workflows remain incomplete.
