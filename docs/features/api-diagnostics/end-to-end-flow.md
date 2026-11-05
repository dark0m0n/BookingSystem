# End-to-end flow

Trigger: an HTTP request reaches the running API.

1. Startup validates required configuration and registers error services (`api-startup`, `api-errors`). Startup exceptions occur outside request handling.
2. Exception and status-code middleware wrap endpoint execution; HTTPS redirection precedes the endpoint. `/health` and Development OpenAPI remain diagnostic paths (BR-01/BR-02).
3. A future business handler can call a domain guard. Rejection throws DomainRuleException carrying a DomainError; allocation writes can throw SeatAllocationConflictException. These business routes are not implemented.
4. ApiExceptionHandler selects a public definition using ApiErrorCatalog (`error-handler`, BR-05/BR-06). Unexpected exceptions are logged and mapped to `internal_error`, without exposing their messages.
5. ApiProblemDetails writes `application/problem+json` with a stable code, request path and trace ID (BR-03/BR-04). If the framework writer rejects Accept, a JSON fallback preserves the contract.
6. Bodyless error statuses, such as 404/405, use the same writer and status-based code. Existing response bodies are not rewritten by status-code middleware.

Alternative: identical payment result/reference replay returns normally (BR-07); conflicting outcomes fail. A stale allocation token fails with `seat_not_available`; clients must refresh and revalidate before deciding to retry.

[Entry points](entry-points.json) index the real registrations. HTTP tests use synthetic routes to exercise these steps; no new business routes are registered in Program.cs. Transactions, refund handoff, expiration workers, durable payment deduplication and network retry policies remain separate application work.
