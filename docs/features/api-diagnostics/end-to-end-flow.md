# End-to-end flow

Trigger: a developer or operator sends a request to a running API.

1. Startup reads connection configuration and signing key (`api-startup`). Missing values throw before the server runs.
2. The request enters exception handling, status-code handling, and HTTPS redirection (`api-errors`). HTTP clients may be redirected before reaching an endpoint.
3. `/health` invokes the registered health middleware (`api-health`, BR-01). No application data is read or changed.
4. In Development, the OpenAPI endpoint is registered by `MapOpenApi` (`api-openapi`, BR-02). README documents the default `/openapi/v1.json` location; there is no explicit route literal in this repository's registration.
5. A bodyless error can be formatted as ProblemDetails when the client accepts a supported JSON representation; customization supplies `instance` and `traceId` (`api-errors`, BR-03/BR-04).

Evidence for entry IDs: [entry-points.json](entry-points.json). Production skips OpenAPI registration; a request to its absent route follows ordinary error handling. Unhandled request exceptions use the generic error pipeline described in README. Exceptions during startup occur outside that request pipeline.

Transactions, asynchronous jobs, persistent state transitions, confirmation, cancellation, and human handoff: Not applicable for these diagnostic requests. No application retry policy is configured. HTTP response details and HTTPS behavior are Not verified at runtime.
