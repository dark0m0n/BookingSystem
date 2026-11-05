# Customer/Admin roles and policies

Status: **implemented** for role definitions, transactional public Customer registration and reusable named API policies. Production booking/catalogue management endpoints and resource-ownership authorization remain separate work. Working tree inspected.

## Roles and policy decisions

[RoleNames](../../../src/TicketBooking.Domain/Identity/RoleNames.cs) defines Customer and Admin without framework dependencies. [AuthorizationPolicies](../../../src/TicketBooking.Api/Auth/AuthorizationPolicies.cs) names the API policies configured by [AuthenticationConfiguration](../../../src/TicketBooking.Api/Auth/AuthenticationConfiguration.cs).

| Policy | Required identity | Permitted role membership |
| --- | --- | --- |
| Customer | Authenticated | Customer or Admin. |
| Admin | Authenticated | Admin only. |

An authenticated roleless user passes neither policy. Missing/invalid bearer authentication returns 401 unauthorized; insufficient membership returns 403 forbidden. Role values are case-sensitive; the data migration canonicalizes matching normalized definitions. Authorization reads validated server-issued JWT role claims; request fields, display names and UI visibility do not grant permissions.

Future endpoints should explicitly apply the appropriate policy:

```csharp
endpoint.RequireAuthorization(AuthorizationPolicies.Customer);
managementEndpoint.RequireAuthorization(AuthorizationPolicies.Admin);
```

No production business endpoints are invented for this step. Real HTTP fixture routes verify both policies with login/refresh tokens. Role eligibility does not replace the reusable [resource ownership checks](resource-ownership.md) or transaction rules. Admin has no implicit ownership bypass.

## Role initialization and compatibility

[SeedCustomerAndAdminRoles](../../../src/TicketBooking.Infrastructure/Persistence/Migrations/20261105211207_SeedCustomerAndAdminRoles.cs) is a data-only migration applied by the existing [local startup command](../../../README.md#local-startup). It upserts Customer/CUSTOMER and Admin/ADMIN using the unique normalized-name index. Existing role IDs and UserRoles memberships are preserved; names and concurrency stamps become canonical. Reapplication is idempotent.

The migration inserts no users or memberships and creates no admin credentials. Its Down intentionally retains role data because definitions can predate this migration or have acquired grants. Existing roleless users are not automatically backfilled; any such assignment requires a trusted decision. The API does not connect to the database or create roles during startup merely to initialize these definitions.

## Public registration and privileged provisioning

RegisterRequest still contains only email, password and displayName. Public registration creates a new user and calls UserManager.AddToRoleAsync with the fixed RoleNames.Customer value in the **same database transaction**. A failure assigning membership rolls back the new user; no partially registered roleless account remains. No success is returned before commit.

Extra role, roles, isAdmin or permissions JSON fields are ignored and cannot produce Admin membership. The client cannot choose an existing account ID either. Applying the role data migration is a prerequisite; missing role data is a server configuration failure, not permission to create a fallback role or grant Admin.

Admin assignment is an out-of-band, trusted server operation using Identity's managers, for example AddToRoleAsync(existingUser, RoleNames.Admin), with its IdentityResult checked. The explicit [Development admin initializer](development-admin.md) creates a new account only in Development; it refuses to promote an existing non-admin account and does not reset an existing admin. No public promotion/role-management endpoint or automatic admin seed is implemented. Direct database editing should not replace normal Identity operations.

Login and refresh read current persisted memberships when minting claims. A trusted grant/removal changes newly issued tokens; existing access JWTs retain their prior role snapshot until expiry. Admin-only membership can use Customer policy operations without a separate Customer membership, but a Customer alone cannot use Admin operations.

## Verification

[AuthorizationPolicyTests](../../../tests/TicketBooking.IntegrationTests/AuthorizationPolicyTests.cs) checks authentication/role combinations without PostgreSQL. [Role HTTP tests](../../../tests/TicketBooking.IntegrationTests/AuthEndpointsTests.Roles.cs) verify seeded definitions with zero users/grants, idempotency, preservation of legacy IDs/memberships, rejected public admin overposting, rollback after membership-write failure, anonymous challenges and new-token behavior after trusted role changes. The latest full PostgreSQL/HTTP/CLI-backed suite passed 424 tests; only disposable data was migrated.

Framework policy semantics follow [ASP.NET Core role authorization](https://learn.microsoft.com/en-us/aspnet/core/security/authorization/roles?view=aspnetcore-10.0). Business ownership, production admin provisioning, public role administration and immediate access-token revocation are outside this implementation.
