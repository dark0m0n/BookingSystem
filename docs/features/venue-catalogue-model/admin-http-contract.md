# Admin venue and session setup

Status: **implemented**. Every route below requires a valid Bearer token satisfying the Admin policy. Missing/invalid/expired authentication is 401 unauthorized; Customer-only authentication is 403 forbidden. Use the [Development initializer](../identity-persistence/development-admin.md) and normal login for a local Admin token. No credentials belong in tracked requests/configuration.

## Complete session setup

Send application/json with `Authorization: Bearer <access-token>`. Each POST returns 201 with a server-generated ID and Location header. Replace reference placeholders with IDs returned in preceding responses.

1. POST `/api/admin/venues`:

   ```json
   { "name": "Central Cinema", "address": "Kyiv, Main Street 1" }
   ```

2. POST `/api/admin/auditoriums`:

   ```json
   { "venueId": "<venue-id>", "name": "Hall 1" }
   ```

3. POST `/api/admin/auditoriums/{auditoriumId}/seats`:

   ```json
   { "seats": [ { "row": "A", "number": 1, "type": 0 }, { "row": "A", "number": 2, "type": 1 } ] }
   ```

   The batch creates physical seats atomically and returns their DTOs. Types: Standard=0 (default), Premium=1, Accessible=2. Add more batches before scheduling if needed. Coordinates are trimmed and case-sensitive; the batch and existing auditorium layout must have unique row/number pairs. The batch must contain 1–1000 nonnull seats, positive numbers and rows of at most 20 characters.

4. POST `/api/admin/events`:

   ```json
   { "title": "Example Film", "description": "A feature film", "durationMinutes": 90, "ageRating": 12, "status": 1 }
   ```

   Status: Draft=0 (default), Published=1, Archived=2. Scheduling requires Published. Duration must be positive, age rating nonnegative. Description is required but may be empty. Use PATCH `/api/admin/events/{id}` to update details or publish an existing draft before scheduling.

5. POST `/api/admin/sessions` using a future start with an explicit offset:

   ```json
   { "eventId": "<event-id>", "auditoriumId": "<auditorium-id>", "startsAt": "2030-01-01T18:00:00+02:00", "price": 150.00, "currency": "UAH" }
   ```

   The example date must remain in the future relative to server time. Both parents must exist, the event must be Published and the auditorium must contain seats. Price is required, nonnegative decimal(18,2), with UAH/EUR/USD currency (normalized uppercase). The same configured price applies to every seat, regardless of SeatType; each inventory row captures an independent Money copy. Zero/free prices, one cent and 9,999,999,999,999,999.99 per seat are valid in each supported currency. Missing/blank currency, missing price, excess precision, negative price and values above the maximum return 400 without creating a session or inventory. Session end comes from the stored event duration; clients do not supply endsAt or initial inventory state. Submitted extra fields cannot override these values.

   The response contains id, eventId, auditoriumId, startsAt, endsAt, status (Scheduled=0) and seats. Each seat contains id, seatId, row, number, type, price, currency, status (Available=0) and version (0). Times are normalized to UTC/PostgreSQL microseconds. A new inventory ID is generated for every physical seat in exactly the selected auditorium. Session and all inventory rows commit together.

6. GET `/api/admin/sessions/{id}` to inspect the complete persisted result. This is an Admin inspection endpoint; anonymous event listing is available through the [public contract](public-http-contract.md); anonymous session details and seat maps are also documented there.

## Management and lookup routes

| Method | Route | Result |
| --- | --- | --- |
| GET | /api/admin/venues | Venue DTO array ordered by name/ID. No pagination yet. |
| GET | /api/admin/venues/{id} | Venue DTO. |
| POST | /api/admin/auditoriums | New auditorium under an existing venue. |
| GET | /api/admin/venues/{venueId}/auditoriums | Auditorium DTO array ordered by name/ID. |
| GET | /api/admin/auditoriums/{id} | Auditorium DTO. |
| PATCH | /api/admin/auditoriums/{id} | `{ "name": "New name" }`; returns 200 DTO. Venue/ID are unchanged. |
| DELETE | /api/admin/auditoriums/{id} | 204; deletes unused auditorium and its physical seats atomically. |
| GET | /api/admin/auditoriums/{id}/seats | Physical seat DTO array ordered by row/number. |
| POST | /api/admin/auditoriums/{id}/seats | Atomic batch creation described above. |
| PATCH | /api/admin/auditoriums/{id}/seats/{seatId} | 200 seat DTO; requires row, positive number and optional type (default Standard=0). Retains seat ID and auditorium. |
| DELETE | /api/admin/auditoriums/{id}/seats/{seatId} | 204; removes that auditorium's unused seat. A seat from another auditorium is 404. |
| GET | /api/admin/events/{id} | Event DTO. |
| PATCH | /api/admin/events/{id} | Partial update of title, description, durationMinutes, ageRating, status; returns 200 Event DTO. |
| GET | /api/admin/sessions/{id} | Session DTO with complete inventory in one repeatable-read snapshot. |
| PATCH | /api/admin/sessions/{id} | Partial eventId/auditoriumId/startsAt update; 200 complete Session DTO. |

Once any session uses an auditorium, adding/editing/removing seats and deleting the auditorium return 409 conflict. An inventory reference to one of its physical seats also blocks these operations, even if the session header incorrectly names another auditorium. Renaming is still allowed. Edit a physical seat to correct its coordinates/type before scheduling; moving auditoriums between venues and mutating used layouts are not supported.

## Event updates

PATCH `/api/admin/events/{id}` accepts any nonempty subset of the event creation fields. For example, `{ "status": 1 }` publishes a draft; `{ "title": "Updated title", "durationMinutes": 120 }` changes only those fields. Omitted or null fields retain stored values; use `"description": ""` to clear the description. An empty/unknown-only/all-null patch is 400. Title is trimmed; description is preserved as supplied. Creation defaults status to Draft; updates never default an omitted status. All defined status transitions (Draft, Published, Archived) are allowed. ID comes from the route; extra body fields cannot replace it.

Creation and updates share validation: title nonblank/max 200 after trimming, description max 10000, durationMinutes positive, ageRating nonnegative and status defined. Invalid updates persist no changes; a valid update for a missing event returns 404. Event edits use an exclusive row lock and scheduling uses a shared event lock after the auditorium lock, both held until transaction completion. A concurrent schedule either captures the published event before an edit, or sees the committed edit and validates it. Existing sessions retain their captured EndsAt and inventory when event duration/status changes; archiving does not cancel sessions. Simultaneous partial edits merge against the latest locked row; changes to the same field take effect in lock order with no version/precondition check.

## Session updates

PATCH `/api/admin/sessions/{id}` accepts any nonempty subset of `eventId`, `auditoriumId`, `startsAt`. Omitted/null fields retain stored values. For example, `{ "startsAt": "2030-01-02T18:00:00+02:00" }` reschedules a session; the example date must be in the future. Empty patches and empty GUIDs are 400; missing sessions/events/auditoriums are 404. Only Published events and nonempty auditoriums are eligible. Starts are normalized to UTC/PostgreSQL microseconds and checked again after locks. Past/equal starts and derived-end overflow are 400. The existing session must still be Scheduled and future; otherwise 409.

A start-only edit preserves the captured duration even if the Event was edited later. Changing EventId captures the new event duration. Client endsAt, status, inventory and price fields cannot override server values. No cancellation/status-edit route is implemented.

Updates reject sessions with any hold/booking history or non-Available inventory (409), preserving captured reservation deadlines. History includes both header SessionId references and item SessionSeatId references, so inconsistent imported headers cannot hide a reservation. All hold and booking states block edits, including expired/cancelled/converted/refunded records with currently Available seats. Rejection leaves schedule, inventory IDs/versions/prices, history, totals, ownership and deadlines unchanged. Moves also require one uniform stored seat price. A move between auditoriums (including different venues) replaces all session inventory with exactly the destination auditorium seats, fresh inventory IDs, copied stored prices and version 0. It never moves physical seats. Existing inventory must match the source auditorium layout; inconsistency returns 409. Other schedule changes retain inventory IDs and increment their versions, invalidating stale allocation tokens. Even a valid no-op PATCH advances retained inventory versions. There is no schedule-overlap policy or repricing.

The transaction locks the session FOR NO KEY UPDATE, source/destination auditoriums in sorted ID order, the event FOR SHARE, then inventory FOR UPDATE in ID order. The session lock allows FK key-share locks from allocation transactions. Inventory updates/deletes retain expected-version predicates. If an allocation wins, session editing rejects the allocated/history state; if editing wins, stale allocation tokens/removed IDs fail. Every schedule/inventory write commits or rolls back together. GET reads session, inventory and layout in one repeatable-read snapshot, including during moves. Future customer workflows must use the allocation writer and persist history in the same transaction.

## Validation, errors and retries

Names/title must be nonblank and at most 200 characters, address 500, description 10000. Names/address/rows are trimmed. Unknown numeric enum values, empty required IDs, bad prices and nonfuture starts return 400 validation_failed with field errors. Missing referenced records return 404 not_found. Seat edits and deletions return 404 when the seat does not belong to the route auditorium, before checking whether its layout is fixed. Edits trim rows, exclude the current seat from duplicate checks and reject duplicate coordinates without changing state. Duplicate coordinates, fixed layouts, unpublished events and empty auditoriums return 409 conflict. Errors follow the existing ProblemDetails code/instance/traceId contract; provider exception details are not exposed.

No request idempotency key, schedule-overlap prevention, cleaning buffer, per-seat pricing or session cancellation route is added by these Admin operations. Anonymous event listing is documented separately. Repeating a successful POST can create another record; callers should retain returned IDs/Location and inspect the result after uncertain delivery. Layout/session concurrency is serialized with the same auditorium row lock; do not bypass it in future Admin writers.

Evidence: [registered handlers](../../../src/TicketBooking.Api/Administration/AdminCatalogueEndpoints.cs), [contracts](../../../src/TicketBooking.Contracts/Administration), [Admin HTTP tests](../../../tests/TicketBooking.IntegrationTests/AuthEndpointsTests.Administration.cs).

Input rules above are enforced by FluentValidation endpoint filters after authorization. All Admin routes document 401/403/500; body writes also document 400 validation_failed and 415 http_error, plus route-specific 404/409. Field names, partial-update semantics and transaction checks are unchanged. See [validation and ProblemDetails](validation-and-problems.md).
