# Identity accounts and authentication

Status: **partial**. Account storage, registration/login, JWT validation, refresh rotation/logout and Customer/Admin roles/policies and explicit Development admin initialization are implemented. Reusable resource ownership checks and the authenticated caller adapter are implemented; business endpoint wiring, refunds, email confirmation and MFA completion remain future work. Working tree inspected on top of `e00b0be` with the preceding Identity changes.

Customers can register an email/password/display name, log in for an access/refresh pair, rotate a persisted refresh token without submitting a password, and revoke that login family through logout. Identity owns credentials and EF account persistence; the API owns validation, token issuance and authentication middleware. Domain remains independent of ASP.NET Core.

## Acceptance and verification

The latest full PostgreSQL/HTTP/CLI run passed 424 tests with zero skips, including all 121 cases in the registration/login, JWT and role fixtures. Coverage includes failed-login storage side effects, cross-user refresh/logout isolation, expired access tokens, rotation/reuse, role snapshots and existing ownership/admin regressions. Tests use disposable data; no local admin was created in the development database.

- POST /api/auth/logout accepts a refresh token from any generation and commits family revocation before returning idempotent 204; unknown/revoked/expired well-formed tokens also receive 204. Malformed input is 400. Replay on refresh remains 401 invalid_refresh_token and revokes renewal rights. No new migration is needed.

- POST /api/auth/register returns 201 with a safe user DTO; Identity failures become 400 validation_failed or 409 account_already_exists.
- POST /api/auth/login returns 200 with a Bearer token, UTC expiry and user DTO; unknown/wrong credentials, lockout and unsupported two-factor accounts return 401 invalid_credentials.
- Concurrent registrations cannot duplicate normalized emails/usernames. Profile and unique-email migration preserve existing accounts, giving existing rows an empty display name.
- Token signature, exact issuer/audience and strict lifetime are validated; audience trailing slashes are significant. Roles come from persisted server assignments. Registration atomically assigns only Customer; client role/permission fields never grant Admin. See the [JWT validation contract](jwt-validation.md).

Use the existing [local startup command](../../../README.md#local-startup) to apply migrations. Set Jwt:SigningKey to at least 32 random UTF-8 bytes; default access lifetime is 15 minutes and refresh-family lifetime is seven days (Jwt:RefreshTokenDays, 1–90). See [HTTP contract and examples](http-contract.md). Tests need TICKETBOOKING_TEST_CONNECTION_STRING for PostgreSQL; run `dotnet test TicketBooking.slnx --configuration Release`.

## Navigation

- [Business context and rules](business-context.md)
- [End-to-end flow](end-to-end-flow.md)
- [Entry points](entry-points.json)
- [Application responsibilities](per-app-responsibility.md)
- [Frontend structure](frontend-structure.md)
- [Backend structure](backend-structure.md)
- [Data model](data-model.md)
- [Integration points](integration-points.md)
- [Known gotchas](known-gotchas.md)
- [HTTP contract](http-contract.md)
- [JWT validation contract](jwt-validation.md)

- [Refresh-token rotation contract](refresh-token-rotation.md)

- [Logout and revocation contract](logout-and-revocation.md)

- [Customer/Admin roles and policies](roles-and-policies.md)

- [Development admin initialization](development-admin.md)

- [Resource ownership checks and handler contract](resource-ownership.md)

- [Authentication test coverage and commands](testing.md)
