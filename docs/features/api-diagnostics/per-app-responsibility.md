# Application responsibilities

- **TicketBooking.Api** is the deployable host. It owns error service registration, exception/status classification, safe public payloads, unexpected-error logging, health/OpenAPI and HTTPS behavior.
- **TicketBooking.Contracts** is a shared library owning ErrorCodes wire strings. Consumers should treat these strings as stable identifiers.
- **TicketBooking.Domain** owns DomainError and DomainRuleException, emitted by reservation/booking/payment guards. It has no HTTP or Contracts dependency.
- **TicketBooking.Infrastructure** owns the allocation conflict marker and EF exception context, not HTTP statuses.
- **TicketBooking.Application** has no implemented business handlers or transaction orchestration yet.

These are libraries within one modular monolith, not separate services. PostgreSQL is not part of the liveness check. No frontend or separate worker application is implemented. Human ownership is unspecified.
