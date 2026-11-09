# Application responsibilities

- **TicketBooking.Api** owns the customer hold HTTP workflow: authenticated caller resolution, request validation, eligibility checks, ordered fenced allocations, aggregate persistence in one PostgreSQL transaction, owner-only reads, atomic cancellation/release, bounded expiry polling/selection with fresh scopes, and DTO/error serialization. Program registers the route, validators, clock, policy and writer. Admin roles use their own identity under the established Customer policy.
- **TicketBooking.Application** provides ICurrentUser and ResourceOwnershipService; no database dependencies are introduced there.
- **TicketBooking.Domain.Reservation** owns hold validity, snapshots, totals, expiry/state/owner rules, one-time allocation-version capture, and owner-safe repeated cleanup. It does not mutate inventory or booking aggregates.
- **TicketBooking.Domain.Catalogue** owns session and inventory state/price representation.
- **TicketBooking.Domain.Booking/Payment** own their existing domain lifecycle rules; their HTTP workflows remain absent.
- **TicketBooking.Infrastructure** owns EF mappings/migrations, PostgreSQL storage, Identity stores, SessionSeatAllocationWriter version/status predicates, the transactional HoldExpiryReleaser, and HoldConversionService orchestration using Application ownership checks.

All modules remain in the existing application and assembly dependency structure. The API hosts the hold expiry worker. No frontend, provider adapter, messaging integration or refund flow is added. Redis is used for disposable active hold response snapshots.

Application defines IHoldCache; Infrastructure owns Redis serialization, TTL and failure handling. API creation/lookup and persistence lifecycle services call that contract. Redis owns disposable cached copies only; PostgreSQL remains authoritative for ownership, deadlines, state and inventory.
