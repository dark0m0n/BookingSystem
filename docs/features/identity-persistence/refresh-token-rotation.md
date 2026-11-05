# Persisted refresh tokens and rotation

Status: **implemented** for login issuance, POST /api/auth/refresh and replay/account-ineligibility revocation. POST /api/auth/logout also revokes the affected family. Session-list/all-device administration, cleanup and immediate access-JWT revocation remain deferred. Working tree inspected.

## Credentials and storage

Login creates an independent refresh family and returns its first token alongside the access JWT. Each refresh secret is 32 bytes from RandomNumberGenerator, encoded as 43 canonical base64url characters. Only its uppercase SHA-256 hash is stored. A fast hash is appropriate for these high-entropy random credentials; passwords continue using Identity's password hasher. The database cannot recover a plaintext replacement for a retry.

RefreshSessions stores the account ID, hashed security-stamp snapshot, creation/absolute expiry and optional revocation time. RefreshTokens stores each generation's ID, family ID, hash, creation and optional consumption time. Hash uniqueness prevents duplicate stored credentials; a filtered unique index permits only one unconsumed generation per family. Required user/family FKs cascade deletion. See [data model](data-model.md).

Plaintext refresh tokens exist only during generation, request handling and response delivery. Never log them, place them in URLs, return their hashes as credentials or enable sensitive EF parameter logging in production. Requests/responses use HTTPS JSON with no-store/no-cache response headers; no cookie or browser storage strategy is introduced.

## Atomic rotation and replay handling

1. Validate the token's canonical shape and look up its hash. Unknown tokens return 401 invalid_refresh_token without changing another family.
2. Begin a database transaction and lock the stable RefreshSessions row with SELECT FOR UPDATE. Every generation, including an old ancestor replay, uses this same lock.
3. Sample UTC TimeProvider **after** acquiring the lock. Reject revoked families or now ≥ ExpiresAt. Reread the token and current account under the lock.
4. If consumed, persist family RevokedAt and commit before returning generic 401. Changed security stamp, lockout, enabled two-factor authentication or unmet configured confirmation requirements also revoke the family. Unknown/deleted accounts cannot refresh.
5. Otherwise read current roles, mark the old token consumed and insert one replacement hash. These writes commit together; failure before commit rolls back consumption. Return a fresh JWT and replacement refresh token only after the database commit.

This implements the [RFC 9700 rotation/replay-containment pattern](https://www.rfc-editor.org/rfc/rfc9700.html#section-4.14.2): an old credential's reuse invalidates renewal rights for its family. It does not implement a complete OAuth authorization server.

Concurrent use is treated as replay: one request may receive 200 before a loser revokes that winner's family. Ancestor replay and descendant rotation also serialize on the family row, preventing a surviving successor from bypassing revocation. Other login families remain valid. Clients must serialize refresh requests across tabs/callers; there is no replay grace, transparent retry or way to identify the legitimate presenter. A lost response after commit requires signing in again.

## Expiry and account rules

Jwt:RefreshTokenDays defaults to seven and accepts 1–90. Capture the family's deadline once at login; rotation/configuration changes never extend it. Normalize this deadline to PostgreSQL microsecond precision before returning it, so persistence cannot shift the cutoff. Runtime eligibility uses full UTC clock precision: one tick before is valid, equality rejects. Access JWT dates retain their separate whole-second precision/lifetime.

An access token is not required to refresh; an expired access JWT does not block the anonymous refresh route. Refresh uses the family account and stored current roles/profile, never client-supplied user IDs/roles. A changed Identity security stamp blocks further renewal. Existing access JWTs remain valid until their own expiry; family revocation/deletion is not immediate access-token revocation, and an access token can outlive refresh-family expiry.

Consumed rows must remain available while the family is live for ancestor replay detection. No cleanup job or post-expiry retention policy exists. Logout now uses that same family-lock/revocation discipline; future session administration must do so too. See [logout contract](logout-and-revocation.md).

## Verification

[Refresh HTTP tests](../../../tests/TicketBooking.IntegrationTests/AuthEndpointsTests.Refresh.cs) verify hash-only storage, exact expiry, expired-access renewal, replay isolation, same-token/ancestor races, rotation through an independent provider, account state changes, fresh roles and rollback after a forced replacement-write failure. The complete PostgreSQL-backed suite passed 325 tests. Migration: [AddRefreshTokenRotation](../../../src/TicketBooking.Infrastructure/Persistence/Migrations/20261105165909_AddRefreshTokenRotation.cs); only disposable test data was migrated. Apply development migrations through the existing [startup command](../../../README.md#local-startup).
