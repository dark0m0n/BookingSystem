# Application responsibilities

The deployable API and PostgreSQL service are unchanged. All six types are part of the single TicketBooking.Domain library:

- **Venue** boundary owns Venue, Auditorium, Seat, and SeatType: physical layout only.
- **Catalogue** boundary owns Event, Session, SessionSeat, and their status enums: offerings, schedule, and per-session inventory representation.
- **Reservation/Booking** retain responsibility for future temporary-hold and durable-booking workflows; SessionSeat status does not implement those workflows.
- **Application** will coordinate referenced-entity checks and cross-boundary operations when implemented.
- **Infrastructure** retains EF/provider responsibility; no mappings are added here.
- **Contracts/API** receive no DTOs, routes, or public HTTP behavior in this change.

The entity constructors own local validation and return read-only scalar values. They perform no external calls or data access. Human owners and deployed database ownership are not assigned. Evidence: [domain map](../../../src/TicketBooking.Domain/README.md) and [entity source index](entry-points.json).
