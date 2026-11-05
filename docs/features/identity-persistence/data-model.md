# Data model

The shared IdentityDbContext retains seven framework account/role tables alongside eleven business tables. [AddAspNetCoreIdentity](../../../src/TicketBooking.Infrastructure/Persistence/Migrations/20261105162749_AddAspNetCoreIdentity.cs) creates Identity tables; [AddRegistrationProfileAndUniqueEmail](../../../src/TicketBooking.Infrastructure/Persistence/Migrations/20261105163800_AddRegistrationProfileAndUniqueEmail.cs) adds DisplayName and replaces EmailIndex with a unique index. Only disposable test schemas/database were migrated; development/deployment state is not verified.

| Table | Keys and relevant data |
| --- | --- |
| AspNetUsers | GUID Id; nullable UserName/NormalizedUserName and Email/NormalizedEmail (varchar256); required DisplayName varchar200; nullable PasswordHash/SecurityStamp/ConcurrencyStamp; confirmation, phone, 2FA, LockoutEnd (timestamptz), lockout/access-failure fields. |
| AspNetRoles | GUID Id; nullable Name/NormalizedName (varchar256), ConcurrencyStamp. |
| AspNetUserRoles | Composite PK UserId/RoleId; required user/role FKs. |
| AspNetUserClaims / AspNetRoleClaims | Generated integer Id, required user/role FK, optional claim type/value. |
| AspNetUserLogins | Composite PK LoginProvider/ProviderKey, required UserId, optional display name. |
| AspNetUserTokens | Composite PK UserId/LoginProvider/Name, required UserId, optional token value. |

UserNameIndex, EmailIndex and RoleNameIndex are unique on normalized values. Multiple null emails remain permitted at SQL level; public registration requires a valid email. Normalization requires manager calls; raw SQL can bypass it. User/role ConcurrencyStamp properties are EF tokens. Six Identity dependent FKs cascade; thirteen business FKs restrict. Hold/booking UserId still has no account FK/backfill.

Existing rows receive an empty DisplayName when the column is added; new public registrations require a nonblank value. Existing duplicate nonnull normalized emails make the unique-index migration fail; it does not silently merge/delete accounts. Resolve such conflicts through a deliberate account-data decision before applying it to that database.

Contracts contain only submitted credentials or safe user/token response fields. JWTs carry sub/email/jti/iat/nbf/exp and stored role claims, not password hashes/security stamps. Tokens are signed, not encrypted; there is no access-token database row. Refresh session/history entities are described below. Deletion/retention/anonymization policy remains undefined.

## Refresh families and token history

[AddRefreshTokenRotation](../../../src/TicketBooking.Infrastructure/Persistence/Migrations/20261105165909_AddRefreshTokenRotation.cs) adds two tables without modifying business tables:

| Table | Fields and constraints |
| --- | --- |
| RefreshSessions | GUID Id; required UserId → AspNetUsers (Cascade); SecurityStampHash varchar(64), required/C collation; CreatedAt/ExpiresAt timestamptz; nullable RevokedAt; indexes on UserId/ExpiresAt. |
| RefreshTokens | GUID Id; required SessionId → RefreshSessions (Cascade); unique TokenHash varchar(64), required/C collation; CreatedAt timestamptz and nullable ConsumedAt. |

IX_RefreshTokens_UnconsumedSession uniquely indexes SessionId WHERE ConsumedAt IS NULL. This prevents two unconsumed generations in one family; it does not mean a revoked/expired family's unused token is valid. A normal SessionId index supports history/deletion. Old consumed hashes remain for ancestor replay detection; no plaintext token, replacement secret or access JWT is stored.

Twenty entity types now share the context: eleven business, seven framework Identity and two refresh types. Business FKs remain thirteen Restrict relationships; Identity/refresh add eight Cascade relationships. User deletion cascades refresh history but does not immediately invalidate issued access JWTs.

Refresh expiry is fixed at family creation and normalized to PostgreSQL microsecond precision before the login response, preventing a round-trip deadline shift. Rotation does not update it. Runtime decisions compare a full UTC TimeProvider reading sampled after locking. There is no cleanup worker; retained consumed hashes must not be removed from live families because that would remove replay evidence. Retention after expiry is a future policy.

## Logout/replay revocation

Logout and replay share the existing RefreshSessions.RevokedAt field and family lock. The first revocation time is preserved; no history is deleted and no new migration is needed. A revoked family can retain an unconsumed token row, but that row cannot refresh. Unknown well-formed logout does not create a family or token. Access JWTs have no persisted revocation row and remain valid until their own exp.

## Customer/Admin definitions and memberships

[SeedCustomerAndAdminRoles](../../../src/TicketBooking.Infrastructure/Persistence/Migrations/20261105211207_SeedCustomerAndAdminRoles.cs) is a data-only migration: Customer/CUSTOMER and Admin/ADMIN are upserted into AspNetRoles. Canonical names/concurrency stamps are updated while existing IDs and UserRoles links are retained. Newly inserted roles receive fixed migration IDs. No user or membership is seeded; table/entity counts remain unchanged. Down retains definitions because they can predate the migration or acquire memberships.

Public registration inserts one UserRoles membership for Customer within the same transaction as its new user. Existing roleless accounts are not backfilled automatically. Admin membership is only changed by trusted server operations; new login/refresh JWTs reflect stored changes, while existing access tokens retain their role snapshot until expiry.

The explicit Development admin command uses these existing Users/UserRoles tables and the migrated Admin definition. UserManager hashes the externally supplied password; user and Admin-only membership commit in one transaction. It creates no token family, schema object or default account during normal startup. Existing account data remains unchanged on rerun/conflict; see [development initialization](development-admin.md).

Ownership checks reuse existing SeatHold.UserId and Booking.UserId. Payment ownership follows its existing BookingId FK to Booking.UserId and verifies the supplied parent ID. ICurrentUser is request-scoped state, not an EF entity. No schema/migration/account-FK change is made; refund-specific storage is still deferred. See [ownership resolution](resource-ownership.md).
