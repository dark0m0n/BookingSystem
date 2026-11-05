# Booking

Namespace for C# types: `TicketBooking.Domain.Booking`. Match any nested namespaces to their folders.

## Ownership

Durable booking records, booked items or ticket allocations, and booking lifecycle rules when implemented.

## Boundary

Reference customer, offering, seat, and reservation identities rather than owning their mutable models. Payment owns financial transaction state; do not infer booking confirmation from a notification.

Implemented types: Booking, BookingItem, and BookingStatus. Local state transitions and validation are documented in the [lifecycle bundle](../../../docs/features/reservation-booking-payment/FEATURE.md). Application transactions and provider integrations are not implemented. [Mapped persistence and constraints](../../../docs/features/local-persistence/FEATURE.md) now store the entities and snapshots. Refund and messaging entities remain deferred to their planned phases.

Keep domain rules free of ASP.NET Core, EF Core, provider SDKs, and network I/O. Prefer identifiers or immutable values for cross-boundary references; use Application to coordinate workflows rather than having one boundary mutate another boundary's aggregates. All boundaries remain in the same assembly and process.

See the [domain boundary map](../README.md) for the shared conventions.

Monetary fields use immutable `Money` with explicit currency and decimal(18,2) limits. See [money and pricing](../../../docs/features/money-and-pricing/FEATURE.md) for server-derived totals and the current integration limits.
