# Public catalogue endpoints

Status: implemented; working tree inspected. `GET /api/events` allows anonymous access and returns Published event metadata. No authentication or role is required.

Example: `GET /api/events?query=space&date=2030-01-02&venueId=<venue-guid>&page=1&pageSize=20`. Replace the venue placeholder with a stored ID; a date in the past is allowed.

| Input | Default | Validation / meaning |
| --- | --- | --- |
| query | No text filter | Trimmed, at most 200 characters. Case-insensitive literal substring in title or description; blank is no filter. `%`, `_` and backslash are escaped rather than treated as SQL wildcards. |
| date | No date filter | Exact `yyyy-MM-dd`. Match Scheduled session starts in the UTC day, including midnight and excluding the next midnight. Maximum DateOnly date is accepted without overflowing next-day arithmetic. |
| venueId | No venue filter | Nonempty GUID. Follow Session.AuditoriumId → Auditorium.VenueId. Unknown valid ID returns an empty result. |
| page | 1 | Positive Int32 integer; `(page - 1) * pageSize` must fit Int32. |
| pageSize | 20 | Integer 1–100 inclusive. Out-of-range values are rejected, not clamped. |

All supplied parameters must occur once. Blank date/venue/numeric values, malformed values and offset overflow return 400 `validation_failed` ProblemDetails with field errors, instance and traceId. Unrecognized parameter names are ignored. Numeric parsing is invariant and accepts digits only.

With neither date nor venue, Published events can appear without sessions. When either filter is supplied, at least one Scheduled session must match; cancelled/completed sessions do not qualify. When both filters are supplied they must match the same session. Events appear once regardless of the number of matching sessions. No automatic future-only, sold-out or price filter is applied.

A 200 response has this shape:

```json
{
  "items": [
    {
      "id": "00000000-0000-0000-0000-000000000001",
      "title": "Space story",
      "description": "An event description",
      "durationMinutes": 90,
      "ageRating": 12
    }
  ],
  "page": 1,
  "pageSize": 20,
  "totalCount": 1,
  "totalPages": 1,
  "hasPreviousPage": false,
  "hasNextPage": false
}
```

Items sort by title under database collation and then ID. TotalCount counts all events satisfying the filters before pagination. Both event and event-session lists share these top-level fields: items, page, pageSize, totalCount, totalPages, hasPreviousPage and hasNextPage. TotalPages is ceiling(totalCount / pageSize), or 0 when no matches exist; HasPreviousPage means page > 1, including empty/out-of-range pages, and HasNextPage means page < totalPages. Existing field names and detail/seat-map shapes are retained. Count and items use the same repeatable-read database snapshot. Empty results or pages beyond the end return 200 with empty items and the appropriate totalCount; separate page requests may shift under concurrent edits. SQL applies filtering and Skip/Take before response materialization. No reservation, owner, inventory or Identity data is exposed.

Evidence: [route and validation](../../../src/TicketBooking.Api/Catalogue/CatalogueEndpoints.cs), [response contracts](../../../src/TicketBooking.Contracts/Catalogue), [public HTTP tests](../../../tests/TicketBooking.IntegrationTests/AuthEndpointsTests.PublicEvents.cs). No schema change, configuration, external search provider or frontend is introduced.

## Event and session details

All routes below are anonymous. Published event visibility and Scheduled session visibility are shared across the public catalogue; hidden and missing resources return the same 404 `not_found`. Malformed GUID route segments also return 404. No implicit future-time filter is applied.

| Route | Response |
| --- | --- |
| GET /api/events/{id} | EventSummaryResponse with id, title, description, durationMinutes and ageRating. |
| GET /api/events/{id}/sessions | EventSessionListResponse with items, page, pageSize, totalCount, totalPages, hasPreviousPage and hasNextPage. |
| GET /api/sessions/{id} | SessionDetailsResponse with event, auditorium, venue and stored session-time metadata. |
| GET /api/sessions/{id}/seats | SessionSeatMapResponse containing the complete priced inventory map. |

Event-session lists accept `date`, `venueId`, `page`, `pageSize` under the exact same bounds/parsing rules as event listing, including duplicate rejection and UTC start-day semantics. They sort by startsAt then id. Each item contains id, eventId, eventTitle, auditoriumId, auditoriumName, venueId, venueName, venueAddress, startsAt, endsAt and status. Session details return the same shape. Times retain their server-captured UTC values; current Event.Duration does not recompute EndsAt. Lists check parent visibility, count and page within one repeatable-read snapshot. A Published event with no matching sessions returns 200 with an empty page; an unknown valid venue also produces an empty page. Invalid query inputs return 400 before parent lookup.

Seat maps return sessionId, auditoriumId and seats ordered by row/number/physical ID. Each seat includes id (SessionSeat inventory identifier), seatId (physical identifier), row, number, type, price, currency, status and version. SeatType is Standard=0/Premium=1/Accessible=2; availability is Available=0/Held=1/Booked=2/Unavailable=3. The amount/currency and version come directly from that session’s stored inventory. Multiple sessions sharing physical seats retain independent inventory/prices/availability.

Seat maps use `Cache-Control: no-store` and a repeatable-read snapshot across session, inventory and physical-layout queries. All physical seats must be represented and belong to the session auditorium. Incomplete or cross-auditorium inventory returns 409 `conflict` with no partial/foreign seat data. An empty layout with empty inventory returns an empty map, although normal Admin creation refuses empty layouts.

Availability is persisted state: this read does not expire holds, infer ownership, reserve seats or recalculate prices. It never returns customer/owner IDs, hold/booking IDs, private expiry data or Identity entities. A future reservation client must retain inventory id/version and revalidate through authenticated allocation workflows; a snapshot is not a reservation and must not be blindly retried with refreshed tokens.

Evidence: [public detail/session/map handlers](../../../src/TicketBooking.Api/Catalogue/CatalogueEndpoints.cs), [detail and seat-map HTTP tests](../../../tests/TicketBooking.IntegrationTests/AuthEndpointsTests.PublicDetails.cs). These routes are registered by MapCatalogueEndpoints in Program.

## Stable response boundary

All successful public handlers declare concrete typed Ok responses from TicketBooking.Contracts.Catalogue. EventListResponse and EventSessionListResponse inherit shared PagedResponse<T> metadata with concrete DTO item types. Totals/navigation use integer arithmetic with a widened intermediate, so Int32-sized counts cannot overflow the page calculation. JSON field names remain camelCase; no entity, navigation graph, EF proxy, IQueryable or Domain Money object is serialized. Prices remain explicit decimal/currency scalars, and session times remain UTC DateTimeOffset instants. Errors retain the existing ProblemDetails contract.

[Response contract HTTP tests](../../../tests/TicketBooking.IntegrationTests/AuthEndpointsTests.ResponseContracts.cs) assert exact response field sets and declared 200 DTO types for all five public routes; [pagination contract tests](../../../tests/TicketBooking.IntegrationTests/PaginationContractTests.cs) verify navigation at empty/last/out-of-range/large-count boundaries and JSON round-tripping.

Input rules now run through FluentValidation via CatalogueQueryValidator before parsing. Public endpoints document 500 internal_error in addition to their existing 400/404/409 outcomes; the request/response shapes and field names are unchanged. Development OpenAPI includes application/problem+json schemas/descriptions/examples. See [validation and ProblemDetails](validation-and-problems.md).

## Seat-map server cache

Eligible maps may use Redis projections with a short TTL, while PostgreSQL still checks visibility, physical layout count and current inventory revision in the same RepeatableRead snapshot. Small maps bypass caching. HTTP no-store, DTO ordering/fields, ownership privacy and 404/409 semantics remain unchanged. Inventory commits select new keys, including confirmation and release; key expiry itself never releases inventory. See [scope and exact invalidation boundaries](seat-availability-caching.md).
