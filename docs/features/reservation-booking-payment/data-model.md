# Data model

AdminBookingListResponse derives from shared PagedResponse<AdminBookingSummaryResponse>. Admin summaries contain booking headers plus the stored UserId; customer DTOs continue to omit ownership IDs. Source-hold links, claim tokens, account/payment fields and seat items are excluded. IndexAdminBookingHistory adds (CreatedAt, Id) for global newest-first reads; no columns/data are changed.

BookingListResponse derives from shared PagedResponse<BookingSummaryResponse>. Summaries contain stored booking headers/deadlines/totals without nested items or internal ownership/token fields; details reuse BookingResponse. IndexBookingHistoryByOwner adds (UserId ASC, CreatedAt DESC, Id DESC), with no column or data changes. Read transactions preserve one count/page or header/item snapshot and never modify state.

IndexPendingBookingExpirations adds the nonunique (PaymentDeadline, Id) index filtered to Booking.Status = 0 and captures Booking.Status concurrency metadata in the snapshot (no new status column). Expiry changes only PendingPayment → Expired and inventory status/version. Booking/source-hold deadlines, prices, totals and captured claim tokens remain historical. Payment outcomes are untouched.

POST /api/bookings accepts only CreateBookingRequest.HoldId and returns BookingResponse with generated Id/Number, SessionId, numeric Status, four timestamp snapshots, TotalAmount/Currency and item SessionSeatId/Price/Currency. Stored owner IDs and allocation versions are excluded. EnforceUniqueBookingSourceHold adds nullable SourceHoldId, a unique index and a restrictive SeatHolds foreign key. New factory-created bookings always capture their hold ID; legacy rows remain null without inferred backfill. See [HTTP contract](booking-http-contract.md).

All IDs are nonempty Guid. Required text is nonblank. Monetary values use immutable Money: nonnegative decimal(18,2), explicit UAH/EUR/USD currency, no mixed-currency sums or silent rounding. See [money/pricing](../money-and-pricing/data-model.md). Stored timestamps are DateTimeOffset normalized to UTC offset 00:00. Public properties have no public setters. Private constructors/setters and private item lists support EF materialization without exposing mutation to callers.

| Type | Fields and constraints |
| --- | --- |
| SeatHold | Id, SessionId, UserId: Guid; ExpiresAt: UTC DateTimeOffset calculated from hold duration and capped at future session start; Status: SeatHoldStatus, initially Active; Items: read-only, nonempty SeatHoldItem list derived from selected inventory; TotalAmount: Money calculated from snapshots. |
| SeatHoldItem | SeatHoldId, SessionSeatId: Guid; PriceSnapshot: independent amount/currency Money copy; AllocationVersion: nullable long, positive when captured once from the allocation writer. Hold creates parent IDs itself and validates unique selected SessionSeatId values, session membership, and availability. No extra item ID. |
| Booking | Id, UserId, SessionId: Guid; SourceHoldId: nullable Guid (populated by every new factory call); Number: string; Status: BookingStatus, initially PendingPayment; TotalAmount: Money calculated from snapshots; CreatedAt: single UTC clock reading shared with hold validation; SessionStartsAt, PaymentDeadline, CancellationDeadline: get-only captured UTC DateTimeOffset values; Items: read-only BookingItem list copied from the hold. |
| BookingItem | BookingId, SessionSeatId: Guid; PriceSnapshot: independent Money copy from the hold without consulting current seat price; AllocationVersion: nullable positive long captured once from the successful transfer. No extra item ID. |
| Payment | Id, BookingId: Guid; ProviderReference: nullable string until provider correlation is known; Amount: Money derived from a pending booking total; Status: PaymentStatus, initially Pending; IdempotencyKey: required string. |

Explicit enum values:

- SeatHoldStatus: Active=0, Expired=1, Converted=2, Cancelled=3.
- BookingStatus: PendingPayment=0, Confirmed=1, Cancelled=2, Expired=3, Refunded=4.
- PaymentStatus: Pending=0, Succeeded=1, Failed=2, Refunded=3.

Refunded values are reserved with no public transition; no Refund entity exists. No OutboxMessage or ProcessedMessage entities exist. State transitions are listed in [end-to-end-flow.md](end-to-end-flow.md).

SessionSeatId references Catalogue inventory; UserId is a GUID identity reference; [Identity accounts](../identity-persistence/FEATURE.md) now exist in Infrastructure, but business UserId foreign keys/backfill remain absent. The [persistence layer](../local-persistence/data-model.md) now maps item navigations, required foreign keys, composite item keys, booking numbers, and scoped payment identities. Booking captures PaymentDeadline, CancellationDeadline, and SessionStartsAt from policy at construction; no caller-supplied deadline override is accepted by transitions. Hold items link their hold to inventory and new HTTP-created items retain AllocationVersion. Booking source-hold correlation is stored; payment/confirmation correlation remains future work; captured deadline/snapshot fields are mapped.

These snapshots and totals use separate persisted amount/currency columns; [PostgreSQL repricing tests](../../../tests/TicketBooking.IntegrationTests/PostgresPersistenceTests.cs) verify they survive changes to tracked inventory. No additional migration is needed for value-copy isolation; development/deployed data is Not verified. No retention or customer-data policy is established by declaring UserId; provider references and idempotency keys should not be treated as authentication credentials.

Reusable [customer ownership checks](../identity-persistence/resource-ownership.md) now resolve hold/booking UserId and payment ownership through a matching booking. Hold/booking transitions share the same Domain ownership rule. No refund model is added. The customer hold creation route uses these ownership checks.

AddHoldItemAllocationVersion adds nullable bigint AllocationVersion plus CK_SeatHoldItems_AllocationVersion_Positive. Existing hold items preserve their historical values with a null token; null means unknown and must never authorize a future release or transfer using a refreshed inventory version. Down drops the check and column. New HTTP-created items have a captured positive token. Apply the checked-in migration chain with the existing README startup procedure before running this endpoint; development/deployed migration state is not verified by isolated tests.

Owner GET/DELETE introduce no schema changes. DELETE preserves the hold, ExpiresAt, item snapshots and original AllocationVersion values; Status becomes Cancelled only when all releases commit. Inventory versions advance on release, fencing old claims. Null legacy item tokens remain readable by the owner but cannot authorize release. GET returns stored Status rather than deriving/overwriting it from wall time.

IndexActiveHoldExpirations adds a nonunique (ExpiresAt, Id) index on SeatHolds filtered to Status = 0 (Active). No data/owner/token backfill occurs. Worker expiry changes only hold Status and retained-version inventory status/version; snapshots, total, original expiry, user/session IDs and claim tokens remain historical. Cursor state is process-local, not persisted. Migration/schema verification is limited to isolated PostgreSQL tests.

AddBookingItemAllocationVersion adds nullable bigint AllocationVersion plus CK_BookingItems_AllocationVersion_Positive; legacy booking items remain null, never assigned a refreshed token. Conversion captures new transfer versions on booking items without changing the source hold's tokens. SeatHold.Status gains EF concurrency metadata (no new database column), so tracked writes compare the original state. EnforceUniqueBookingSourceHold subsequently adds SourceHoldId and IX_Bookings_SourceHoldId, preventing multiple bookings for one known hold even through bypass writers. The hold lock still serializes normal conversion; repeated conversion returns a conflict. No API replay key or replay-success lookup is added.

Redis stores disposable HoldResponse JSON plus provider expiry metadata under `hold:{holdId:D}`. Original expiry, server totals and item snapshots are copied; owner IDs and allocation versions remain only in PostgreSQL. Fixed absolute expiration uses the stored deadline; terminal/elapsed responses are removed. No new SQL fields or migration are introduced. See [payload and TTL](hold-caching.md).
