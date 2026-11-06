# Backend structure

[TicketBookingDbContext](../../../src/TicketBooking.Infrastructure/Persistence/TicketBookingDbContext.cs) inherits IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>, calls the base Identity model configuration, exposes eleven business DbSets plus Identity sets, and loads business IEntityTypeConfiguration classes through ApplyConfigurationsFromAssembly. [Configurations](../../../src/TicketBooking.Infrastructure/Persistence/Configurations) contain explicit table names, primary/foreign keys, required columns, indexes, and money facets.

[SeatConfiguration](../../../src/TicketBooking.Infrastructure/Persistence/Configurations/SeatConfiguration.cs) defines unique auditorium/row/number coordinates with C collation on Row. [SessionSeatConfiguration](../../../src/TicketBooking.Infrastructure/Persistence/Configurations/SessionSeatConfiguration.cs) defines the session/seat unique pair. [PaymentConfiguration](../../../src/TicketBooking.Infrastructure/Persistence/Configurations/PaymentConfiguration.cs) defines booking-scoped idempotency and nonnull provider-reference uniqueness. All thirteen business FKs use Restrict deletion; Identity dependent FKs use the framework Cascade behavior.

Money is an explicitly mapped EF complex value, not an owned entity: snapshots/totals are independently copied values without a separate entity identity. Do not share EF-mutable Money instances between capture stages. [MoneyMapping](../../../src/TicketBooking.Infrastructure/Persistence/Configurations/MoneyMapping.cs) maps Amount to numeric(18,2) and Currency to required varchar(3). See [EF complex-type documentation](https://learn.microsoft.com/en-us/ef/core/modeling/complex-types).

Private constructors/setters allow materialization without invoking clocks or policy calculations. SeatHold/Booking item navigations use private List fields; public access returns read-only views. Their materialized totals/deadlines are stored values, not recalculated using current policy or catalogue prices.

Tests: [PersistenceModelTests](../../../tests/TicketBooking.IntegrationTests/PersistenceModelTests.cs) validates all entity/FK/index/money metadata without a database; [PostgresPersistenceTests](../../../tests/TicketBooking.IntegrationTests/PostgresPersistenceTests.cs) applies the migration and verifies actual round trips and PostgreSQL constraint failures. CI passes TICKETBOOKING_TEST_CONNECTION_STRING from its dedicated PostgreSQL service. These constraint tests do not implement web handlers or transactions for customer use cases. [SessionSeatConcurrencyTests](../../../tests/TicketBooking.IntegrationTests/SessionSeatConcurrencyTests.cs) now verify the version-fenced writer, tracked updates/deletes, and PostgreSQL races; see [concurrency](../session-seat-concurrency/FEATURE.md).

[Identity persistence](../identity-persistence/FEATURE.md) adds GUID user/role stores and Identity tables to this same context. Tests use the production AddTicketBookingIdentity extension to verify persisted hashes, lookups, roles/claims and concurrency. Business UserId FKs are deferred to a compatibility/backfill decision.

RefreshSession/RefreshToken mappings and RefreshTokenService now persist renewal families/history in the shared context. Rotation locks the family row (SELECT FOR UPDATE), checks eligibility after the lock, then consumes/inserts in one transaction. Replay commits family revocation before the endpoint returns 401; a failed replacement insert rolls back consumption. No expiry/history cleanup worker is added.

## Venue and catalogue configurations

All six are IEntityTypeConfiguration implementations discovered by the shared context:

- [VenueConfiguration](../../../src/TicketBooking.Infrastructure/Persistence/Configurations/VenueConfiguration.cs): Venues table, caller-supplied key and required name/address.
- [AuditoriumConfiguration](../../../src/TicketBooking.Infrastructure/Persistence/Configurations/AuditoriumConfiguration.cs): Auditoriums table, required name and restrictive VenueId reference.
- [SeatConfiguration](../../../src/TicketBooking.Infrastructure/Persistence/Configurations/SeatConfiguration.cs): Seats table, required row, restrictive auditorium reference and unique auditorium/row/number coordinates.
- [EventConfiguration](../../../src/TicketBooking.Infrastructure/Persistence/Configurations/EventConfiguration.cs): Events table, required title/description; duration, age rating and enum status use provider scalar conventions.
- [SessionConfiguration](../../../src/TicketBooking.Infrastructure/Persistence/Configurations/SessionConfiguration.cs): Sessions table with restrictive event/auditorium references; UTC starts/ends and enum status use provider scalar conventions.
- [SessionSeatConfiguration](../../../src/TicketBooking.Infrastructure/Persistence/Configurations/SessionSeatConfiguration.cs): SessionSeats table, required Money complex value, restrictive session/seat references, unique session/seat pair and application-managed concurrency token.

The existing schema migration and snapshot include these fields; adding a duplicate table-creation or empty migration is unnecessary. The model-snapshot check and four-state six-entity round-trip test now explicitly guard this requested mapping scope.
