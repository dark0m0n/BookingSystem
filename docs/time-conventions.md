# UTC time and clocks

## Stored instants

Use `DateTimeOffset` for instants and normalize with `ToUniversalTime()` at domain construction boundaries. `Session.StartsAt`, `Session.EndsAt`, `SeatHold.ExpiresAt`, `Booking.CreatedAt`, `Booking.SessionStartsAt`, `Booking.PaymentDeadline`, and `Booking.CancellationDeadline` always expose offset `00:00`. An input carrying another explicit offset is accepted and converted without changing its instant. Do not interpret an unspecified local wall time as UTC; resolve its time zone/offset before constructing domain objects.

Use `TimeSpan` for durations. Local time conversion belongs at future presentation/input boundaries; no venue time-zone policy or UI is implemented yet. The [persistence mappings](features/local-persistence/data-model.md) now store instants as PostgreSQL timestamp with time zone; domain inputs remain UTC-normalized. Future API contracts should serialize UTC using ISO 8601 `Z` or `+00:00`.

## Current time

Use the built-in `TimeProvider`, not direct `DateTime.Now`, `DateTime.UtcNow`, or `DateTimeOffset.UtcNow` reads in business rules. The API registers `TimeProvider.System` as a singleton in [Program.cs](../src/TicketBooking.Api/Program.cs). Future application handlers should inject `TimeProvider` and pass it into the existing domain operations:

```csharp
hold.EnsureCanConvert(userId, timeProvider);
hold.Convert(userId, timeProvider);
hold.Cancel(userId, timeProvider);
hold.Expire(timeProvider);
booking.Confirm(timeProvider);
booking.Cancel(userId, timeProvider);
booking.Expire(timeProvider);
```

SeatHold construction and `Booking.CreateFromHold` also require the clock, an injected BookingTimingPolicy, and the authoritative session-start timestamp. Entities do not retain it as state. Each operation samples `GetUtcNow()` once; booking creation shares that reading with the internal hold-validity check and `CreatedAt`. The internal `EnsureCanConvertAt` method is not a public caller-time API.

All clocks are explicit required dependencies; missing clocks throw ArgumentNullException rather than silently using wall time. Session-start inputs are DateTimeOffset values normalized to UTC. The [configured timing policy](features/booking-timing/FEATURE.md) derives and captures hold/payment/cancellation deadlines at creation; transition methods do not accept deadline overrides. The clock supplies current time without defining durations or transaction behavior.

## Exact boundaries and tests

- Conversion/cancellation of a hold requires current UTC time strictly before ExpiresAt.
- Hold expiration is allowed at ExpiresAt and later.
- Booking cancellation requires current UTC time strictly before the policy deadline.
- Booking payment initiation and confirmation require current UTC time strictly before the captured payment deadline.
- Booking expiration is allowed at that deadline and later.

Tests use the per-test [ManualTimeProvider](../tests/TicketBooking.Domain.Tests/ManualTimeProvider.cs); advance it instead of sleeping. [UtcTimeTests](../tests/TicketBooking.Domain.Tests/UtcTimeTests.cs) exercises UTC normalization, different input offsets, advancing-clock boundaries, missing clocks, and a single-read booking factory. No timer-based worker, scheduler, database UTC constraint, or asynchronous expiration service is implemented.
