# Known gotchas

- **Observed:** Missing connection configuration or JWT key stops startup before error middleware exists. Configure User Secrets per README; request ProblemDetails cannot report startup failures.
- **Observed:** Liveness does not check PostgreSQL. A healthy response cannot establish database readiness.
- **Observed:** OpenAPI is Development-only. JWT request authentication is now configured by the [Identity feature](../identity-persistence/FEATURE.md); diagnostics do not require login or prove business authorization.
- **Observed:** Middleware handles exceptions and bodyless error statuses; existing arbitrary response bodies are not rewritten. A response already started cannot reliably be replaced.
- **Observed:** Unsupported Accept headers still receive JSON through the explicit fallback. Clients should request application/problem+json.
- **Observed:** ArgumentException and arbitrary InvalidOperationException are not broadly mapped to 400/409. Validate request DTOs in future handlers; expected business failures must use DomainRuleException.
- **Observed:** A different provider reference reports payment_reference_mismatch before the terminal-outcome check. An identical final result/reference is a no-op; payment_already_processed indicates a conflicting outcome or disallowed terminal reference recording.
- **Observed:** Allocation conflicts indicate stale version/state or a missing row, not proof of which user owns a seat. Refresh and revalidate; do not blindly retry with a new token.

HTTP tests verify the error pipeline with synthetic routes, not real booking endpoints, production startup, TLS, redirects, health or actual Development exposure. Generated catalogue/Admin OpenAPI responses are now tested through the fixture. Registration/login and JWT middleware are now HTTP-tested separately. Application transaction coordination, business ownership authorization, durable callback deduplication, late-payment compensation and refunds remain implementation gaps.
