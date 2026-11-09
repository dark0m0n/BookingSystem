# Data model

All public properties are nonnullable and read-only to callers; private setters support EF materialization. IDs and foreign IDs are Guid, must be nonempty, and are supplied by callers. Relationships are scalar identifiers without navigation properties or database constraints.

| Entity and source | Fields beyond Id | Validation/defaults |
| --- | --- | --- |
| [Venue](../../../src/TicketBooking.Domain/Venue/Venue.cs) | Name: string; Address: string | Both nonblank. |
| [Auditorium](../../../src/TicketBooking.Domain/Venue/Auditorium.cs) | VenueId: Guid; Name: string | Name nonblank. |
| [Seat](../../../src/TicketBooking.Domain/Venue/Seat.cs) | AuditoriumId: Guid; Row: string; Number: int; Type: SeatType | Row nonblank (supports letter/numeric labels); Number > 0; Type defaults Standard. |
| [Event](../../../src/TicketBooking.Domain/Catalogue/Event.cs) | Title: string; Description: string; Duration: TimeSpan; AgeRating: int; Status: EventStatus | Title nonblank; description may be empty; duration > 0; age >= 0; status defaults Draft. |
| [Session](../../../src/TicketBooking.Domain/Catalogue/Session.cs) | EventId: Guid; AuditoriumId: Guid; StartsAt/EndsAt: DateTimeOffset; Status: SessionStatus | EndsAt > StartsAt by instant; both values normalized to UTC offset 00:00; status defaults Scheduled. |
| [SessionSeat](../../../src/TicketBooking.Domain/Catalogue/SessionSeat.cs) | SessionId: Guid; SeatId: Guid; Price: Money; Status: SessionSeatStatus; Version: long | Money requires explicit UAH/EUR/USD currency, nonnegative decimal(18,2), and at most two effective fractional digits (free seats allowed); Version >= 0, defaults 0; status defaults Available. |

Enums have explicit integer values starting at zero:

- SeatType: Standard=0, Premium=1, Accessible=2 (provisional).
- EventStatus: Draft=0, Published=1, Archived=2 (provisional).
- SessionStatus: Scheduled=0, Cancelled=1, Completed=2 (provisional).
- SessionSeatStatus: Available=0, Held=1, Booked=2, Unavailable=3 (specified vocabulary).

Event.UpdateDetails changes descriptive fields and a defined status without imposing a one-way lifecycle. Existing Session values are independent captures and are not recomputed by event edits. Version is now an application-managed EF concurrency token; tracked saves and explicit allocation writes compare the observed token and advance it. See [concurrency](../session-seat-concurrency/FEATURE.md). Price follows the [Money policy](../money-and-pricing/FEATURE.md); excess precision is rejected rather than silently rounded. Relational uniqueness constraints now exist in the persistence layer; aggregate graph consistency still requires application validation.

These domain declarations now have [EF mappings and a schema migration](../local-persistence/data-model.md), including auditorium seat-coordinate and session-seat uniqueness plus required foreign keys. Actual database state is Not verified. PII/retention policies are not established by this model.

## Admin write contract

Admin routes persist these existing six mapped entities; no schema change is required. CreateSessionAsync derives EndsAt from the stored Event.Duration, captures StartsAt/EndsAt in UTC at PostgreSQL microsecond precision and inserts one new SessionSeat per selected auditorium Seat. Every initial price is an independent Money copy, status Available and Version 0. It accepts no inventory IDs, owner, status or end-time override. Session.UpdateSchedule preserves Id/status and validates new event/auditorium IDs and end-after-start before assigning UTC times. Session PATCH persists no new fields. Auditorium moves replace SessionSeats using version-checked tracked deletes, retaining their stored uniform price in independent Money copies. Schedule-only edits retain seats but increment every version; GET returns a repeatable-read snapshot. Holds/bookings block schedule edits so their captured deadlines are not recalculated. Event.UpdateDetails preserves Id and validates title, description, duration, age rating and status before assigning any fields. Admin event updates lock and change only the Events row; existing session timestamps and inventory are retained. Auditorium.Rename changes only Name. Seat.UpdateLayout changes Row/Number/Type after validating all values; Id and AuditoriumId remain fixed. Uniqueness is scoped to (AuditoriumId, Row, Number), with case-sensitive C collation on Row. Admin edit queries exclude the current seat and trim rows before persistence. Physical layouts become immutable through Admin operations after any session references them; direct SQL remains outside this application guard.

## Reservation references during management

Independent FKs do not prove SessionSeats.SeatId belongs to Sessions.AuditoriumId, or that a history item belongs to its header’s SessionId. Management therefore follows both scalar headers and item/physical-seat links before editing. Any committed hold/booking reference, in any status, blocks session changes. A physical seat referenced by inventory freezes its auditorium’s layout. These checks preserve existing snapshots and return 409 for conflicts rather than relying on a restrictive FK failure during deletion. No schema changes are introduced and inconsistent data is not repaired automatically.

## Public listing projection

EventListResponse contains Items (EventSummaryResponse[]), Page, PageSize, TotalCount, TotalPages, HasPreviousPage and HasNextPage. Each item exposes Id, Title, Description, integer DurationMinutes and AgeRating; published state is filtered rather than exposed as a mutable public field. No inventory, reservation, Identity or owner data is returned. Filters read Events.Status and Sessions.EventId/Status/StartsAt/AuditoriumId → Auditoriums.VenueId. Venue/date filtering uses EXISTS, so multiple qualifying sessions do not duplicate events. No new tables or migrations are needed.

## Public detail projections

Event details reuse EventSummaryResponse. EventSessionListResponse exposes Items (SessionDetailsResponse[]), Page, PageSize, TotalCount, TotalPages, HasPreviousPage and HasNextPage. SessionDetailsResponse exposes Id, EventId/EventTitle, AuditoriumId/AuditoriumName, VenueId/VenueName/VenueAddress, UTC StartsAt/EndsAt and integer Status. Only Scheduled sessions of Published events are visible.

SessionSeatMapResponse contains SessionId, AuditoriumId and Seats. SeatMapSeatResponse contains Id (session inventory ID), SeatId (physical seat ID), Row, Number, integer Type, decimal Price, Currency, integer Status and long Version. Status values are Available=0, Held=1, Booked=2, Unavailable=3. Version is the stored allocation fencing token, not ownership proof. SeatHold/Booking/Identity entities, owner IDs, history, totals and expiry metadata are never returned. A seat map must cover exactly the session auditorium’s physical seats; mismatches are 409 rather than partial maps. No schema change is needed.

Both list envelopes inherit the shared read-only PagedResponse<T> contract. TotalPages uses ceiling division of TotalCount by PageSize with a long intermediate. Empty results have zero total pages; HasPreviousPage is Page > 1 and HasNextPage is Page < TotalPages even when Items is empty. Derived values are computed from constructor-bound count/page inputs rather than separate mutable fields. This is a response-only change with no persistence model or migration changes.

## Validation models and errors

Request Contracts remain dependency-free. CatalogueQuery is an API-only raw input record retaining Query/Date/VenueId/Page/PageSize and repeated parameter names; it is parsed only after FluentValidation succeeds. Validators add no stored data. HttpValidationProblemDetails carries errors as a string-to-string-array map with keys such as title, price, seats[0].row, event or session. code remains validation_failed; instance/traceId and urn:ticketbooking:error type follow existing diagnostics. No schema or migration change is required.

## Development sample markers

The seed uses reserved existing entity IDs: Venue 8d6c73e5-447b-4480-83de-000000000001, Auditorium 8d6c73e5-447b-4480-83de-000000000002 and Event 8d6c73e5-447b-4480-83de-000000000003. It adds no columns/tables. First use creates rows A/B/C with numbers 1–8, eight Premium seats in B and two Accessible seats at C1/C2 (others Standard). The Published fictional movie is 120 minutes, age 12. Sessions/inventory receive fresh GUIDs and independent Money(150, UAH) captures. Whole-minute UTC starts derive from injected current time plus one day. An existing future session/layout is reused unchanged; absent future sessions cause additive new rows while historical IDs and deadlines remain intact.

Seat-map caching adds no SQL schema. Redis stores public SessionSeatMapResponse plus a short absolute expiry under a key containing session/auditorium IDs and a SHA-256 inventory ID/version revision. Existing SessionSeat.Version remains the source of freshness; cached rows contain no hold/booking/customer ownership data. Old generations expire rather than being enumerated/deleted. See [key and TTL](seat-availability-caching.md).
