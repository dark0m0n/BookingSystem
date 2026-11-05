# Integration points

The boundaries share the same Domain assembly. This change introduces no runtime network integrations, events, contract types, message broker, webhooks, authentication exchanges, retry mechanisms, or service-to-service calls.

Existing project dependencies remain: Application references Domain and Contracts; Infrastructure references Application, Domain, and Contracts; API references Application, Infrastructure, and Contracts. Evidence: [Application project](../../../src/TicketBooking.Application/TicketBooking.Application.csproj), [Infrastructure project](../../../src/TicketBooking.Infrastructure/TicketBooking.Infrastructure.csproj), and [API project](../../../src/TicketBooking.Api/TicketBooking.Api.csproj).

Contributor convention: Application coordinates spanning operations; Domain keeps owned invariants; Infrastructure handles providers and storage. Specific inputs/outputs and consistency contracts are unresolved until a use case is implemented. Introducing a direct project dependency from Domain to outer layers would violate the documented direction.

Existing [API diagnostics](../api-diagnostics/FEATURE.md) and [local persistence](../local-persistence/FEATURE.md) runtime behavior is unchanged.
