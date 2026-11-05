# Reservation

Namespace for C# types: `TicketBooking.Domain.Reservation`. Match any nested namespaces to their folders.

## Ownership

Temporary holds on bookable inventory, including hold validity, expiry, and release rules when implemented.

## Boundary

A reservation is temporary; a durable booking belongs to Booking. Do not own payment-provider state or duplicate Venue layouts.

Implemented types: SeatHold, SeatHoldItem, and SeatHoldStatus. Local state transitions and validation are documented in the [lifecycle bundle](../../../docs/features/reservation-booking-payment/FEATURE.md). Application transactions and provider integrations are not implemented. [Mapped persistence and constraints](../../../docs/features/local-persistence/FEATURE.md) now store the entities and snapshots. Refund and messaging entities remain deferred to their planned phases.

Keep domain rules free of ASP.NET Core, EF Core, provider SDKs, and network I/O. Prefer identifiers or immutable values for cross-boundary references; use Application to coordinate workflows rather than having one boundary mutate another boundary's aggregates. All boundaries remain in the same assembly and process.

See the [domain boundary map](../README.md) for the shared conventions.

Monetary fields use immutable `Money` with explicit currency and decimal(18,2) limits. See [money and pricing](../../../docs/features/money-and-pricing/FEATURE.md) for server-derived totals and the current integration limits.
