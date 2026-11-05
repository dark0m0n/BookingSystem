# Venue

Namespace for C# types: `TicketBooking.Domain.Venue`. Match any nested namespaces to their folders.

## Ownership

Physical locations, rooms, seating layouts, and seat identities.

## Boundary

Own the physical layout, not event-specific seat holds or confirmed ticket allocations. Those belong to Reservation and Booking respectively.

Implemented types: Venue, Auditorium, Seat, and SeatType. Entities have get-only properties and constructor validation; see the [model documentation](../../../docs/features/venue-catalogue-model/FEATURE.md). No application operations, lifecycle transitions, persistence mappings, or integrations are implemented yet.

Keep domain rules free of ASP.NET Core, EF Core, provider SDKs, and network I/O. Prefer identifiers or immutable values for cross-boundary references; use Application to coordinate workflows rather than having one boundary mutate another boundary's aggregates. All boundaries remain in the same assembly and process.

See the [domain boundary map](../README.md) for the shared conventions.
