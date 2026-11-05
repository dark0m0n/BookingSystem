# Authentication test coverage

The integration suite uses real HTTP requests through Kestrel, production authentication/authorization extensions, Identity managers and migrated PostgreSQL stores. Each database fixture uses an isolated schema and disposes its connection pool. Test-only protected routes exercise access control; production business endpoints are still deferred.

## Scenario matrix

| Scenario | Evidence and assertions |
| --- | --- |
| Registration | AuthEndpointsTests.RegisterThenLogin_PersistsProfileAndReturnsAUsableJwtWithoutCredentials: 201, trimmed profile, hashed password, no credential fields. Validation/duplicate/concurrent-registration tests reject invalid input and preserve the original account. |
| Login | The same registration/login test verifies 200, signed token claims, expiry, no-store and a usable protected request. RepeatedLogin_IssuesDistinctTokenIdsWithTheSameConfiguredLifetime verifies separate token IDs. |
| Invalid credentials | UnknownAndIncorrectCredentials_ReturnTheSamePublicFailure checks generic 401 invalid_credentials. InvalidCredentials_NeverIssueTokensOrPersistRefreshSessions additionally verifies no token fields or refresh rows for unknown users/wrong passwords. Lockout and unsupported two-factor login also have tests. |
| Expired/invalid access tokens | AccessToken_IsRejectedExactlyAtItsExpirationWithoutGrace tests one tick before/at/after expiry. JwtBearerValidationTests covers strict issuer, audience, signature, algorithm and not-before/expiry validation. |
| Refresh rotation | Refresh_AfterAccessExpiry_ReturnsNewTokensWithoutExtendingTheFamily verifies renewal without an active access token, changed credentials, fixed family expiry and a usable replacement. Storage tests verify hashes only and rotation through an independent provider. |
| Refresh reuse | ReplayingAnAncestor_RevokesItsDescendantsButNotAnotherLoginFamily and concurrent-refresh tests verify 401 invalid_refresh_token, family revocation and isolation. RefreshExpiry_EnforcesTheAbsoluteCutoff tests exact boundaries. |
| Logout | Logout_RevokesRenewalButPreservesHistoryAndExistingAccessExpiry verifies 204, blocked renewal and retained access-token lifetime. Additional tests cover idempotency, ancestors, malformed/unknown/expired tokens, races and rollback. |
| Role enforcement | PublicRegistration_AssignsOnlyCustomerDespiteRequestedAdminFields rejects privilege overposting. TrustedAdminAssignment_GrantsBothPoliciesAndRefreshReflectsRemoval verifies Admin/Customer access, current roles on refresh, retained role snapshots in prior access tokens, and their rejection at expiry. Anonymous and roleless accounts receive 401 and 403 respectively. |
| Cross-user isolation | RefreshAndLogout_AreBoundToTheTokenOwnerAndLeaveOtherUsersUnaffected verifies refreshed DTO/JWT/protected subject identity and that one user's logout revokes only their family while another user can still refresh. |

## Run

Supply `TICKETBOOKING_TEST_CONNECTION_STRING` externally for an **isolated test database**, then run from the repository root:

```bash
dotnet test TicketBooking.slnx --configuration Release
```

For only the HTTP/token/role scenarios above:

```bash
dotnet test tests/TicketBooking.IntegrationTests --configuration Release --filter 'FullyQualifiedName~AuthEndpointsTests|FullyQualifiedName~JwtBearerValidationTests|FullyQualifiedName~AuthorizationPolicyTests'
```

Without the connection variable, PostgreSQL tests are skipped, so a green run alone does not prove registration/refresh/logout persistence. The existing CI PostgreSQL service supplies it; no workflow changes are needed for these tests. Test credentials/signing keys are synthetic and fixtures do not read development database secrets. Clocks are controlled without sleeps for token deadlines; Identity's own lockout clock remains framework-controlled.

## Code locations and limits

- [Registration/login fixture](../../../tests/TicketBooking.IntegrationTests/AuthEndpointsTests.cs)
- [Refresh tests](../../../tests/TicketBooking.IntegrationTests/AuthEndpointsTests.Refresh.cs)
- [Logout tests](../../../tests/TicketBooking.IntegrationTests/AuthEndpointsTests.Logout.cs)
- [Role tests](../../../tests/TicketBooking.IntegrationTests/AuthEndpointsTests.Roles.cs)
- [Credential side effects and cross-user isolation](../../../tests/TicketBooking.IntegrationTests/AuthEndpointsTests.Security.cs)
- [JWT validation and ownership HTTP tests](../../../tests/TicketBooking.IntegrationTests/JwtBearerValidationTests.cs)
- [Policy combinations](../../../tests/TicketBooking.IntegrationTests/AuthorizationPolicyTests.cs)
- [CI PostgreSQL configuration](../../../.github/workflows/ci.yml)

These tests do not verify browser certificate trust, UI storage, external identity providers, MFA completion, email delivery or production business-route authorization. See [known gotchas](known-gotchas.md) and the separate [ownership contract](resource-ownership.md).

Latest verification: **424 passed, zero failed/skipped** with disposable PostgreSQL; **121 passed** in AuthEndpointsTests, JwtBearerValidationTests and AuthorizationPolicyTests. Changed-test formatting passed. Detailed TRX results were saved to `/tmp/ticketbooking-auth-results/` for this run.
