# Logout and refresh-family revocation

Status: **implemented**. POST /api/auth/logout revokes one refresh family. POST /api/auth/refresh rejects consumed-token reuse with 401 invalid_refresh_token and revokes the affected family. This does not immediately revoke access JWTs or sign out every device.

## HTTP contract and authority

Send application/json over HTTPS:

```json
{"refreshToken":"<the exact refresh secret from this login family>"}
```

The route is anonymous: possession of the secret authorizes revoking its family. It works without a live access JWT, including when the supplied Authorization header contains an expired one. Do not supply an account/family ID as authority; LogoutRequest contains only refreshToken. Any retained generation works, including a consumed ancestor whose descendants have already rotated.

| Input / state | Response and effect |
| --- | --- |
| Known live family | Commit RevokedAt, then return 204 with an empty body. |
| Consumed ancestor | Revoke that same family's current descendants; return 204. |
| Already revoked or expired family | Return 204; preserve the first RevokedAt, setting it only when absent. |
| Unknown well-formed secret | Return 204 without creating records or touching another family. |
| Missing/malformed/noncanonical secret | Return 400 validation_failed with refreshToken field errors. |
| Database failure before commit | Return safe 500; transaction rollback leaves no partial revocation. |

Responses use Cache-Control:no-store and Pragma:no-cache. Successful repeated logout is idempotent and does not reveal whether the credential was known. Clients discard both locally held token types after logout and may retry logout after response loss. This differs from strict refresh rotation: retrying a consumed token on /refresh is replay and revokes the family.

## Persistence and concurrency

[AuthEndpoints.LogoutAsync](../../../src/TicketBooking.Api/Auth/AuthEndpoints.cs) validates the request and calls [RefreshTokenService.RevokeAsync](../../../src/TicketBooking.Infrastructure/Identity/RefreshTokenService.cs). The service hashes the credential, locates its family, begins a transaction and takes the same SELECT FOR UPDATE family-row lock used by rotation and replay detection. It samples injected UTC time after acquiring the lock and updates only an unset RevokedAt.

Rotation, explicit logout and replay use the same LockSessionAsync/RevokeFamilyAsync helpers. Logout racing rotation either prevents rotation or revokes its replacement after rotation commits. Ancestor logout cannot be bypassed by a descendant changing generations. No response indicating success is returned before commit; the first revocation time is not overwritten by subsequent requests.

Revocation preserves consumed hashes and unused generations as replay/history evidence. The existing RefreshSessions/RefreshTokens schema already contains the necessary state, so no new migration is needed. Other login families and account security stamps are unchanged.

## Access-token and client limits

After logout, refresh of any generation in this family returns 401 invalid_refresh_token. A previously issued access JWT can still authorize requests until its own exp; bearer validation does not consult family revocation or a token denylist. Clients must discard it, and immediate server-side access-token revocation remains separate work. Session-list/all-device administration and history cleanup are also deferred.

The non-disclosing success behavior follows the reasoning of [RFC 7009 section 2.2](https://www.rfc-editor.org/rfc/rfc7009.html#section-2.2). This repository's endpoint uses its own JSON/204 contract, not the OAuth form/200 protocol.

## Verification

[Logout HTTP tests](../../../tests/TicketBooking.IntegrationTests/AuthEndpointsTests.Logout.cs) run against real Kestrel/PostgreSQL and cover family isolation, consumed ancestors, repeat/unknown/expired/replayed credentials, expired access headers, parallel logouts, rotation races, malformed input and forced failure before commit. Existing refresh tests verify consumed-token reuse and ancestor-replay races. The full suite passed 343 tests; development data was not changed.
