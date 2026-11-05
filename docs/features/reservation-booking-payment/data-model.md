# Data model

All IDs are nonempty Guid. Required text is nonblank. Monetary values are decimal >= 0 with no specified currency/rounding policy. Stored timestamps are DateTimeOffset normalized to UTC offset 00:00. Only status/provider-reference properties expose private setters; scalar identities and price snapshots are get-only.

| Type | Fields and constraints |
| --- | --- |
| SeatHold | Id, SessionId, UserId: Guid; ExpiresAt: UTC DateTimeOffset > injected clock reading at construction; Status: SeatHoldStatus, initially Active; Items: read-only, nonempty SeatHoldItem list. |
| SeatHoldItem | SeatHoldId, SessionSeatId: Guid; PriceSnapshot: decimal. Hold validates parent ID and unique SessionSeatId within its collection. No extra item ID. |
| Booking | Id, UserId, SessionId: Guid; Number: string; Status: BookingStatus, initially PendingPayment; TotalAmount: decimal calculated from snapshots; CreatedAt: single UTC clock reading shared with hold validation; Items: read-only BookingItem list copied from the hold. |
| BookingItem | BookingId, SessionSeatId: Guid; PriceSnapshot: decimal copied without consulting current seat price. No extra item ID. |
| Payment | Id, BookingId: Guid; ProviderReference: nullable string until provider correlation is known; Amount: decimal; Status: PaymentStatus, initially Pending; IdempotencyKey: required string. |

Explicit enum values:

- SeatHoldStatus: Active=0, Expired=1, Converted=2, Cancelled=3.
- BookingStatus: PendingPayment=0, Confirmed=1, Cancelled=2, Expired=3, Refunded=4.
- PaymentStatus: Pending=0, Succeeded=1, Failed=2, Refunded=3.

Refunded values are reserved with no public transition; no Refund entity exists. No OutboxMessage or ProcessedMessage entities exist. State transitions are listed in [end-to-end-flow.md](end-to-end-flow.md).

SessionSeatId references Catalogue inventory; UserId is an identity reference, not an implemented Identity user. No aggregate navigation graph, uniqueness constraints, or mapped foreign keys are added. Booking does not store HoldId or payment/cancellation deadlines because those fields are not in the supplied model; the future application/persistence design must establish durable correlation and authoritative policies.

EF model and migrations remain unchanged and empty. Actual database state is Not verified. No retention or customer-data policy is established by declaring UserId; provider references and idempotency keys should not be treated as authentication credentials.
