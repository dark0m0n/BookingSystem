# Application responsibilities

- **TicketBooking.Domain.Money** is a shared immutable value in the existing Domain assembly. It owns local currency/precision validation and same-currency addition; it is not a service or exchange-rate component.
- **Catalogue** owns current SessionSeat prices.
- **Reservation** snapshots selected inventory prices and calculates the hold total.
- **Booking** owns historical booking items and derives its total from them.
- **Payment** derives the charge from a pending booking and owns its attempt state. It reads booking values without mutating the booking.
- **Application/API**, when implemented, must load authoritative inventory and bookings, check access, ignore client-supplied totals, and coordinate transactions.
- **Infrastructure** maps Money amounts/currency through EF complex properties in the [persistence layer](../local-persistence/FEATURE.md). Provider validation/adapters are still absent.

The existing API deployable, PostgreSQL service, and project references remain unchanged. No currency database, pricing microservice, frontend, tax service, human owner, or new deployment unit is introduced.
