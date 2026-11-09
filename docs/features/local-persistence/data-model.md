# Data model and constraints

Source: [configurations](../../../src/TicketBooking.Infrastructure/Persistence/Configurations), [snapshot](../../../src/TicketBooking.Infrastructure/Persistence/Migrations/TicketBookingDbContextModelSnapshot.cs), and [schema migration](../../../src/TicketBooking.Infrastructure/Persistence/Migrations/20261105143133_AddDomainForeignKeysAndUniqueConstraints.cs). Migration application/round trips were verified only in a disposable PostgreSQL 17 database; development/deployment state is Not verified.

## Business tables and foreign keys

All Id and reference columns are nonnull uuid unless noted. Every business entity Id is caller-supplied; there is no database-generated identifier. Status/type enums are integer columns.

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

All thirteen business FKs use ON DELETE RESTRICT. Parent rows cannot be removed while referenced; application code must explicitly manage deletion/retention rather than relying on cascades.

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

## Identity tables

The shared context now also stores seven framework Identity entities: users, roles, user roles, user claims, role claims, external logins and user tokens. Six required dependent FKs cascade; user/role IDs are GUIDs, and claim IDs are database-generated integers. Normalized username/role indexes are unique; EmailIndex is now unique after AddRegistrationProfileAndUniqueEmail; AspNetUsers also has required varchar(200) DisplayName. See [Identity data model](../identity-persistence/data-model.md) and [AddAspNetCoreIdentity](../../../src/TicketBooking.Infrastructure/Persistence/Migrations/20261105162749_AddAspNetCoreIdentity.cs). Business UserId foreign keys remain absent. The [registration migration](../../../src/TicketBooking.Infrastructure/Persistence/Migrations/20261105163800_AddRegistrationProfileAndUniqueEmail.cs) gives existing rows an empty display name and requires resolving any duplicate normalized emails before its unique index can be applied.

## Refresh credentials

The [refresh migration](../../../src/TicketBooking.Infrastructure/Persistence/Migrations/20261105165909_AddRefreshTokenRotation.cs) adds RefreshSessions (GUID key, required cascading UserId, security-stamp hash, UTC CreatedAt/ExpiresAt/RevokedAt) and RefreshTokens (GUID key, cascading SessionId, unique TokenHash, UTC CreatedAt/ConsumedAt). A filtered unique SessionId index permits one unconsumed generation; consumed history remains for replay detection. No raw credential is stored. The context now has twenty entities and twenty-one FKs: thirteen business Restrict, eight Identity/refresh Cascade. See the [Identity data model](../identity-persistence/data-model.md) for exact columns, expiry precision and constraints.

## Built-in role data

SeedCustomerAndAdminRoles upserts Customer/CUSTOMER and Admin/ADMIN into AspNetRoles without changing tables or adding users/memberships. Existing normalized roles keep their IDs/UserRoles; names become canonical. Down retains role data to avoid deleting existing grants. Public registration alone adds Customer membership transactionally; existing roleless accounts are not backfilled.

## Venue/catalogue verification scope

The six-entity PostgreSQL round-trip test reloads Venue, Auditorium, Seat, Event, Session and SessionSeat after clearing EF tracking. It verifies all scalar values and parent IDs, Unicode text, nondefault enum values, interval duration, UTC-normalized session timestamps, numeric price/currency, initial concurrency version and each Available/Held/Booked/Unavailable inventory status. Existing tests separately verify uniqueness, restrictive deletion, foreign keys and concurrency. A metadata test asserts no pending model changes against the checked-in migration snapshot. No new migration or production schema change is introduced by this verification.

## Hold mapping verification scope

The hold round-trip test covers Active, Converted, Cancelled and Expired states; hold/session/user identifiers; the original UTC expiry; a 19.75 UAH total from 12.50 and 7.25 UAH items; each item's hold/inventory identifiers and price; and the read-only collection after materialization. Transitions use the domain APIs and a deterministic clock. Existing tests cover restrictive foreign keys, duplicate item keys, independent historical snapshots and migration/model consistency. This work retains the existing schema and intentionally introduces no duplicate or empty migration.

## Customer hold allocation versions

[AddHoldItemAllocationVersion](../../../src/TicketBooking.Infrastructure/Persistence/Migrations/20261107231200_AddHoldItemAllocationVersion.cs) adds nullable bigint SeatHoldItems.AllocationVersion and a positive-or-null check. Existing rows retain null (unknown) tokens; the customer hold endpoint captures returned claim versions for new items in the same transaction as inventory allocation. This additive migration follows the earlier mapping-only verification above. See the [hold HTTP contract](../reservation-booking-payment/hold-http-contract.md).

## Active-hold expiry scan index

[IndexActiveHoldExpirations](../../../src/TicketBooking.Infrastructure/Persistence/Migrations/20261107235332_IndexActiveHoldExpirations.cs) adds nonunique IX_SeatHolds_ExpiresAt_Id on (ExpiresAt, Id) filtered to Status = 0. It supports the bounded due-Active-hold keyset scan without changing history or ownership data. Model/index metadata, snapshot consistency and migration application are exercised by isolated PostgreSQL tests. The API does not apply this migration automatically.

## Booking transfer tokens and hold transition concurrency

[AddBookingItemAllocationVersion](../../../src/TicketBooking.Infrastructure/Persistence/Migrations/20261108003652_AddBookingItemAllocationVersion.cs) adds nullable bigint BookingItems.AllocationVersion with CK_BookingItems_AllocationVersion_Positive. Existing rows retain null/unknown ownership evidence. New HoldConversionService bookings capture returned Held → Held transfer versions independently of source hold tokens. The migration target/snapshot also records SeatHold.Status as an EF concurrency token (no column change): stale tracked writes compare original state and roll back on mismatch. See [cleanup/conversion rules](../reservation-booking-payment/hold-cleanup-conversion.md).

RedisCache timeout settings add no persistence fields, tables or migrations. Existing hold/seat-map payloads and deadlines/revisions are unchanged. Timeout/late-operation handling changes only cache access and transport configuration; PostgreSQL remains the source of truth. See [settings](redis-timeouts.md).
