# Reservation, booking, and payment domain lifecycle

Status: **partial**. Domain entities and local state transitions are implemented; application workflows, persistence, and external integrations are absent.

Working tree inspected on top of `fbed4d974e84f7625413c61379701f3b21dcff88`. Requirements came from the user-provided `TicketBooking_Project_Plan.md`, sections 5–7 and phases 3/5/6/7. The source plan is outside the repository; the relevant rules and design interpretations are recorded in this bundle rather than requiring that local file to run the project.

A future customer can hold seats, create a booking, and complete a simulated payment. The present supported actor is trusted application/test code: it can construct holds with price snapshots, derive pending bookings from valid owned holds, and perform guarded lifecycle operations. No customer-facing endpoints are implemented. Stored instants follow the shared [UTC/clock convention](../../time-conventions.md).

## Acceptance and verification

- SeatHold/SeatHoldItem, Booking/BookingItem, and Payment match the plan's fields; statuses are private-set and initial states fixed.
- Hold ownership/expiry, immutable item snapshots, calculated booking totals, cancellation deadlines, and payment result guards are implemented and unit-tested.
- Transition matrices, including rejected changes and payment replays, are tested. No database/concurrency or API guarantee follows from those tests.
- Refund, OutboxMessage, and ProcessedMessage entities are deliberately deferred to phases 7 and 6 respectively. Refunded enum values are reserved with no public transition yet.

Run `dotnet test tests/TicketBooking.Domain.Tests/TicketBooking.Domain.Tests.csproj --configuration Release`. Tests use a controllable TimeProvider instead of wall time or sleeps. No database, Redis, provider, or secrets are needed for these tests. API [local startup](../../../README.md#local-startup) is unchanged.

## Navigation

- [Business context and rules](business-context.md)
- [End-to-end flow and transitions](end-to-end-flow.md)
- [Entry points](entry-points.json)
- [Application responsibilities](per-app-responsibility.md)
- [Frontend structure](frontend-structure.md)
- [Backend structure](backend-structure.md)
- [Data model](data-model.md)
- [Integration points](integration-points.md)
- [Known gotchas](known-gotchas.md)
