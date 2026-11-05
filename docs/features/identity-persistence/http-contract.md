# Registration and login HTTP contract

Both endpoints accept application/json, are anonymous and return safe JSON. Registration does not automatically log the user in. Use HTTPS; the configured local API is https://localhost:7281.

## Register

`POST /api/auth/register`

```json
{"email":"alice@example.test","password":"Example-Password1!","displayName":"Alice"}
```

Success: 201, with only `id` (GUID), `email` and `displayName`. Email/displayName are trimmed; password is preserved verbatim. Only Customer membership is assigned; no role can be chosen by the client. Unknown role/roles/isAdmin/permissions fields cannot grant Admin. User creation and Customer membership commit together. Display name must be nonblank and ≤200 characters; email must be valid and ≤256; password is required and ≤128, and must satisfy Identity's default strength rules (at least six characters with uppercase, lowercase, digit and non-alphanumeric character).

Validation: 400 application/problem+json with code validation_failed and errors keyed by email/password/displayName. Duplicate normalized email or username, including a concurrent insert: 409 account_already_exists. A duplicate never changes the original account/password/profile.

## Login

`POST /api/auth/login`

```json
{"email":"alice@example.test","password":"Example-Password1!"}
```

Success: 200 with `accessToken`, `tokenType` (Bearer), `expiresAt` (UTC), `refreshToken`, `refreshTokenExpiresAt` (UTC), and `user` containing id/email/displayName. Cache-Control:no-store and Pragma:no-cache prevent caching. Wrong or unknown credentials, lockout and unsupported two-factor accounts: 401 invalid_credentials with generic detail. Invalid request fields: 400 validation_failed.

Send `Authorization: Bearer <accessToken>` to routes requiring authentication. JWT validation requires an HS256 signature, exact configured issuer/audience (including case and audience trailing slash) and exp. Eligibility is nbf ≤ now < exp when nbf exists; there is no time grace. Sub is the account GUID; role claims come only from stored assignments. Invalid/missing bearer credentials on a protected route use code unauthorized; insufficient roles use forbidden. See the [validation contract](jwt-validation.md). The production app has no protected business endpoints yet.

## Local configuration and examples

Jwt:Issuer and Jwt:Audience default to TicketBooking and TicketBooking.Api. Jwt:AccessTokenMinutes defaults to 15 and must be 1–1440. Jwt:RefreshTokenDays defaults to 7 and must be 1–90; it applies when a new login family is created. Set Jwt:SigningKey through API User Secrets to at least 32 random UTF-8 bytes, then use the existing [startup command](../../../README.md#local-startup) to apply both Identity migrations and start the API.

```bash
curl https://localhost:7281/api/auth/register \
  -H 'Content-Type: application/json' \
  -d '{"email":"alice@example.test","password":"Example-Password1!","displayName":"Alice"}'
curl https://localhost:7281/api/auth/login \
  -H 'Content-Type: application/json' \
  -d '{"email":"alice@example.test","password":"Example-Password1!"}'
```

The example credentials are illustrative local test data. Login now returns a refresh token and /api/auth/refresh rotates it; logout now revokes its family through POST /api/auth/logout. No cookie, email message or MFA response is produced. [Known gotchas](known-gotchas.md) records revocation and migration limits.

## Refresh

`POST /api/auth/refresh`

```json
{"refreshToken":"<the exact refreshToken returned by login or the preceding refresh>"}
```

The placeholder is illustrative; actual tokens are 43 canonical base64url characters encoding 32 random bytes. No access token, user ID or password is required. Do not trim/modify the secret.

Success: 200 with the same LoginResponse shape, a newly signed access JWT and a different refreshToken. The prior refresh token becomes consumed and cannot be retried. refreshTokenExpiresAt stays unchanged. Cache-Control:no-store and Pragma:no-cache apply. Use the newly returned pair for subsequent requests.

Malformed/missing token: 400 validation_failed with refreshToken field errors. Unknown/expired/revoked/consumed tokens and ineligible accounts: generic 401 invalid_refresh_token. Consumed-token replay revokes its whole family; unrelated login families remain usable. Serialize refreshes and reauthenticate after replay or ambiguous response loss. See [rotation and secure storage](refresh-token-rotation.md).

## Logout

`POST /api/auth/logout`

```json
{"refreshToken":"<a current or previously consumed refresh token from this login family>"}
```

No valid access JWT is required. The refresh secret is the authority to revoke its family; request user IDs, family IDs and role fields are not accepted as authority. A consumed ancestor also reaches its current descendants.

Success: 204 with an empty body, Cache-Control:no-store and Pragma:no-cache. The same response applies to already revoked/expired families and unknown well-formed tokens, so logout is safe to retry without revealing token state. Missing/malformed input returns 400 validation_failed with refreshToken errors. Backend failure returns a safe 500; no success is returned before commit.

Discard both local token types after logout. Any subsequent refresh in this family returns 401 invalid_refresh_token; other login families remain valid. Existing access JWTs remain usable until their own expiry. This endpoint does not sign out every device or revoke access JWTs immediately. See [logout and revocation](logout-and-revocation.md).

## Customer/Admin policy use

Registration/login/refresh/logout remain anonymous auth routes. Future customer endpoints should use RequireAuthorization(AuthorizationPolicies.Customer); management endpoints should use RequireAuthorization(AuthorizationPolicies.Admin). Customer permits an authenticated Customer or Admin; Admin permits only an authenticated Admin. Roleless users receive 403, and missing/invalid bearer credentials receive 401. Public registration creates no admin account or membership; see [role provisioning and policy rules](roles-and-policies.md).
