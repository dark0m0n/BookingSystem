# Application responsibilities

- **TicketBooking.Api** binds/validates operator configuration during startup and registers immutable BookingTimingPolicy plus the existing TimeProvider.System. Defaults live in BookingTimingOptions and appsettings.json. No endpoint is added.
- **TicketBooking.Domain** owns the dependency-free BookingTimingPolicy calculations and validation. Reservation captures hold expiry; Booking captures session/payment/cancellation instants and checks them; Payment's factory uses Booking's internal pending/deadline guard.
- **Application** will load authoritative sessions, inject the policy/clock into use cases, coordinate persistence/seat state, and handle late-payment outcomes. The [retention/late-result decision](seat-retention-and-late-payments.md) specifies that pending bookings own allocations through PaymentDeadline and late successful charges require reconciliation/compensation. These handlers do not exist yet.
- **Infrastructure** now persists absolute deadline snapshots through [entity mappings](../local-persistence/FEATURE.md); expiry/provider integration remains future work.

All boundaries remain in the existing assembly/application. No separate timing service, scheduler deployment, Redis/RabbitMQ dependency, provider call, or human owner is introduced. Unit/configuration tests own no production data.
