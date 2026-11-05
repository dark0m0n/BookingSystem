# End-to-end flow

Startup registers Npgsql, Identity stores plus SignInManager, JWT options/issuer, bearer authentication and authorization. Program wraps requests with safe error middleware, redirects HTTP, invokes authentication/authorization and maps the four anonymous auth routes. Named Customer/Admin policies are available for protected endpoints.

## Registration

1. Customer posts RegisterRequest to `auth-register`; cache prevention headers are set (BR-05).
2. Handler validates email/password/displayName, trims email/name and constructs ApplicationUser with username=email (BR-01/03).
3. Begin a shared-context transaction. UserManager validates, normalizes, hashes and saves (BR-02/04). IdentityResult failures become field validation errors or account_already_exists; narrowly recognized account index violations use the same conflict after a concurrent race.
4. Add the new user only to the seeded Customer role through UserManager, then commit both user and membership (BR-17). Any membership failure rolls back account creation. Return 201 UserResponse(Id, Email, DisplayName). No admin grant, cookie, token or confirmation email is created.

## Login

1. Customer posts LoginRequest to `auth-login`; validation precedes credential checks.
2. Resolve normalized email. Unknown users and two-factor-enabled accounts fail generically. SignInManager checks password, sign-in eligibility and lockout while counting failures (BR-06/07).
3. On success, create a refresh family and its first hashed token in one EF save, load stored roles and issue an HS256 JWT with sub, email, jti, iat, nbf and exp; return LoginResponse with both token types and expirations (BR-08/12/14). Response has Cache-Control:no-store and Pragma:no-cache.
4. A caller can send Authorization:Bearer on a future protected route. Middleware validates signature/issuer/audience and now < exp, mapping sub/role claims without inbound claim renaming. No production business route is protected by this task because those routes do not exist yet.

Failures are ProblemDetails with code/instance/traceId. Registration validation never inserts the account; a duplicate cannot update the existing profile/password. Login returns the same public failure for unknown/wrong/locked/unsupported-two-factor credentials. Sequential repeat logins issue distinct jti values without extending any existing token.

Each manager save uses the scoped shared context; registration now wraps account creation and Customer membership in an explicit transaction. Refresh uses an explicit family-lock transaction; see the flow below and [entry points](entry-points.json). Logout uses the same family-lock transaction; MFA completion, callback handoff and notifications remain absent.

## Refresh

1. Client posts RefreshRequest to auth-refresh. An access JWT is not required; an expired JWT header does not block this anonymous route. Validate canonical token shape before lookup.
2. Hash the supplied secret and locate its token/family. Unknown hashes fail with generic 401 invalid_refresh_token and do not revoke unrelated families.
3. Begin a transaction, lock RefreshSessions with SELECT FOR UPDATE, then sample UTC time and reread the token/account. Reject revoked/expired families; detect consumed ancestors or changed account eligibility.
4. On replay/ineligibility, persist RevokedAt on that family and commit before returning 401. On valid use, consume the old token and insert one new hash atomically. A failed insert rolls back consumption.
5. Commit before returning any replacement secret, then issue the access JWT from current server user/roles. Keep the original refresh expiry and return the token pair with no-store headers.

Racing generations serialize on the same family row. Concurrent reuse can yield one 200 followed by revocation of that winner's family; callers must serialize refreshes and sign in after replay or ambiguous response loss. Detailed boundaries and limitations: [refresh-token-rotation.md](refresh-token-rotation.md).

## Logout and revocation

1. Client posts LogoutRequest to auth-logout with the exact refresh secret. No live access JWT or user/family ID is required; possession of any generation authorizes revoking that family (BR-16).
2. Validate canonical shape. Malformed input returns 400 validation_failed; unknown well-formed hashes return 204 without touching another family.
3. For a known token, begin a transaction and use LockSessionAsync, shared with rotation. If not already revoked, sample UTC after the lock and set RevokedAt once, including for an expired family. Commit before returning 204.
4. Subsequent refresh of any family generation returns 401 invalid_refresh_token. A logout racing rotation either prevents it or revokes its newly created successor. Other login families remain usable.
5. Repeated logout is safe after an ambiguous response; it preserves the first timestamp. A failed commit rolls back revocation. Clients discard both locally stored tokens after logout; existing access JWTs expire naturally. See [logout contract](logout-and-revocation.md).

## Role authorization

The role data migration runs through the existing startup migration command. It ensures the two normalized definitions, preserving pre-existing role IDs/memberships; it does not assign users. A named Customer policy permits authenticated Customer or Admin claims; Admin permits only authenticated Admin. Missing authentication produces 401 and insufficient roles 403. JWT claims come from persisted membership at login/refresh. Protected customer/admin fixture routes verify this flow; production catalogue/booking management routes remain future work. See [roles-and-policies.md](roles-and-policies.md).

## Development admin command

The operator supplies external credentials and invokes `--initialize-admin` (entry `development-admin-command`, BR-20). Program removes the flag before building configuration. The command first requires Development, validates configuration and the migrated Admin role, then starts a shared-context transaction. Existing admins return unchanged success; existing non-admin/username conflicts fail. Otherwise UserManager creates the account and Admin membership and commits, then the process exits without HTTP. Failure rolls back and returns exit 1 with a fixed message. See [startup and rerun details](development-admin.md).

## Resource authorization building block

For future customer routes, authenticate and apply Customer policy, load the resource and parent booking from storage, then invoke ResourceOwnershipService (entries current-user, resource-ownership-service; BR-21/22) before DTO return or side effects. The HTTP adapter extracts the validated GUID sub; no caller means 401. Another owner or a mismatched payment parent means 403 resource_not_owned. On success, continue with lifecycle/deadline and transaction rules. The helper itself never mutates resources. Only test routes exercise this HTTP flow so far; business routes and refunds remain future work. See [the handler contract](resource-ownership.md).
