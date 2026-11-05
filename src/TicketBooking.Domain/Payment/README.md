# Payment

Namespace for future C# types: `TicketBooking.Domain.Payment`. Match any nested namespaces to their folders.

## Ownership

Payment attempts, financial transaction references, and payment/refund lifecycle rules when implemented.

## Boundary

Provider SDKs, HTTP calls, credentials, and webhook adapters belong in Infrastructure/API. Booking owns booking state; payment state alone is not a booking record.

These are responsibility guidelines for future implementation. No entities, policies, application operations, or integrations are implemented in this folder yet. Lifecycle rules and exact fields require feature-specific requirements.

Keep domain rules free of ASP.NET Core, EF Core, provider SDKs, and network I/O. Prefer identifiers or immutable values for cross-boundary references; use Application to coordinate workflows rather than having one boundary mutate another boundary's aggregates. All boundaries remain in the same assembly and process.

See the [domain boundary map](../README.md) for the shared conventions.
