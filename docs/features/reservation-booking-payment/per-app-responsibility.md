# Application responsibilities

All new code lives in the single TicketBooking.Domain assembly:

- **Reservation** owns SeatHold, SeatHoldItem, expiry/owner checks, and hold status. It does not mutate SessionSeat or Booking.
- **Booking** owns Booking, BookingItem, historical totals, and booking status. Its factory shares one clock reading with the hold's internal validation and reads snapshots without mutating that aggregate. It does not know Payment objects or provider behavior.
- **Payment** owns payment attempt identity, amount/key, provider reference, and result guards. It never confirms/cancels a booking itself.
- **Catalogue** continues to own SessionSeat representation. No seat mutation/concurrency method is added.

API registers TimeProvider.System as the singleton clock. Entities receive TimeProvider per operation and do not retain it. Future application handlers should inject that registration.

Application is responsible for future transactions spanning owners, authoritative deadlines/identities, amount/reference checks, payment deduplication, and atomic seat updates. Infrastructure will implement persistence/provider adapters; API will enforce authentication and map errors. Beyond clock registration, no application handlers or infrastructure adapters are implemented, and no new deployables are added.

Notification, refund processing, Outbox, and consumers are deferred. Existing PostgreSQL is not called by these methods. No Redis/RabbitMQ/Mailpit service or human ownership assignment is introduced.
