# Resource ownership

Status: **implemented** for reusable Domain/Application checks and the HTTP caller adapter; **implemented** for customer hold creation/GET/DELETE wiring; **planned** for booking/payment endpoints and refund-specific traversal. Working tree inspected. The customer hold routes now apply these checks; booking/payment/refund routes remain absent.

## Authority and owner resolution

| Resource | Authoritative owner | Check |
| --- | --- | --- |
| SeatHold | Persisted SeatHold.UserId | EnsureHoldOwner(hold). |
| Booking | Persisted Booking.UserId, copied from the owner's validated hold | EnsureBookingOwner(booking). |
| Payment | Persisted Payment.BookingId → matching Booking.Id → Booking.UserId | EnsurePaymentOwner(payment, booking) checks the link as well as the owner. |
| Refund (planned) | Owning booking, resolved through the refund's persisted booking/payment reference when its model exists | Validate every parent link, then reuse EnsureBookingOwner or EnsurePaymentOwner. No Refund entity/check adapter exists yet. |

Never authorize from a submitted UserId, owner ID, booking number, provider reference or idempotency key. Load resources/parents from persistence using route IDs. Query/body ownership fields do not establish identity. List queries must filter to the caller's stored ownership before pagination; this helper does not filter queries or load data.

[ICurrentUser](../../../src/TicketBooking.Application/Identity/ICurrentUser.cs) exposes only the caller ID. [HttpCurrentUser](../../../src/TicketBooking.Api/Auth/HttpCurrentUser.cs) accepts exactly one authenticated identity with exactly one nonempty GUID `sub` in canonical D format. The JWT issuer emits this format and inbound claim mapping is disabled. Missing HTTP context, anonymous/ambiguous identities and missing/malformed/empty/duplicate subjects provide no caller ID; NameIdentifier is not a fallback. Only authentication middleware may establish the principal.

## Handler contract

Customer handlers must apply the Customer policy, load server resources, invoke the scoped [ResourceOwnershipService](../../../src/TicketBooking.Application/Identity/ResourceOwnershipService.cs), then apply lifecycle/deadline rules before any mutation, save, provider call or resource DTO return:

```csharp
// Inside a Customer-protected handler, after loading payment and its actual booking:
ownership.EnsurePaymentOwner(payment, booking);
// Continue with state/deadline/idempotency checks and the existing transaction rules.
```

EnsureOwner(ownerId) is the extension point for future resources with an already verified server-resolved owner; never pass a request's asserted owner. Hold/booking transitions also reuse [ResourceOwnership.EnsureOwner](../../../src/TicketBooking.Domain/Identity/ResourceOwnership.cs) as a domain safeguard. Domain still accepts explicit actor IDs; HTTP callers must obtain them from ICurrentUser. Payment provider-result methods and expiration jobs remain system operations, not customer authorization entry points.

Role permission and resource ownership are cumulative. Admin satisfies Customer role eligibility but has **no implicit ownership bypass**. A future management operation needs its own explicitly authorized workflow; do not pass the owner's ID as the Admin's identity. Ownership success does not override cancellation deadlines, payment deadlines, hold expiry or state transitions.

## Failures and transactions

- Missing/unusable caller: AuthenticationRequiredException → HTTP 401 `unauthorized`. Protected routes challenge anonymous/invalid bearer credentials before their handler executes.
- Another owner or mismatched payment/booking link: DomainRuleException(ResourceNotOwned) → HTTP 403 `resource_not_owned`, with no owner identifiers in its public detail. This preserves the existing error contract; it does not promise existence-hiding 404 behavior.
- Missing resource: the loader must return 404 `not_found`; the check accepts loaded resources and does not decide lookup outcomes. Null arguments are programming errors.
- Checks are read-only and perform no I/O. They create no transaction, retry or lock. Coordinate server loading, business rules and ownership-changing writes in the operation's transaction; retain SessionSeat version checks. No owner reassignment workflow currently exists.

No EF model, migration, persisted owner column or account FK changes are needed. Refund eligibility, refund state transitions and refund/provider authentication remain future work; ownership alone cannot authorize a provider callback or process a refund.

## Verification

[Application tests](../../../tests/TicketBooking.Application.Tests/ResourceOwnershipServiceTests.cs) exercise owner/other/missing callers across all three implemented resource adapters, mismatched payment parents, unchanged state on rejection and lifecycle rules after authorization. [Domain tests](../../../tests/TicketBooking.Domain.Tests/ResourceOwnershipTests.cs) cover ID equality and empty IDs. [HTTP caller tests](../../../tests/TicketBooking.IntegrationTests/HttpCurrentUserTests.cs) cover claim/query confusion and ambiguous subjects. [JWT HTTP tests](../../../tests/TicketBooking.IntegrationTests/JwtBearerValidationTests.cs) exercise the registered services with signed tokens, customer/Admin ownership, anonymous challenges and safe 401/403 codes on a test-only route. PostgreSQL HTTP tests in AuthEndpointsTests.HoldOwnership now verify owner-only hold history/cancellation and Customer/Admin denial without mutation. Refund end-to-end authorization remains absent.

Verification run: all 421 tests passed with disposable PostgreSQL enabled, with no skipped tests. Formatting passed for this task's changed code files; all nine feature bundles passed JSON/code-anchor/local-link validation.
