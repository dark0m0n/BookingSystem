# Backend structure

- [Program.cs](../../../src/TicketBooking.Api/Program.cs): AddApiErrors, UseExceptionHandler, UseStatusCodePages; health/OpenAPI, HTTPS and required startup configuration remain here.
- [ApiProblemDetails.cs](../../../src/TicketBooking.Api/Errors/ApiProblemDetails.cs): DI registration, code/path/trace enrichment, status formatting and JSON fallback.
- [ApiExceptionHandler.cs](../../../src/TicketBooking.Api/Errors/ApiExceptionHandler.cs): catches request exceptions, logs unexpected failures, writes safe responses.
- [ApiErrorCatalog.cs](../../../src/TicketBooking.Api/Errors/ApiErrorCatalog.cs): explicit DomainError/status mappings; allocation marker and EF SessionSeat-only conflicts map to seat availability, other EF conflicts to concurrency_conflict.
- [ErrorCodes.cs](../../../src/TicketBooking.Contracts/Errors/ErrorCodes.cs): public wire constants; Domain remains independent of Contracts and HTTP.
- [DomainRuleException.cs](../../../src/TicketBooking.Domain/DomainRuleException.cs): typed business failures used by SeatHold, Booking and Payment.
- [SeatAllocationConflictException.cs](../../../src/TicketBooking.Infrastructure/Persistence/SeatAllocationConflictException.cs): DbUpdateConcurrencyException subclass preserving the allocation writer's public base exception contract.

[ApiErrorContractTests.cs](../../../tests/TicketBooking.IntegrationTests/ApiErrorContractTests.cs) starts real Kestrel with the error pipeline; tests codes, safe payloads, media fallback, HTTP statuses and payment replay. [DomainErrorTests.cs](../../../tests/TicketBooking.Domain.Tests/DomainErrorTests.cs) covers expired holds and conflicting payment results. The focused run passed 150 tests including all domain tests.

No business controllers, authentication, error-specific database settings, or workers are added. Error middleware does not own transaction rollback. The API still requires ConnectionStrings:DefaultConnection and Jwt:SigningKey at startup; launch settings retain ports 7281/5222.
