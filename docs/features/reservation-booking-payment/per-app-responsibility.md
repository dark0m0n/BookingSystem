# Application responsibilities

The API owns the explicit AdminBookingEndpoints management read and authorization; Contracts owns a separate admin summary/envelope including UserId. Infrastructure owns the global (CreatedAt, Id) index. Domain transitions, customer ownership checks and payment/provider integrations are unchanged.

The API owns booking details/history routes, pagination validation, caller-only query filtering, ownership checks and summary/detail serialization. Contracts owns BookingResponse, BookingSummaryResponse and the shared paginated envelope. Infrastructure owns the owner/creation/ID index. Read workflows neither call Redis nor perform lifecycle transitions.

The API hosts the bounded BookingExpiryWorker and singleton batch processor; Infrastructure owns the scoped BookingExpiryReleaser transaction and retained-token writes. Domain Booking.Expire enforces the stored payment deadline independently of cancellation eligibility. System cleanup does not use HTTP caller identity or payment provider outcomes.

Infrastructure owns the unique source-hold index, restrictive reference and migration. Domain captures SourceHoldId from the supplied hold, while the conversion command retains ownership/deadline validation and transaction locks. API continues to return a conflict for repeated conversion.

- **TicketBooking.Api** owns the customer hold HTTP workflow: authenticated caller resolution, request validation, eligibility checks, ordered fenced allocations, aggregate persistence in one PostgreSQL transaction, owner-only reads, atomic cancellation/release, bounded expiry polling/selection with fresh scopes, and DTO/error serialization. BookingEndpoints also owns POST /api/bookings validation, server-generated identity/number and BookingResponse serialization, delegating conversion to HoldConversionService. Program registers the routes, validators, clock, policy and writer. Admin roles use their own identity under the established Customer policy.
- **TicketBooking.Application** provides ICurrentUser and ResourceOwnershipService; no database dependencies are introduced there.
- **TicketBooking.Domain.Reservation** owns hold validity, snapshots, totals, expiry/state/owner rules, one-time allocation-version capture, and owner-safe repeated cleanup. It does not mutate inventory or booking aggregates.
- **TicketBooking.Domain.Catalogue** owns session and inventory state/price representation.
- **TicketBooking.Domain.Booking/Payment** own their existing domain lifecycle rules; booking creation is exposed by the API; payment HTTP workflows remain absent.
- **TicketBooking.Infrastructure** owns EF mappings/migrations, PostgreSQL storage, Identity stores, SessionSeatAllocationWriter version/status predicates, the transactional HoldExpiryReleaser, and HoldConversionService orchestration using Application ownership checks.

All modules remain in the existing application and assembly dependency structure. The API hosts the hold expiry worker. No frontend, provider adapter, messaging integration or refund flow is added. Redis is used for disposable active hold response snapshots.

Application defines IHoldCache; Infrastructure owns Redis serialization, TTL and failure handling. API creation/lookup and persistence lifecycle services call that contract. Redis owns disposable cached copies only; PostgreSQL remains authoritative for ownership, deadlines, state and inventory.
