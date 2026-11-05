# Data model and constraints

Source: [configurations](../../../src/TicketBooking.Infrastructure/Persistence/Configurations), [snapshot](../../../src/TicketBooking.Infrastructure/Persistence/Migrations/TicketBookingDbContextModelSnapshot.cs), and [schema migration](../../../src/TicketBooking.Infrastructure/Persistence/Migrations/20261105143133_AddDomainForeignKeysAndUniqueConstraints.cs). Migration application/round trips were verified only in a disposable PostgreSQL 17 database; development/deployment state is Not verified.

## Tables and foreign keys

All Id and reference columns are nonnull uuid unless noted. Every entity Id is caller-supplied; there is no database-generated identifier. Status/type enums are integer columns.

| Table | Key and references | Other mapped fields |
| --- | --- | --- |
| Venues | PK Id | Name, Address: required text. |
| Auditoriums | PK Id; VenueId → Venues.Id | Name: required text. |
| Seats | PK Id; AuditoriumId → Auditoriums.Id | Row: required text/C collation; Number/Type: integer. |
| Events | PK Id | Title/Description: required text; Duration: interval; AgeRating/Status: integer. |
| Sessions | PK Id; EventId → Events.Id; AuditoriumId → Auditoriums.Id | StartsAt/EndsAt: timestamptz; Status. |
| SessionSeats | PK Id; SessionId → Sessions.Id; SeatId → Seats.Id | Price: numeric(18,2); Currency: varchar(3); Status; Version: bigint. |
| SeatHolds | PK Id; SessionId → Sessions.Id | UserId: uuid reference without Identity FK; ExpiresAt: timestamptz; Status; TotalAmount/Currency. |
| SeatHoldItems | PK (SeatHoldId, SessionSeatId); both columns FK to SeatHolds/SessionSeats | PriceSnapshot/Currency. |
| Bookings | PK Id; SessionId → Sessions.Id | UserId without Identity FK; Number: varchar(128); Status; CreatedAt/SessionStartsAt/PaymentDeadline/CancellationDeadline: timestamptz; TotalAmount/Currency. |
| BookingItems | PK (BookingId, SessionSeatId); both columns FK to Bookings/SessionSeats | PriceSnapshot/Currency. |
| Payments | PK Id; BookingId → Bookings.Id | Amount/Currency; Status; IdempotencyKey: varchar(256); ProviderReference: nullable varchar(256). |

All monetary pairs have required numeric(18,2) amount and varchar(3) currency columns. Domain Money validates supported currency, nonnegative amount, and scale; the database mapping does not add those domain checks as SQL CHECK constraints. Direct SQL fractional inputs can be coerced by numeric scale, so do not bypass server Money validation.

All thirteen FKs use ON DELETE RESTRICT. Parent rows cannot be removed while referenced; application code must explicitly manage deletion/retention rather than relying on cascades.

## Uniqueness scopes

| Constraint/index | Columns | Scope |
| --- | --- | --- |
| IX_Seats_AuditoriumId_Row_Number | AuditoriumId, Row, Number | Exact coordinates unique within one auditorium; other auditoriums may reuse them. |
| IX_SessionSeats_SessionId_SeatId | SessionId, SeatId | One inventory row per session/physical-seat pair; a seat may appear in different sessions. |
| PK_SeatHoldItems / PK_BookingItems | Parent ID, SessionSeatId | No duplicate item within one parent; historical parents may reference the same inventory row. |
| IX_Bookings_Number | Number | Globally unique booking number. |
| IX_Payments_BookingId_IdempotencyKey | BookingId, IdempotencyKey | Same key cannot create two attempts for one booking; other bookings may reuse the key. |
| IX_Payments_ProviderReference | ProviderReference, WHERE nonnull | One payment per known provider operation; pending null references may repeat. |

Booking Number, payment keys/references, and Row use C collation for exact case-sensitive comparison. Row labels are not normalized; A and a are different coordinates. Number/key/reference lengths are storage limits; request-level length validation is not implemented.

Foreign keys prove reference existence only: they do not prove the session and seat share an auditorium, or that an item belongs to its parent's session. User existence, allocation ownership, one active hold per seat, state transitions, payment/booking amount agreement, and sum/currency consistency across rows require future application/schema work. Version is now an application-managed EF concurrency token with a nonnegative CHECK. Tracked updates and the explicit allocation writer advance it and compare the observed value; see [concurrency](../session-seat-concurrency/FEATURE.md). Arbitrary raw SQL/set-based operations are not protected automatically.
