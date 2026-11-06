# Frontend structure

Not applicable: no frontend exists. Admin operators use authenticated JSON requests documented in the [HTTP contract](admin-http-contract.md). There are no management screens, browser API clients, cookie/CSRF flow or frontend tests.

A future client must retain server-returned IDs, create a layout before scheduling, handle 400 field errors/404 missing records/409 layout conflicts, and refresh displayed data after successful writes. UI visibility cannot grant Admin permission. Public browsing and seat-map screens remain future work. A future browsing client can call the anonymous [public catalogue contract](public-http-contract.md), reset page when filters change, and handle 400 field errors; no browser client is implemented here.

The API supports the browse → event detail → filtered sessions → session detail → seat-map flow. A future UI can render row/number/type plus price/currency and stored availability, retaining inventory Id/Version for an eventual authenticated reservation request. It must handle hidden-resource 404 and inconsistent-map 409, refresh after changes, and treat availability as a snapshot. No screens or browser client are implemented.

A future pagination control can consume totalPages/hasPreviousPage/hasNextPage instead of guessing from items.length. An out-of-range page can be empty with a nonzero filtered total; a page > 1 still has HasPreviousPage=true when there are zero matches. Existing page resets after filter changes remain the client’s responsibility.

A future client should show errors by the returned camelCase/indexed keys and branch on code/status, without parsing message wording. Field errors may be absent for malformed JSON/binding failures. These server validators and ProblemDetails examples do not add browser form validation or screens.

Not applicable for the seed command itself: operators use the CLI. After seeding and starting the API, existing public JSON endpoints expose the sample event/session/seat map; no UI or auto-login is added.
