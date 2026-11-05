# Integration points

| Boundary | Contract / authority | Failure behavior |
| --- | --- | --- |
| HTTP client → API | POST /api/auth/register and /api/auth/login; Contracts.Auth DTOs. | 400 validation_failed, 409 account_already_exists, 401 invalid_credentials; no automatic retry. |
| API → Identity | UserManager and SignInManager; PostgreSQL EF stores. | IdentityResult validation/concurrency or safe generic login failure; unexpected provider exceptions propagate. |
| Registration → PostgreSQL | One user insert, unique normalized account indexes. | Concurrent duplicates are safely mapped by constraint name; unrelated constraint failures are not mislabeled. |
| Login/refresh → issuance | Persisted refresh grant plus current user/roles; JwtTokenIssuer signs access tokens. | Login creates a family; refresh rotates under a database transaction. No external provider call. |
| API → RefreshTokenService | RefreshRequest/LogoutRequest secret and cancellation token; database stores its hash. | 400 validation_failed for malformed input; generic 401 invalid_refresh_token for unusable credentials. Consumed-token replay commits family revocation. |
| Bearer middleware → authorization | Validated sub/role claims. | Missing/invalid/expired token gets 401 unauthorized; denied role gets 403 forbidden through status formatting. |
| Business → account references | Existing GUID UserId fields. | Reusable explicit ownership checks are available; business handlers, account FK and backfill remain deferred. |

See [persistence](../local-persistence/FEATURE.md), [error codes](../api-diagnostics/error-codes.md), [domain boundaries](../domain-boundaries/FEATURE.md) and [schema decision](../../decisions/0001-seat-ownership-price-history-and-transitions.md). Contracts and wire codes are stable; changing lifetime/key/issuer/audience requires coordinated configuration/client handling.

No external identity provider, email sender, broker or cookie scheme is integrated. This repository issues its own symmetric access tokens. See Microsoft's [JWT bearer configuration](https://learn.microsoft.com/aspnet/core/security/authentication/configure-jwt-bearer-authentication?view=aspnetcore-10.0) and [Identity model documentation](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/customize-identity-model?view=aspnetcore-10.0) for framework responsibilities.

Replay containment follows the [RFC 9700 refresh-token rotation pattern](https://www.rfc-editor.org/rfc/rfc9700.html#section-4.14.2): preserve relationships between generations and revoke renewal rights on reuse. This custom JSON endpoint does not implement a complete OAuth authorization server or client registration protocol. See the [rotation contract](refresh-token-rotation.md) for concurrency, failure and expiry decisions.

POST /api/auth/logout returns 204 after known-family revocation, including when already revoked/expired or when the well-formed credential is unknown. Its idempotent non-disclosing success follows the revocation reasoning in [RFC 7009 section 2.2](https://www.rfc-editor.org/rfc/rfc7009.html#section-2.2); this repository uses its own JSON/204 contract rather than the OAuth form/200 wire format. No cookies or external sign-out service are invoked.

Named Customer/Admin policies evaluate server-issued JWT role claims; RoleNames defines canonical role strings, and UserManager manages persisted memberships. A data migration ensures definitions, not users/grants. Role provisioning and the public-registration transaction are detailed in [roles-and-policies.md](roles-and-policies.md).

The Development admin command consumes standard API configuration (User Secrets/environment), calls UserManager/RoleManager and shares the existing PostgreSQL transaction. It requires migrated Admin role data and never calls HTTP or JWT issuance. Failures return exit 1 with fixed diagnostics, without automatic retries. See [operator configuration and outcomes](development-admin.md).

Application's ICurrentUser is implemented by the API's HttpCurrentUser; authenticated sub is the sole caller source. ResourceOwnershipService consumes server-loaded Domain entities and shares Domain.Identity.ResourceOwnership with hold/booking transition guards. ApiErrorCatalog maps missing callers to unauthorized and ownership failures to resource_not_owned. No provider call or database lookup occurs inside these checks. Future customer workflows must explicitly invoke them; see [the ownership contract](resource-ownership.md).
