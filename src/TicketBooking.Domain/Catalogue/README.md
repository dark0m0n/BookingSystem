# Catalogue

Namespace for C# types: `TicketBooking.Domain.Catalogue`. Match any nested namespaces to their folders.

## Ownership

Events and bookable offerings, their descriptive information, and scheduling concepts.

## Boundary

Reference venue identities for where an offering takes place. Venue layout belongs to Venue; temporary holds belong to Reservation.

Implemented types: Event, Session, SessionSeat, EventStatus, SessionStatus, and SessionSeatStatus. Entities have publicly read-only properties and constructor validation; see the [model documentation](../../../docs/features/venue-catalogue-model/FEATURE.md). No application operations, lifecycle transitions, or provider integrations are implemented yet. Relational mappings now exist in [Infrastructure](../../../docs/features/local-persistence/FEATURE.md).

Keep domain rules free of ASP.NET Core, EF Core, provider SDKs, and network I/O. Prefer identifiers or immutable values for cross-boundary references; use Application to coordinate workflows rather than having one boundary mutate another boundary's aggregates. All boundaries remain in the same assembly and process.

See the [domain boundary map](../README.md) for the shared conventions.

Monetary fields use immutable `Money` with explicit currency and decimal(18,2) limits. See [money and pricing](../../../docs/features/money-and-pricing/FEATURE.md) for server-derived totals and the current integration limits.
