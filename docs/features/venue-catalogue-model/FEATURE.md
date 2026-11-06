# Venue and catalogue domain model

Status: **partial**. Six domain entities, EF mappings/migrations, Admin-only setup endpoints and anonymous event browsing, session details and seat-map endpoints are implemented. An Admin can create a venue, manage auditoriums/layouts, create/update an event, and create/update sessions with priced inventory. Schedule-overlap rules, customer booking routes and session cancellation remain deferred. Working tree inspected on top of `93f4f16`.

Contributors can represent a venue, auditorium, physical seat, event/movie, session/screening, and priced session seat. Canonical names are `Event` and `Session`; no duplicate Movie or Screening types are introduced. Venue entities live in Venue; event scheduling and session inventory live in Catalogue within the existing assembly.

## Acceptance and verification

- Requested fields and the four SessionSeat statuses are implemented; see [data model](data-model.md).
- Session creation generates exactly one inventory row per stored auditorium seat, each with a new ID, Available status, version 0 and an independent copy of the configured price/currency. Free, one-cent and maximum decimal(18,2) prices are supported in UAH/EUR/USD. Inventory and session commit together; customer checkout totals remain server-derived.
- Constructors reject invalid local values; targeted xUnit tests cover representative invalid inputs, status values, relationships, and time offsets.
- ORM mappings and reference/identity constraints are now supplied by [persistence](../local-persistence/FEATURE.md); row-level concurrency is now implemented by the [concurrency feature](../session-seat-concurrency/FEATURE.md), while customer reservation workflows remain unimplemented. Test execution results are reported separately.

Run `dotnet test tests/TicketBooking.Domain.Tests/TicketBooking.Domain.Tests.csproj` after restoring dependencies. This exercises pure domain behavior without a database or API secrets.

## Navigation

- [Business context](business-context.md)
- [End-to-end flow](end-to-end-flow.md)
- [Entry points](entry-points.json)
- [Application responsibilities](per-app-responsibility.md)
- [Frontend structure](frontend-structure.md)
- [Backend structure](backend-structure.md)
- [Data model](data-model.md)
- [Integration points](integration-points.md)
- [Known gotchas](known-gotchas.md)

## Public catalogue

GET `/api/events` is anonymous and returns Published events with optional text, UTC session-date and venue filters. Pagination defaults to page 1/size 20 and rejects sizes outside 1–100. Both list DTOs return totalCount/totalPages and previous/next navigation flags while preserving existing fields; public handlers explicitly declare their successful DTO type. Date and venue must match the same Scheduled session; each event appears once. Anonymous event details, paged event sessions, session details and a complete priced seat map are also available. Hidden events/non-Scheduled sessions return 404; inconsistent seat maps return 409 without partial results. See the [public HTTP contract](public-http-contract.md) for request/response fields and examples.

## Validation and failures

FluentValidation rules cover public catalogue filters and Admin request bodies. Authorization runs before Admin validation; storage/state checks and post-lock clock checks remain in handlers. Responses retain validation_failed with camelCase/indexed field errors. Development OpenAPI declares applicable ProblemDetails statuses and safe examples. See [validation and ProblemDetails](validation-and-problems.md).

## Local sample catalogue

An explicit Development-only `--seed-sample-data` command now creates a sample venue, auditorium, 24-seat layout, Published movie/Event and future session with complete 150 UAH inventory. It preserves existing records/history, reuses a future session and adds another when the old one has passed. It runs without HTTP hosting, JWT or an Admin account. See [development sample data](development-sample-data.md).

## Admin setup

All `/api/admin` routes require the Admin policy (401 for missing/invalid credentials, 403 for Customer-only credentials). Follow the [Admin HTTP contract](admin-http-contract.md) to create a complete session. Session creation derives the end time from event duration and creates one Available, version-zero SessionSeat per physical seat in the selected auditorium. Parent existence, nonempty layouts, future starts, published event status and Money precision are checked before commit.

Layout changes and session creation serialize on the auditorium row. Once any session references an auditorium, its seats cannot be added/edited/removed and the auditorium cannot be deleted; renaming remains allowed. Seat edits preserve identity and auditorium, reject duplicate trimmed row/number pairs and return 404 for seats outside the route auditorium. Event PATCH supports partial edits and publishing/archiving. Event updates serialize with scheduling; existing sessions keep their captured times and inventory. Session PATCH can change event, auditorium or start before a session begins, provided all seats are available and no hold/booking header or item references it. All history states block edits; captured seat IDs, prices, owners and deadlines remain unchanged on rejection. Layout guards follow both session auditorium IDs and inventory-to-physical-seat links, including inconsistent imported data. Auditorium moves atomically rebuild inventory; schedule-only edits retain seats and advance version tokens. Session reads use a consistent database snapshot. All inventory creation commits with the session or rolls back. No new migration is required; the existing six-entity schema is reused.

- [Admin HTTP contract and example setup](admin-http-contract.md)

Verification: **142 targeted catalogue HTTP/PostgreSQL cases across 58 methods passed with zero skips**, including seven new regressions. Coverage includes all five requested areas, all 18 Admin routes, combined filters/pagination, compound errors, credentials and seat-map limits/state/privacy. Tests used a disposable PostgreSQL 17 instance; the seeded local Compose database was untouched. See [coverage and run instructions](testing.md).
