# Application responsibilities

- **TicketBooking.Api** is the deployable HTTP application. It owns diagnostic registration, request error customization, environment gating, and HTTPS middleware. Inputs are HTTP requests and host configuration; outputs are health text, OpenAPI JSON, or error responses. It owns no diagnostic database tables.
- **TicketBooking.Infrastructure** is a referenced library providing the registered DbContext; it does not implement API diagnostics.
- **TicketBooking.Application**, **TicketBooking.Domain**, and **TicketBooking.Contracts** are logical libraries. No diagnostic handlers or domain policies are implemented there.
- PostgreSQL is a separate Compose service, not part of the API's liveness check.

Evidence: [Program.cs](../../../src/TicketBooking.Api/Program.cs) and [API project references](../../../src/TicketBooking.Api/TicketBooking.Api.csproj). No frontend, worker, or separate authentication application exists in the inspected source. Human ownership is not specified.
