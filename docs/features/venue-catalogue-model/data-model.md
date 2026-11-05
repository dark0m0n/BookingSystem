# Data model

All properties are nonnullable and get-only. IDs and foreign IDs are Guid, must be nonempty, and are supplied by callers. Relationships are scalar identifiers without navigation properties or database constraints.

| Entity and source | Fields beyond Id | Validation/defaults |
| --- | --- | --- |
| [Venue](../../../src/TicketBooking.Domain/Venue/Venue.cs) | Name: string; Address: string | Both nonblank. |
| [Auditorium](../../../src/TicketBooking.Domain/Venue/Auditorium.cs) | VenueId: Guid; Name: string | Name nonblank. |
| [Seat](../../../src/TicketBooking.Domain/Venue/Seat.cs) | AuditoriumId: Guid; Row: string; Number: int; Type: SeatType | Row nonblank (supports letter/numeric labels); Number > 0; Type defaults Standard. |
| [Event](../../../src/TicketBooking.Domain/Catalogue/Event.cs) | Title: string; Description: string; Duration: TimeSpan; AgeRating: int; Status: EventStatus | Title nonblank; description may be empty; duration > 0; age >= 0; status defaults Draft. |
| [Session](../../../src/TicketBooking.Domain/Catalogue/Session.cs) | EventId: Guid; AuditoriumId: Guid; StartsAt/EndsAt: DateTimeOffset; Status: SessionStatus | EndsAt > StartsAt by instant; both values normalized to UTC offset 00:00; status defaults Scheduled. |
| [SessionSeat](../../../src/TicketBooking.Domain/Catalogue/SessionSeat.cs) | SessionId: Guid; SeatId: Guid; Price: decimal; Status: SessionSeatStatus; Version: long | Price >= 0 (free seats allowed); Version >= 0, defaults 0; status defaults Available. |

Enums have explicit integer values starting at zero:

- SeatType: Standard=0, Premium=1, Accessible=2 (provisional).
- EventStatus: Draft=0, Published=1, Archived=2 (provisional).
- SessionStatus: Scheduled=0, Cancelled=1, Completed=2 (provisional).
- SessionSeatStatus: Available=0, Held=1, Booked=2, Unavailable=3 (specified vocabulary).

No lifecycle transition methods exist. Version is not an EF concurrency token and does not auto-increment. Price has no currency or rounding policy. No uniqueness constraints or aggregate graph consistency checks exist.

These are domain declarations, not mapped database tables. DbContext, initial migration, and model snapshot are unchanged and contain no mapped entities. Actual database state is Not verified. PII/retention policies are not established by this model.
