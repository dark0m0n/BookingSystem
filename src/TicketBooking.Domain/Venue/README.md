# Venue

Namespace for C# types: `TicketBooking.Domain.Venue`. Match any nested namespaces to their folders.

## Ownership

Physical locations, rooms, seating layouts, and seat identities.

## Boundary

Own the physical layout, not event-specific seat holds or confirmed ticket allocations. Those belong to Reservation and Booking respectively.

Implemented types: Venue, Auditorium, Seat, and SeatType. Entities have publicly read-only properties and constructor validation; see the [model documentation](../../../docs/features/venue-catalogue-model/FEATURE.md). Admin venue/layout/event/session setup now exists in the API; see the [Admin contract](../../../docs/features/venue-catalogue-model/admin-http-contract.md). Public browsing and provider integrations remain future work. Relational mappings now exist in [Infrastructure](../../../docs/features/local-persistence/FEATURE.md).

Keep domain rules free of ASP.NET Core, EF Core, provider SDKs, and network I/O. Prefer identifiers or immutable values for cross-boundary references; use Application to coordinate workflows rather than having one boundary mutate another boundary's aggregates. All boundaries remain in the same assembly and process.

See the [domain boundary map](../README.md) for the shared conventions.
