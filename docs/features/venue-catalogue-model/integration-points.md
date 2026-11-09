# Integration points

| Direction | Contract / authority | Failure / retry behavior |
| --- | --- | --- |
| Operator → API | Bearer JWT with persisted Admin role claims; Contracts.Administration JSON. | 401 without valid authentication, 403 without Admin; no automatic create retry. |
| API → Venue/Catalogue Domain | Validated constructors, Event.UpdateDetails, Auditorium.Rename and Money. | Local values checked before persistence; existing Domain guards remain framework-free. |
| API → shared EF/PostgreSQL | Required parent queries, auditorium row lock and explicit write transactions. | 404 for missing parents, 409 for duplicate coordinates/fixed layouts; rollback on failure. |
| Session → event/layout | Stored event duration and selected auditorium's seats are authoritative. | Nonpublished event or empty layout is 409; invalid start/price is 400. |
| Setup → future reservations | SessionSeat starts Available with captured price and Version 0. | Existing ownership-changing writes must use the concurrency rules; setup performs no hold/payment calls. |

See [Identity authentication/Admin policies](../identity-persistence/FEATURE.md), [persistence](../local-persistence/FEATURE.md), [Money](../money-and-pricing/FEATURE.md), [concurrency](../session-seat-concurrency/FEATURE.md) and [error codes](../api-diagnostics/error-codes.md). The existing migration chain is reused; no external providers, broker, worker or new configuration secrets are introduced.

Event creation/update share validation and EventResponse. UpdateEventRequest supplies nullable partial fields; the stored event is merged under an exclusive lock. Scheduling reads event status/duration under a shared lock through commit; existing sessions keep their captured times/inventory. No automatic session cancellation or repricing is triggered.

Session PATCH reuses SessionResponse and accepts only UpdateSessionRequest fields. Session.UpdateSchedule changes scalar schedule values; the API owns parent checks, published-state checks, layout/inventory reconciliation and transactions. Existing hold/booking headers and item references prevent changes in every historical state, including when a malformed header points to a different session. Retained inventory advances fencing tokens; moved inventory receives new IDs. The allocation writer therefore rejects stale pre-edit inventory observations. No captured deadline or price history is rewritten.

Seat edits reuse SeatRequest/SeatResponse and never accept a new auditorium or seat ID from the body; the route and stored relation are authoritative. Foreign seats are 404 for editing/deletion, including fixed layouts.

Cross-row auditorium consistency is enforced when these Admin handlers generate inventory; raw EF/SQL can still bypass that application rule. Session cancellation, schedule conflict prevention, resource-specific management audit history remain future work.

Layout safety also follows SessionSeat.SeatId to the owning physical auditorium, not only Session.AuditoriumId. These conservative checks detect committed inconsistent references that independent FKs allow; they do not repair the data or replace transactional version-fenced customer allocation writes.

Public visitor → CatalogueEndpoints.GET /api/events uses [public response contracts](public-http-contract.md) without authentication or ownership checks because it returns Published event metadata only. The query reads the shared EF/PostgreSQL store through event/session/auditorium relationships. It has no external search provider, price calculation, seat allocation or per-user behavior. Database cancellation follows RequestAborted; malformed filters are 400 validation_failed, empty results are 200.

Public details and event-session lists reuse event publication/session-state visibility and existing UTC/date/pagination conventions. Session metadata joins the stored auditorium/venue. Seat maps integrate with SessionSeat allocation fencing by exposing inventory Id/Version, stored status and independent price/currency; consumers must still perform authenticated ownership/deadline checks in later workflows. Maps do not expose hold/booking/customer identities or automatically expire allocations. Consistent snapshots prevent mixed old/new layouts during Admin moves.

Public response compatibility: items/page/pageSize/totalCount remain top-level fields, with additive totalPages/hasPreviousPage/hasNextPage metadata shared across both paged routes. Event/session/seat-map item field names, scalar money/time representation and ProblemDetails errors remain stable. DTOs live in Contracts and handlers declare their concrete success type; EF persistence types stay behind query projections.

FluentValidation is an in-process API dependency. Explicit singleton validators are stateless; session validators reference the injected TimeProvider dynamically. Public and Admin request records retain their existing shapes and Contracts gain no validation dependency. Validation failures flow through ApiProblemDetails.Validation; binding/errors still flow through ASP.NET Core diagnostics. OpenAPI uses Microsoft.AspNetCore.OpenApi operation transformation over declared response metadata; it does not call a network service. Authentication validation remains unchanged.

Local operator → --seed-sample-data → DevelopmentSampleDataSeeder → shared PostgreSQL is a one-shot, explicit path. It uses the same domain constructors, Money snapshots and auditorium/event locks as Admin scheduling. A seed-specific transaction-scoped advisory lock serializes concurrent commands; insertions never bypass version-checked ownership writes because existing inventory is not mutated. Public catalogue endpoints can inspect the resulting Published event/session/map. The command uses the existing connection configuration/migration chain, no Identity managers/accounts, external providers or new packages.

The public seat-map endpoint uses Application ISeatAvailabilityCache through Infrastructure RedisSeatAvailabilityCache and the existing ConnectionStrings:Redis provider. Each read validates its PostgreSQL revision; Redis failure falls back to SQL. Hold creation, cancellation, expiry, transfer, confirmation and release invalidate through committed inventory IDs/version changes, without a Redis dependency in allocation transactions. See [event-specific boundaries](seat-availability-caching.md) and [hold response caching](../reservation-booking-payment/hold-caching.md).
