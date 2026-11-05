# Known gotchas

- **Observed in source:** Removing either required configuration value prevents API startup, so even `/health` is unavailable. Cause: configuration guards precede `builder.Build`. Configure both values using User Secrets (README).
- **Inferred-risk:** A healthy response can coexist with an unavailable database because no database health check is registered. Treat `/health` as liveness only; verify database readiness separately.
- **Observed in source:** OpenAPI mapping is skipped outside Development. Use the Development profile for local inspection; do not assume the route exists in production.
- **Observed in source:** Requiring `Jwt:SigningKey` does not authenticate requests. No authentication/authorization registration exists. Do not infer access control from the configuration guard.
- **Inferred-risk:** Clients requesting unsupported response media types may not receive JSON ProblemDetails. Use `Accept: application/problem+json` for manual checks.

Runtime response bodies, TLS setup, redirects, and exception handling are Not verified. Existing empty tests do not cover these paths. Database readiness and authentication remain implementation gaps, not documented guarantees.
