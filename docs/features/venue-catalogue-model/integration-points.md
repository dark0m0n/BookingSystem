# Integration points

Internal relationship direction is Auditorium → VenueId, Seat → AuditoriumId, Session → EventId/AuditoriumId, and SessionSeat → SessionId/SeatId. Each referenced boundary remains authoritative for its own data; an ID alone does not prove the referenced object exists. No runtime fetch, authentication, retry, or network request is involved in constructing these objects.

The existing Application and Infrastructure project references can consume Domain types; no consumers are implemented in this change. [Domain boundaries](../domain-boundaries/FEATURE.md) defines ownership. [Local persistence](../local-persistence/FEATURE.md) remains an unmapped EF foundation; adding public domain classes alone does not register database entities.

Future Reservation/Booking use cases must define how they coordinate SessionSeat changes and concurrency. No integration contract, event payload, hold ownership, payment linkage, or delivery behavior is invented here. Changing identifier/property types or enum values affects future consumers and persistence mappings; no external schema is published yet.
