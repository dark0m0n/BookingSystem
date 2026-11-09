# Business context

The supplied field list defines the initial model for cinema/event inventory. A physical seat belongs to an auditorium; a SessionSeat describes that seat's price and availability for a session. Domain constructors remain available to code callers; authenticated Admin operators can now create complete sessions through HTTP.

| Rule | Requirement or implementation choice | Enforcement |
| --- | --- | --- |
| BR-01 | Required entity/relationship identifiers are nonempty GUIDs. Implementation choice. | Constructors call `DomainGuard.Id`. |
| BR-02 | Names, title, address, and row are nonblank; description is nonnull but may be empty. Implementation choice. | `DomainGuard.Text`; Event constructor. |
| BR-03 | Duration and seat number are positive; age rating, price, and version are nonnegative. Implementation choices. | Event, Seat, SessionSeat constructors. |
| BR-04 | Session ends strictly after it starts, comparing actual instants. Implementation choice. | Session constructor with DateTimeOffset comparison. |
| BR-05 | Session seat states are Available, Held, Booked, Unavailable. User requirement. | `SessionSeatStatus` and enum validation. |
| BR-06 | Other enums accept only their defined members. Provisional modeling choice. | `DomainGuard.Defined`. |

Evidence: linked source files in [data-model.md](data-model.md). AgeRating is an integer minimum-age label, not a jurisdictional classification or enforced admission check. Currency/precision now follow the [Money policy](../money-and-pricing/FEATURE.md). Hold/payment/cancellation windows now follow the [timing policy](../booking-timing/FEATURE.md). Database uniqueness and version-fenced SessionSeat writes now exist in persistence. Admin setup follows the rules below; dynamic pricing, schedule overlap prevention and refund workflows remain future work.

## Admin operation rules

| Rule | Requirement / implementation choice | Enforcement |
| --- | --- | --- |
| BR-07 | All administration endpoints require the Admin policy; Customer role alone is insufficient. | MapAdminCatalogueEndpoints group; HTTP 401/403 tests. |
| BR-08 | Referenced venue/event/auditorium must exist; session uses exactly its auditorium's physical seats. | EF lookups and filtered seat query; parent FKs remain restrictive. |
| BR-09 | Seat batches are atomic, 1–1000 rows, with trimmed case-sensitive coordinates unique within the auditorium. | Input validation, auditorium row lock, duplicate check and unique index. |
| BR-13 | Seat edits preserve identity and auditorium; edits/deletes resolve the seat within the route auditorium before checking session state. Duplicate row/number pairs exclude the edited seat itself. | UpdateSeatAsync/DeleteSeatAsync scoped lookups, row lock and unique index; Seat.UpdateLayout validates all values before mutation. |
| BR-10 | Once an auditorium has any session, its layout (including coordinates and type) is fixed and deletion is refused; rename remains allowed. Conservative implementation choice. | Layout/delete handlers check Sessions while holding the same row lock as session creation. |
| BR-11 | A new session requires a published event, a nonempty auditorium and future start. Published-only scheduling is an implementation choice. | CreateSessionAsync; injected TimeProvider rechecked after locking. |
| BR-14 | Event updates preserve identity, accept defined statuses and retain omitted/null fields. Description can be cleared with an empty string; at least one nonnull field is required. | UpdateEventAsync/UpdateEventRequest; Event.UpdateDetails validates all values before mutation. |
| BR-15 | Event status/duration updates coordinate with session creation. Existing session times/inventory are retained; only Published events can create new sessions. | UpdateEventAsync FOR UPDATE; CreateSessionAsync FOR SHARE until commit. All defined status transitions are allowed; no one-way publishing lifecycle is imposed. |
| BR-16 | Session PATCH accepts eventId, auditoriumId and startsAt; omitted/null fields retain values. Only future Scheduled sessions with all inventory Available, a complete source layout and no hold/booking history can change. | UpdateSessionAsync locks session/inventory and checks stored parents/history. A conservative management policy; no cancellation workflow is added. |
| BR-17 | New starts must remain future after UTC/microsecond normalization and lock waits; end is derived and must fit the supported range. | NormalizeInstant; CreateSessionAsync/UpdateSessionAsync recheck injected TimeProvider. A start-only edit retains captured duration; changing EventId captures the new published event duration. |
| BR-18 | Destination auditorium must exist and have seats. A move replaces inventory with exactly its seats at the uniform stored session price. Other edits retain inventory and advance versions. | Sorted auditorium locks, shared event lock, tracked version-checked deletes/updates and one transaction. No price override is accepted; nonuniform stored prices prevent a move. |
| BR-19 | Layout edits/deletion are blocked by either a referencing session header or inventory referencing the auditorium’s physical seats. Session edits are blocked by hold/booking headers or items referencing its inventory, regardless of history status or current seat availability. | HasLayoutReferences under the auditorium lock; HasReservationReferences after inventory locks. Prevents further invalidation when independent FKs permit inconsistent cross-row relationships. Rejections are 409 conflict with no writes. |
| BR-20 | Public GET /api/events is anonymous and returns only Published events. Text searches title/description case-insensitively as a literal substring, trimmed and at most 200 characters. Blank query means no text filter. | CatalogueEndpoints.GetEventsAsync; parameterized escaped PostgreSQL ILIKE. |
| BR-21 | Date is a UTC session-start day (inclusive midnight, exclusive next midnight). Date/venue filters require the same Scheduled session; unknown venue returns an empty page. Without these filters, Published events may have no sessions. | Correlated Sessions.Any and Auditorium.VenueId lookup; cancelled/completed sessions cannot satisfy filters. No implicit future-only condition. |
| BR-22 | Page defaults to 1 and pageSize to 20; integers must be positive, pageSize at most 100, and offset must fit Int32. Repeated/malformed parameters are rejected. Results sort by title then ID; filtered count and page share a snapshot. | CatalogueQuery.Bind/CatalogueQueryValidator validation; bounded SQL Skip/Take and RepeatableRead transaction. |
| BR-23 | Event details and event-session lists require a Published event; session details/maps additionally require Scheduled status. Missing/hidden records return the same 404. No implicit future-time filter applies. | GetEventAsync/GetEventSessionsAsync and shared PublicSessions query. |
| BR-24 | Event sessions reuse date/venue/page/pageSize rules, sort by StartsAt/ID, and share parent/count/page snapshot. Public session metadata includes stored UTC times and the joined auditorium/venue. | CatalogueQueryValidator/CatalogueQuery.Parse and ProjectSessions; scalar DTOs only. |
| BR-25 | Seat maps expose the selected session’s complete persisted inventory, coordinates, type, price/currency, status and fencing version; no reservation owner or history data. Mismatched/incomplete maps are 409; maps use a snapshot and no-store. | GetSessionSeatsAsync joins physical seats, checks owning auditorium/count, and returns SessionSeatMapResponse. |
| BR-26 | Paged event/session responses retain items/page/pageSize/totalCount and add totalPages/hasPreviousPage/hasNextPage. Navigation is based on filtered total count, including empty/out-of-range pages; shared integer arithmetic does not overflow Int32-sized counts. | Contracts.PagedResponse<T>, concrete EventListResponse/EventSessionListResponse; contract and HTTP boundary tests. |
| BR-27 | Successful public responses are explicit Contracts DTOs; EF/domain entities, navigation graphs and IQueryable values are not response contracts. | TypedResults/Results<Ok<ConcreteDto>,…> handler signatures, DTO projections and exact JSON/endpoint-metadata tests. |
| BR-28 | Public query/Admin body inputs use registered FluentValidation validators; failures preserve validation_failed and existing camelCase/indexed field names. Admin authorization precedes validation. | CatalogueValidation.ValidateRequest<T>, CatalogueQueryValidator and ApiProblemDetails.Validation. |
| BR-29 | Input validators do not replace existence, ownership, duplicate-coordinate, state, or post-lock deadline rules. OpenAPI documents applicable 400/401/403/404/409/415/500 ProblemDetails responses with safe examples. | Handler transactions remain authoritative; endpoint metadata and ProblemDetailsOperationTransformer generate documentation. |
| BR-12 | EndsAt is derived from event duration; every seat starts Available at version 0 with validated server Money copied from the Admin's configured session price. | Session/SessionSeat constructors and one database transaction. Submitted endsAt/status/inventory fields are ignored. |

Names/title are capped at 200 characters, address 500, row 20, description 10000 at the HTTP boundary; these are API validation limits, not new database constraints. No auditorium-overlap rule or cleaning buffer is introduced. Admin role does not bypass customer ownership on separate workflows.

## Development sample rules

| Rule | Policy | Enforcement |
| --- | --- | --- |
| BR-30 | Sample data is explicit and Development-only; normal startup does not seed or create users. | Program command dispatch; DevelopmentSampleDataCommand and DevelopmentSampleDataSeeder guards. |
| BR-31 | Reserved root IDs identify sample venue/auditorium/movie without matching or overwriting unrelated names. Existing nonempty layouts and future inventory are preserved; archived/inconsistent sample graphs are refused. | DevelopmentSampleDataSeeder; command uses one transaction and a seed advisory lock. |
| BR-32 | If no future Scheduled sample session exists, capture a new start about one UTC day ahead using injected TimeProvider, event duration and independent 150 UAH Available/version-zero inventory. Do not move historical sessions or rewrite reservations. | DevelopmentSampleDataSeeder.SeedAsync; auditorium/event lock order matches Admin scheduling. |

## Seat-map cache rules

- SC-01: Cache only successful public maps meeting the configured size threshold; keep small/error responses and all commands uncached (`GetSessionSeatsAsync`, `IsUseful`).
- SC-02: Validate visibility, physical count and inventory revision from one database snapshot before a cached response (`GetSessionSeatsAsync`).
- SC-03: Every supported inventory commit selects a new revision; rollbacks preserve it. Expiration time alone never releases inventory (`SessionSeatAllocationWriter`, tracked Version advancement).
- SC-04: Redis failures must fall back to PostgreSQL; short TTL reclaims old generations without extending holds (`RedisSeatAvailabilityCache`). See [the invalidation table](seat-availability-caching.md).
