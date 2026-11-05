# Identity

Namespace for future C# types: `TicketBooking.Domain.Identity`. Match any nested namespaces to their folders.

## Ownership

Accounts, identity references, and domain-level roles or access concepts.

## Boundary

Authentication middleware, token handling, and credential storage belong in the appropriate API/Infrastructure implementations. Do not put payment or booking state here.

These are responsibility guidelines for future implementation. No entities, policies, application operations, or integrations are implemented in this folder yet. Lifecycle rules and exact fields require feature-specific requirements.

Keep domain rules free of ASP.NET Core, EF Core, provider SDKs, and network I/O. Prefer identifiers or immutable values for cross-boundary references; use Application to coordinate workflows rather than having one boundary mutate another boundary's aggregates. All boundaries remain in the same assembly and process.

See the [domain boundary map](../README.md) for the shared conventions.
