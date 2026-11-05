# Booking

Namespace for future C# types: `TicketBooking.Domain.Booking`. Match any nested namespaces to their folders.

## Ownership

Durable booking records, booked items or ticket allocations, and booking lifecycle rules when implemented.

## Boundary

Reference customer, offering, seat, and reservation identities rather than owning their mutable models. Payment owns financial transaction state; do not infer booking confirmation from a notification.

These are responsibility guidelines for future implementation. No entities, policies, application operations, or integrations are implemented in this folder yet. Lifecycle rules and exact fields require feature-specific requirements.

Keep domain rules free of ASP.NET Core, EF Core, provider SDKs, and network I/O. Prefer identifiers or immutable values for cross-boundary references; use Application to coordinate workflows rather than having one boundary mutate another boundary's aggregates. All boundaries remain in the same assembly and process.

See the [domain boundary map](../README.md) for the shared conventions.
