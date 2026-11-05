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

No lifecycle transition methods exist. Version is now an application-managed EF concurrency token; tracked saves and explicit allocation writes compare the observed token and advance it. See [concurrency](../session-seat-concurrency/FEATURE.md). Price follows the [Money policy](../money-and-pricing/FEATURE.md); excess precision is rejected rather than silently rounded. Relational uniqueness constraints now exist in the persistence layer; aggregate graph consistency still requires application validation.

These domain declarations now have [EF mappings and a schema migration](../local-persistence/data-model.md), including auditorium seat-coordinate and session-seat uniqueness plus required foreign keys. Actual database state is Not verified. PII/retention policies are not established by this model.
