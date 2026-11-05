# JWT bearer validation contract

Status: **implemented**. [AuthenticationConfiguration](../../../src/TicketBooking.Api/Auth/AuthenticationConfiguration.cs) configures the default Bearer scheme; [Program](../../../src/TicketBooking.Api/Program.cs) registers authentication/authorization middleware. Settings are validated and captured at startup.

| Check | Required behavior | Configuration |
| --- | --- | --- |
| Issuer | Present and exactly matches Jwt:Issuer, including case. | ValidateIssuer=true, ValidIssuer. |
| Audience | Present; at least one audience exactly matches Jwt:Audience, including case and trailing slash. | ValidateAudience=true, RequireAudience=true, ValidAudience, IgnoreTrailingSlashWhenValidatingAudience=false. |
| Signature | Signed by the configured symmetric key using HS256. Unsigned, modified, wrong-key and other-algorithm tokens reject. | ValidateIssuerSigningKey=true, RequireSignedTokens=true, IssuerSigningKey, ValidAlgorithms=[HS256]. |
| Expiration | exp is required; now must be strictly before it. | RequireExpirationTime=true, ValidateLifetime=true, ClockSkew=0 and injected-clock LifetimeValidator. |
| Not-before | When nbf is present, now must be at or after it. | Same LifetimeValidator; no early acceptance grace. |

TimeProvider supplies one UTC reading per lifetime check. Combined eligibility is `(!nbf || nbf <= now) && now < exp`; there are no sleeps or static clock reads in this application validator. Issued JWT dates and response expiresAt use whole-second precision. The default token lifetime is 15 minutes; changing it affects new tokens, not an existing token's captured expiry.

Set Jwt:Issuer/Jwt:Audience in configuration and Jwt:SigningKey through User Secrets or another server-side configuration source. The key must contain at least 32 random UTF-8 bytes; AccessTokenMinutes must be 1–1440. Invalid configuration fails startup. Clients receive tokens, never the signing key.

On a route requiring authorization, missing/invalid bearer credentials return 401 with WWW-Authenticate:Bearer and safe ProblemDetails code unauthorized. Signature/lifetime diagnostics and keys are omitted; unsupported Accept headers still receive JSON. Anonymous auth endpoints remain anonymous; authentication middleware alone does not protect a route.

## Verification and limits

[JwtBearerValidationTests](../../../tests/TicketBooking.IntegrationTests/JwtBearerValidationTests.cs) runs the production registration and error pipeline in real Kestrel without Identity stores or PostgreSQL. It checks accepted server-issued claims, missing/wrong issuer/audience, audience trailing slashes, missing/past expiration, unsigned/tampered tokens, wrong keys/algorithms, future nbf, one-tick time boundaries and safe responses. The focused JWT/configuration/error suite passed 44 tests.

```bash
dotnet test tests/TicketBooking.IntegrationTests/TicketBooking.IntegrationTests.csproj --configuration Release --filter FullyQualifiedName~JwtBearerValidationTests
```

Existing [HTTP account tests](../../../tests/TicketBooking.IntegrationTests/AuthEndpointsTests.cs) separately verify PostgreSQL-backed login tokens and roles. The [refresh lifecycle](refresh-token-rotation.md) now separately checks account state and rotates/revokes renewal rights. This access-token validator does not check a live account/security stamp, revoke access tokens, rotate keys or itself implement logout; the separate [logout route](logout-and-revocation.md) revokes refresh families. Persisted role claims are a snapshot at issuance; the separate [resource ownership adapter](resource-ownership.md) now consumes validated sub claims; production business handlers remain future work. No migration or database access is required for this validation change.
