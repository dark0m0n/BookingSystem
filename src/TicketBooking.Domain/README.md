# Domain boundaries

The domain is organized into seven logical boundaries within the existing `TicketBooking.Domain` project. They share one assembly; this organization adds no projects, services, DbContexts, or deployment units.

| Boundary | Responsibility |
| --- | --- |
| [Identity](Identity/README.md) | Accounts, identity references, and domain-level roles or access concepts. |
| [Catalogue](Catalogue/README.md) | Events and bookable offerings, their descriptive information, and scheduling concepts. |
| [Venue](Venue/README.md) | Physical locations, rooms, seating layouts, and seat identities. |
| [Reservation](Reservation/README.md) | Temporary holds on bookable inventory, including hold validity, expiry, and release rules when implemented. |
| [Booking](Booking/README.md) | Durable booking records, booked items or ticket allocations, and booking lifecycle rules when implemented. |
| [Payment](Payment/README.md) | Payment attempts, financial transaction references, and payment/refund lifecycle rules when implemented. |
| [Notification](Notification/README.md) | Notification intent and delivery outcome concepts when domain behavior requires them. |

## Placement and dependencies

Place C# types under their owning folder with namespace `TicketBooking.Domain.<Boundary>`. Venue and Catalogue contain the initial layout/catalogue entities; Reservation, Booking, and Payment contain lifecycle entities. Identity and Notification currently contain ownership guidance. Domain types do not imply implemented booking workflows.

Keep invariants with the entity or value object that owns them. Avoid generic `Models` or `Services` folders that mix boundary ownership. Keep Domain independent of API, Application, Contracts, Infrastructure, persistence frameworks, and external providers; the Domain project currently has no project or package references.

Prefer identifiers or immutable values when a model refers to another boundary. Avoid cross-boundary aggregate object graphs and direct mutation of another boundary's state. Application coordinates operations spanning boundaries; Infrastructure supplies persistence and external adapters. Introduce explicit contracts only when an implemented use case needs them. Folders and namespaces do not enforce isolation at compile time; no architecture test currently checks these rules.

## Reservation, booking, and payment

Reservation owns temporary holds; Booking owns durable booking records and ticket allocations; Payment owns financial transaction state. Local hold/booking/payment transitions are now defined in the [lifecycle bundle](../../docs/features/reservation-booking-payment/FEATURE.md). No fixed hold duration, cancellation window, or payment timeout is prescribed; callers supply authoritative policy deadlines and a `TimeProvider` for current UTC time. All stored instants are normalized to UTC; see [time conventions](../../docs/time-conventions.md). Application-level consistency, refunds, and transaction strategies remain future work.

## Current implementation

Venue contains Venue, Auditorium, and Seat. Catalogue contains Event, Session, and SessionSeat with local constructor validation. See the [model documentation](../../docs/features/venue-catalogue-model/FEATURE.md) for fields and limits. Reservation adds SeatHold/SeatHoldItem; Booking adds Booking/BookingItem; Payment adds Payment, with local lifecycle guards and immutable price snapshots. No application workflows, domain events, mapped database entities, or boundary-specific endpoints exist yet. No message bus, event dispatch, or provider integration is introduced. The API and shared persistence foundation remain unchanged.

See [feature documentation](../../docs/features/domain-boundaries/FEATURE.md) for implementation status and evidence.
