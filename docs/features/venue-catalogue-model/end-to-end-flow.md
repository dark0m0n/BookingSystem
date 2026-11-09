# End-to-end flow

## Complete Admin setup

1. Obtain a valid server-issued Admin token through login. A trusted Development-only initializer is available; see [Identity](../identity-persistence/FEATURE.md). All routes below enforce BR-07 before executing a handler.
2. POST `/api/admin/venues` creates a server-generated venue ID; POST `/api/admin/auditoriums` references that venue (BR-08). Responses are 201 DTOs with Location headers.
3. POST `/api/admin/auditoriums/{id}/seats` validates the full batch, locks the auditorium and checks it has no sessions, then commits all physical seats together (BR-09/10). Duplicate coordinates return 409 and persist none of the batch.
4. POST `/api/admin/events` stores the offering's duration, age label and status. Use Published (1) to schedule it; PATCH `/api/admin/events/{id}` can publish a draft or update its fields (BR-14).
5. POST `/api/admin/sessions` validates IDs, price/currency and future start, begins a transaction, and locks the same auditorium row. It takes a shared lock on the event and loads the published event and all seats from that auditorium (BR-08/11). Missing parents return 404; draft/archived events or empty layouts return 409.
6. Normalize the start to UTC/PostgreSQL microseconds; derive end from the event duration and recheck the injected clock after waiting for the lock. Create the session and one Available version-zero inventory row per physical seat (BR-12). Save and commit together before 201/Location and the full session DTO. A failure before commit rolls back both session and inventory.
7. GET Admin resources to inspect the result. PATCH an auditorium name without changing its identity/venue. Seat editing/removal or auditorium deletion is allowed only before any session exists; auditorium deletion removes its unused seats in the same transaction.

## Alternatives and concurrency

Malformed/bounded input returns 400 validation_failed, missing records 404 not_found and layout/state conflicts 409 conflict. A valid Customer token receives 403; anonymous/invalid/expired credentials receive 401. There is no automatic retry or creation idempotency key. Retrying a successful create can create another venue, auditorium, event or session; seat coordinate duplicates remain conflicts.

Seat batches, seat editing/deletion, auditorium deletion and session creation all hold `SELECT ... FOR UPDATE` on the auditorium until commit/rollback. A racing layout addition either commits before inventory is copied, or observes the newly created session and returns 409. HTTP tests verify complete maps under races, duplicate batches and rollback after saved inventory writes.

PATCH `/api/admin/auditoriums/{id}/seats/{seatId}` resolves the seat within the locked auditorium (BR-13), checks the layout has no sessions, and rejects duplicate coordinates before mutation. Valid edits preserve identity/auditorium and return the saved DTO. A foreign or missing seat returns 404 for edit/delete even if the route auditorium has sessions. No move between auditoriums is supported.

Event PATCH validates supplied fields, rejects empty patches, locks the event FOR UPDATE and merges with current stored values before saving (BR-14/15). Omitted/null fields remain unchanged; an empty description clears it. Missing events return 404. Scheduling takes its shared event lock after the auditorium lock and retains it until commit, so it either captures the published pre-edit event or validates the post-edit state. Event edits never recalculate existing session EndsAt or inventory.

PATCH `/api/admin/sessions/{id}` validates the partial request (BR-16/17), locks the session, source/destination auditoriums, event and inventory, then checks eligibility/history and layout consistency (BR-18/19). History checks include both session headers and inventory-item references, without filtering expired/cancelled records. Missing parents return 404; ineligible state/empty layouts return 409. It derives and validates end time, then updates the schedule and either replaces inventory on an auditorium move or advances retained inventory tokens. Save/commit are atomic; injected write-failure tests exercise rollback and retry. No hold/booking deadline is recalculated. GET reads a coherent session/layout/inventory snapshot while moves run.

Domain constructors still enforce local ID/enum/time/price rules without I/O. Reserving/holding/booking seats remains a separate workflow; Admin management never claims/releases seat ownership; safe schedule edits can advance inventory version tokens without changing availability.

Layout writers check both Sessions.AuditoriumId and inventory-to-physical-seat references while holding the auditorium lock (BR-19). A conflicting reference returns 409 before any mutation; the operator must preserve the referenced history and use a separate supported inventory/layout instead. No forced-edit bypass, history deletion or automatic deadline migration is provided. In reservation races, a committed allocation/history blocks the edit; a successful edit invalidates stale allocation tokens or inventory IDs, rolling back the losing reservation transaction.

## Public browsing

1. An anonymous visitor GETs /api/events with optional filters (BR-20/21) and page parameters (BR-22). Repeated, malformed, oversized or overflowed input returns 400 validation_failed with field errors.
2. Query Published events only. Apply trimmed literal title/description search; apply date/venue through a single Scheduled-session EXISTS query when supplied. Unscheduled Published events remain eligible when neither date nor venue is supplied.
3. Begin a RepeatableRead snapshot, count filtered events, select the title/ID ordered SQL page and commit. Return items/page/pageSize/totalCount/totalPages/hasPreviousPage/hasNextPage; no matches or out-of-range pages return 200 with an empty items array. A missing venue is an empty result, not 404. Public listing performs no reservation writes.

## Public details and seat selection

1. GET /api/events/{id} returns Published event metadata; a missing/Draft/Archived ID is 404 (BR-23).
2. GET /api/events/{id}/sessions validates optional date/venueId/page/pageSize, checks the Published parent and returns Scheduled sessions sorted by StartsAt/ID (BR-24). A visible event without qualifying sessions returns an empty page; hidden parents are 404.
3. GET /api/sessions/{id} returns current event/auditorium/venue metadata and captured UTC session times, only for a Scheduled session of a Published event. Missing/hidden sessions are 404.
4. GET /api/sessions/{id}/seats reads a consistent complete map of its auditorium, with independent stored prices and persisted availability/version tokens (BR-25). Inconsistent inventory returns 409 without a partial/foreign map. The response is no-store. The public route does not load history/owners, reserve seats or derive prices from client data.
5. A future reservation client must retain the SessionSeat ID and Version and handle later allocation conflicts. A map is a snapshot, not a reservation; it must not blindly retry with a newer token. No customer hold/booking endpoint is introduced here.

Successful public query results cross a concrete typed DTO boundary (BR-27). After bounded page projection and consistent count retrieval, the shared response contract derives navigation metadata (BR-26); it never serializes tracked entities or navigation graphs. Empty and beyond-end responses retain requested page/pageSize and filtered total count.

## Input validation and documented failures

Admin authorization runs before request-body binding/FluentValidation. Validly bound requests pass through ValidateRequest<T>. Invalid fields return enriched 400 validation_failed before handler persistence; valid inputs continue to stored relationship/state checks and transactions. Public list queries preserve raw/repeated values, validate them asynchronously through CatalogueQueryValidator, then parse validated date/GUID/numeric values. Bad JSON/null required bodies/binding errors are handled by the framework/error pipeline; unsupported media is 415 http_error. OpenAPI lists applicable responses and illustrates safe ProblemDetails metadata without internal exception text.

## Development sample setup

1. Operator applies the existing migrations and sets ConnectionStrings:DefaultConnection; run --environment Development --seed-sample-data (BR-30). Other environments or missing connections fail before persistence.
2. In one transaction/advisory lock, create missing reserved-ID venue/auditorium/Event roots, then lock the auditorium and Published event. Refuse changed parent/publication relationships or an empty used layout.
3. Create the initial 3×8 layout if unused/empty, otherwise retain the stored layout. Reuse a future Scheduled sample session only when inventory matches the physical layout (BR-31).
4. If no future session remains, capture a new start approximately one day ahead in UTC/whole-minute precision, derive end from stored event duration and generate independent inventory at 150 UAH (BR-32). Commit before printing IDs/start time. Reruns retain previous dates, prices, versions, holds and bookings; they never reset sample history.
5. Start the API normally and browse the sample through public event/session/map routes. No Admin or JWT is needed for the command or anonymous browsing. Failed seeding exits nonzero with a generic diagnostic; fixing config/data and rerunning is safe.

Seat-map caching (`seat-availability-cache`, SC-01/02/03/04): check public session in RepeatableRead → count physical layout → for eligible maps read inventory IDs/versions → lookup revision key → return validated hit or query/validate full map → commit → fill the original revision. Inventory commits select new keys; late old fills remain isolated. See [cache flow and invalidation](seat-availability-caching.md).
