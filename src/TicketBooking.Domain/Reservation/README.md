# Reservation

Namespace for future C# types: `TicketBooking.Domain.Reservation`. Match any nested namespaces to their folders.

## Ownership

Temporary holds on bookable inventory, including hold validity, expiry, and release rules when implemented.

## Boundary

A reservation is temporary; a durable booking belongs to Booking. Do not own payment-provider state or duplicate Venue layouts.

These are responsibility guidelines for future implementation. No entities, policies, application operations, or integrations are implemented in this folder yet. Lifecycle rules and exact fields require feature-specific requirements.

Keep domain rules free of ASP.NET Core, EF Core, provider SDKs, and network I/O. Prefer identifiers or immutable values for cross-boundary references; use Application to coordinate workflows rather than having one boundary mutate another boundary's aggregates. All boundaries remain in the same assembly and process.

See the [domain boundary map](../README.md) for the shared conventions.
