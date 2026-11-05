# Data model

All IDs are nonempty Guid. Required text is nonblank. Monetary values use immutable Money: nonnegative decimal(18,2), explicit UAH/EUR/USD currency, no mixed-currency sums or silent rounding. See [money/pricing](../money-and-pricing/data-model.md). Stored timestamps are DateTimeOffset normalized to UTC offset 00:00. Public properties have no public setters. Private constructors/setters and private item lists support EF materialization without exposing mutation to callers.

| Type | Fields and constraints |
| --- | --- |
| SeatHold | Id, SessionId, UserId: Guid; ExpiresAt: UTC DateTimeOffset calculated from hold duration and capped at future session start; Status: SeatHoldStatus, initially Active; Items: read-only, nonempty SeatHoldItem list derived from selected inventory; TotalAmount: Money calculated from snapshots. |
| SeatHoldItem | SeatHoldId, SessionSeatId: Guid; PriceSnapshot: independent amount/currency Money copy. Hold creates parent IDs itself and validates unique selected SessionSeatId values, session membership, and availability. No extra item ID. |
| Booking | Id, UserId, SessionId: Guid; Number: string; Status: BookingStatus, initially PendingPayment; TotalAmount: Money calculated from snapshots; CreatedAt: single UTC clock reading shared with hold validation; SessionStartsAt, PaymentDeadline, CancellationDeadline: get-only captured UTC DateTimeOffset values; Items: read-only BookingItem list copied from the hold. |
| BookingItem | BookingId, SessionSeatId: Guid; PriceSnapshot: independent Money copy from the hold without consulting current seat price. No extra item ID. |
| Payment | Id, BookingId: Guid; ProviderReference: nullable string until provider correlation is known; Amount: Money derived from a pending booking total; Status: PaymentStatus, initially Pending; IdempotencyKey: required string. |

Explicit enum values:

- SeatHoldStatus: Active=0, Expired=1, Converted=2, Cancelled=3.
- BookingStatus: PendingPayment=0, Confirmed=1, Cancelled=2, Expired=3, Refunded=4.
- PaymentStatus: Pending=0, Succeeded=1, Failed=2, Refunded=3.

Refunded values are reserved with no public transition; no Refund entity exists. No OutboxMessage or ProcessedMessage entities exist. State transitions are listed in [end-to-end-flow.md](end-to-end-flow.md).

SessionSeatId references Catalogue inventory; UserId is an identity reference, not an implemented Identity user. The [persistence layer](../local-persistence/data-model.md) now maps item navigations, required foreign keys, composite item keys, booking numbers, and scoped payment identities. Booking captures PaymentDeadline, CancellationDeadline, and SessionStartsAt from policy at construction; no caller-supplied deadline override is accepted by transitions. HoldId/allocation-owner correlation remains future work; captured deadline/snapshot fields are now mapped.

These snapshots and totals use separate persisted amount/currency columns; [PostgreSQL repricing tests](../../../tests/TicketBooking.IntegrationTests/PostgresPersistenceTests.cs) verify they survive changes to tracked inventory. No additional migration is needed for value-copy isolation; development/deployed data is Not verified. No retention or customer-data policy is established by declaring UserId; provider references and idempotency keys should not be treated as authentication credentials.
