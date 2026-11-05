# Business context and rules

The plan requires Identity accounts and JWT authentication. This feature supplies registration, password login and persisted refresh rotation; logout/family revocation is also implemented; Customer/Admin role policies are also implemented; reusable resource ownership checks are implemented; business endpoints remain separate work.

| Rule | Enforcement / evidence |
| --- | --- |
| BR-01: GUID account IDs match business reference types. | ApplicationUser and EF Identity context. Business UserId FKs remain absent. |
| BR-02: Identity validates/hashes passwords; plaintext passwords are never persisted or returned. | UserManager.CreateAsync; SignInManager.CheckPasswordSignInAsync; HTTP/storage tests. Identity password defaults remain unchanged. |
| BR-03: Email is the registration username and login identifier. | AuthEndpoints trims email; framework normalizes names/emails; stored spelling is preserved. Passwords are never trimmed. |
| BR-04: Duplicate normalized emails/usernames cannot create another account. | Identity validation plus unique EmailIndex/UserNameIndex; races return the same safe 409. |
| BR-05: Validate required fields before account operations. | Valid email ≤256 characters, nonempty password ≤128, trimmed nonblank display name ≤200; Identity additionally enforces password/username rules. |
| BR-06: Invalid credentials do not disclose whether login failed for a missing, locked or two-factor account. | Generic 401 invalid_credentials. Response parity is tested; constant timing is not guaranteed. |
| BR-07: Failed passwords count toward Identity lockout. | CheckPasswordSignInAsync with lockoutOnFailure:true; default five failures/five-minute lockout. |
| BR-08: JWTs have a captured expiry and server-controlled claims. | JwtTokenIssuer and persisted roles; TimeProvider controls issuance/validation, exact expiry rejects with no grace. |
| BR-09: User updates use optimistic concurrency. | Identity ConcurrencyStamp and manager IdentityResult handling; existing tests. |
| BR-10: Keep credentials/framework models outside Domain. | Infrastructure user/stores; API endpoint/middleware code; Contracts contains safe DTOs. |
| BR-11: Require the configured issuer/audience, HS256 signature and expiration. | Bearer options require exact audience matching, including trailing slash. Database-free HTTP tests cover missing/wrong claims, tampering, unsigned tokens and disallowed algorithms. |
| BR-12: Store only hashes of random refresh tokens. | RandomNumberGenerator produces 32-byte secrets; SHA-256 hex hashes alone are persisted. |
| BR-13: Rotate once in one transaction; replay revokes the family. | RefreshTokenService locks the stable family row, rereads the token and commits consumption/replacement or family revocation. SQL allows one unconsumed token per family. |
| BR-14: Refresh expiry is captured at login and never extended. | Default seven-day policy, configurable 1–90 days; UTC expiry is normalized to PostgreSQL microseconds before returning it. Eligibility uses the full clock reading after the row lock. |
| BR-15: Refresh must revalidate the account. | Security-stamp hash, lockout, two-factor and configured confirmation checks; current profile/roles supply new JWT claims. |
| BR-16: Logout is family-scoped and idempotent. | RevokeAsync locks the same family as rotation/replay, accepts any retained generation, preserves the first RevokedAt and commits before 204. Unknown well-formed credentials disclose no state. |
| BR-17: Public registration grants only Customer and must be atomic. | The API uses RoleNames.Customer, never request roles; failed membership saves roll back the new user. |
| BR-18: Define named Customer/Admin policies. | Authenticated Customer or Admin passes Customer; only authenticated Admin passes Admin. Roleless or unrelated-role accounts do not pass either. |
| BR-19: Seed role definitions without granting admin membership. | SeedCustomerAndAdminRoles upserts normalized names, preserves existing IDs/memberships and creates no users or UserRoles. |

A data migration ensures canonical Customer/Admin role definitions without creating users. Registration grants only Customer in the account-creation transaction, does not auto-login and does not send email. Login requires no email confirmation under current Identity defaults. Two-factor accounts cannot obtain a token through password-only login; completion is deferred. Role names in request JSON grant no privileges. Refresh tokens are single-use and family replay revokes renewal rights. Logout revokes the presented token's family without changing other login families. No public account deletion/recovery or PII retention policy is defined here.

## Explicit development provisioning

BR-20: Only an explicitly invoked Development-only command may initialize a new local admin from external credentials. It must use Identity managers, commit user/membership atomically, preserve an existing admin and reject existing non-admin accounts. Enforced by DevelopmentAdminCommand and covered by command/PostgreSQL tests; see [the operator contract](development-admin.md). Normal startup and public registration never invoke it.

## Resource ownership

BR-21: Customer access requires both role eligibility and ownership; Admin has no implicit ownership bypass. ResourceOwnershipService uses ICurrentUser from authenticated HTTP claims, never request ownership fields. BR-22: Holds/bookings use persisted UserId; payments require a matching BookingId link and the booking owner. Missing caller is 401 unauthorized; another owner or broken link is 403 resource_not_owned. These checks are read-only and do not replace lifecycle/deadline rules. Application/HTTP tests cover these rules; refund-specific traversal remains planned. See [resource ownership](resource-ownership.md).
