# Application responsibilities

- **TicketBooking.Api** is the existing deployable host. AuthEndpoints owns HTTP validation/error mapping, Identity calls and access/refresh response assembly. AuthenticationConfiguration owns validated JWT settings, bearer validation and authorization registration. JwtTokenIssuer signs safe claims using server time/roles.
- **TicketBooking.Infrastructure.Identity** owns GUID ApplicationUser and core user/role registration. Framework managers/stores own credentials, normalization, password policy and concurrency. RefreshTokenService owns refresh-secret generation/hashing, persisted family eligibility and serialized rotation, logout and replay revocation, sharing the same family-row lock.
- **TicketBooking.Infrastructure.Persistence** owns shared IdentityDbContext, account profile/email mapping and migrations. Account and business tables use the same PostgreSQL database.
- **TicketBooking.Contracts.Auth** owns RegisterRequest, LoginRequest, RefreshRequest, LogoutRequest, UserResponse and LoginResponse. No Identity entity or credential hash is returned.
- **TicketBooking.Domain.Identity** owns the framework-independent RoleNames constants and ResourceOwnership equality rule. **Application.Identity** owns ICurrentUser, AuthenticationRequiredException and ResourceOwnershipService for customer ownership checks; authentication endpoints still directly coordinate framework managers.

No separate service, frontend or worker is introduced. The data migration seeds Customer/Admin definitions, public registration grants Customer only, and Admin membership requires a trusted server-side operation. Normal startup creates no admin account; the API owns the explicit Development-only operator command using existing Infrastructure managers/stores. No public role-management endpoint is created; human ownership is unspecified.

The API owns HttpCurrentUser and scoped registration; Application checks server-loaded resources/parent links, and Domain.Identity.ResourceOwnership supplies the framework-independent ID rule. Future resource loaders/handlers own invocation and transaction boundaries. No refund entity or management bypass is introduced.
