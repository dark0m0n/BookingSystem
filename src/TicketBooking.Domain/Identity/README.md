# Identity

Namespace for future C# types: `TicketBooking.Domain.Identity`. Match any nested namespaces to their folders.

## Ownership

Accounts, identity references, and domain-level roles or access concepts.

## Boundary

Authentication middleware, token handling, and credential storage belong in the appropriate API/Infrastructure implementations. Do not put payment or booking state here.

These are responsibility guidelines for future implementation. RoleNames defines framework-independent Customer/Admin constants; ResourceOwnership provides the common nonempty owner-ID check used by hold/booking transitions and Application authorization. No account entity, framework authorization policy or application workflow lives in this folder. Lifecycle rules and exact fields require feature-specific requirements.

Keep domain rules free of ASP.NET Core, EF Core, provider SDKs, and network I/O. Prefer identifiers or immutable values for cross-boundary references; use Application to coordinate workflows rather than having one boundary mutate another boundary's aggregates. All boundaries remain in the same assembly and process.

See the [domain boundary map](../README.md) for the shared conventions.

ASP.NET Core Identity account storage is now implemented in `TicketBooking.Infrastructure.Identity` using GUID users and the shared EF context; see [Identity persistence](../../../docs/features/identity-persistence/FEATURE.md). Keep those framework types outside this Domain folder. Public registration/password login and JWT bearer validation now live in the API; persisted refresh-token rotation also lives in Infrastructure/API. Family logout/revocation is also implemented there; named Customer/Admin API policies and transactional Customer registration are also implemented there. Resource-specific business/ownership endpoints remain future work.
