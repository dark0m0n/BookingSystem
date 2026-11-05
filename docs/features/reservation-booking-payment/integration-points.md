# Integration points

| Direction | Interface/payload | Authority and failure behavior |
| --- | --- | --- |
| Booking factory → SeatHold | Internal EnsureCanConvertAt(userId, now) with the factory's UTC clock reading; immutable Items/SessionId. | Reservation owns validity; invalid owner/state/time rejects. In-process read, no aggregate mutation or retry. |
| Future Application → Reservation/Booking/Payment | Domain constructors and lifecycle methods. | Caller supplies trusted identity/session-start data, passes injected BookingTimingPolicy/TimeProvider at creation, and establishes cross-object consistency. No implemented handlers or transaction. |
| Items → Catalogue | Selected SessionSeat objects, SessionSeatId, and captured Money PriceSnapshot. | Catalogue owns inventory/current price; snapshots become historical facts. Constructor checks local availability/session membership; no authoritative repository lookup in constructors. Row updates now have [version fencing](../session-seat-concurrency/FEATURE.md); full allocation transactions remain absent. |
| Payment → booking identity | CreateForBooking reads the PendingPayment booking ID and server-calculated Money total. | Booking owns its total/status. Factory verifies PendingPayment and its captured payment deadline using the clock, and derives amount/currency. Future Application must verify persisted authority and provider-reported amount/currency/signature/outcome. |

[Venue/Catalogue model](../venue-catalogue-model/FEATURE.md), [domain boundaries](../domain-boundaries/FEATURE.md), and [local persistence](../local-persistence/FEATURE.md) describe neighboring code. These are code dependencies and planned coordination boundaries, not external network integrations.

Same-instance payment replays do not provide API key uniqueness or protect provider calls. Future persistence must scope/enforce payment idempotency and ensure duplicate webhooks cannot create repeated effects. No key-scope or provider-retry policy is invented here. The [retention/late-payment decision](../booking-timing/seat-retention-and-late-payments.md) requires owner-checked release and durable idempotent compensation; no worker, reconciliation record, or refund execution exists yet.

Plan sequencing: phase 6 adds RabbitMQ/MassTransit, OutboxMessage, and ProcessedMessage; phase 7 adds notifications/cancellation application policy and Refund processing. Their entity schemas/workers/events are intentionally not implemented now. Changing snapshot or transition contracts can affect those later consumers.
