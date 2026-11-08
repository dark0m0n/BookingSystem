# Configurable booking timing

Status: **partial**. Startup configuration, deadline calculations, captured entity deadlines, and local enforcement are implemented; customer hold HTTP workflows and Active-hold expiry are implemented; booking/payment workflows and pending-booking expiry remain absent. [Persistence mappings](../local-persistence/FEATURE.md) now store captured deadlines. Working tree inspected on top of commit `5ba7f54`, including the uncommitted Money changes.

Operators can configure hold duration, payment completion window, and how far before session start cancellation closes. Defaults are implementation choices: 10 minutes, 15 minutes, and one hour respectively. Trusted application code supplies the authoritative session start and injects the immutable BookingTimingPolicy and TimeProvider.

After conversion, the pending booking retains seats until its captured PaymentDeadline, independently of the original hold expiry. The [seat-retention and late-payment decision](seat-retention-and-late-payments.md) defines expiry, callback races, and compensation requirements, distinguishing planned allocation workflows from implemented domain guards.

## Acceptance and verification

- `BookingTiming` settings bind to validated TimeSpan values at API startup; malformed/invalid values reject registration.
- Holds and bookings calculate UTC deadlines, capping hold/payment windows at session start. Cancellation cutoff is session start minus lead time.
- Existing entities retain captured deadlines. Late payment initiation/confirmation and cancellation reject at equality; expiration is allowed at equality.
- Domain/configuration tests cover overrides, defaults, caps, offsets, snapshots, and boundary rejection. The expanded domain suite passed 159 tests, including [deadline interactions](../../../tests/TicketBooking.Domain.Tests/DeadlineInteractionTests.cs). Active-hold expiry/release now has PostgreSQL integration tests; pending-booking expiry and provider callbacks remain unimplemented.

See [README settings](../../../README.md#booking-timing) and [time conventions](../../time-conventions.md). Run `dotnet test TicketBooking.slnx --configuration Release`; configuration tests use in-memory settings and do not start the real API or require secrets/PostgreSQL.

## Navigation

- [Business context and rules](business-context.md)
- [End-to-end flow](end-to-end-flow.md)
- [Entry points](entry-points.json)
- [Application responsibilities](per-app-responsibility.md)
- [Frontend structure](frontend-structure.md)
- [Backend structure](backend-structure.md)
- [Data model](data-model.md)
- [Integration points](integration-points.md)
- [Known gotchas](known-gotchas.md)
- [Seat retention and late payment results](seat-retention-and-late-payments.md)
