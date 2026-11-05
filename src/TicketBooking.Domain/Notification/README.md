# Notification

Namespace for future C# types: `TicketBooking.Domain.Notification`. Match any nested namespaces to their folders.

## Ownership

Notification intent and delivery outcome concepts when domain behavior requires them.

## Boundary

Email/SMS providers, templates tied to delivery technology, and sending adapters belong outside Domain. Delivery failure must not itself redefine booking or payment state.

These are responsibility guidelines for future implementation. No entities, policies, application operations, or integrations are implemented in this folder yet. Lifecycle rules and exact fields require feature-specific requirements.

Keep domain rules free of ASP.NET Core, EF Core, provider SDKs, and network I/O. Prefer identifiers or immutable values for cross-boundary references; use Application to coordinate workflows rather than having one boundary mutate another boundary's aggregates. All boundaries remain in the same assembly and process.

See the [domain boundary map](../README.md) for the shared conventions.
