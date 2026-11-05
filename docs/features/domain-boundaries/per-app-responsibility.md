# Application responsibilities

**TicketBooking.Api** remains the only deployable application. Its startup and endpoints are unchanged. **postgres** remains the existing separately running database service.

**TicketBooking.Domain** remains one library with seven responsibility folders. Identity owns identity concepts; Catalogue owns offerings; Venue owns physical layouts; Reservation owns temporary holds; Booking owns durable bookings; Payment owns financial state; Notification owns notification intent/delivery concepts. Venue and Catalogue have initial models documented in the [model bundle](../venue-catalogue-model/FEATURE.md); Reservation, Booking, and Payment now have [lifecycle entities](../reservation-booking-payment/FEATURE.md). Identity and Notification remain prospective. Their tables are now mapped in the [persistence layer](../local-persistence/FEATURE.md), without separate services.

**TicketBooking.Application** remains the intended place for use-case coordination. **TicketBooking.Infrastructure** retains persistence and future external adapters. **TicketBooking.Contracts** remains the shared contract library. This change adds no runtime calls, data transfers, human ownership assignments, or new deployment units.

Evidence: [domain map](../../../src/TicketBooking.Domain/README.md) and [solution](../../../TicketBooking.slnx).
