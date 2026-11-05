# Integration points

| Direction | Interface/payload | Authority and failure behavior |
| --- | --- | --- |
| Booking factory → SeatHold | Internal EnsureCanConvertAt(userId, now) with the factory's UTC clock reading; immutable Items/SessionId. | Reservation owns validity; invalid owner/state/time rejects. In-process read, no aggregate mutation or retry. |
| Future Application → Reservation/Booking/Payment | Domain constructors and lifecycle methods. | Caller supplies trusted identity/policy, passes the injected TimeProvider, and establishes cross-object consistency. No implemented handlers or transaction. |
| Items → Catalogue | SessionSeatId and captured PriceSnapshot. | Catalogue owns inventory/current price; snapshots become historical facts. No availability/session-membership lookup in constructors. |
| Payment → booking identity | BookingId and Amount, stored without lookup. | Booking owns its total/status. Future Application must verify matching amount, supported state, and provider signature/outcome. |

[Venue/Catalogue model](../venue-catalogue-model/FEATURE.md), [domain boundaries](../domain-boundaries/FEATURE.md), and [local persistence](../local-persistence/FEATURE.md) describe neighboring code. These are code dependencies and planned coordination boundaries, not external network integrations.

Same-instance payment replays do not provide API key uniqueness or protect provider calls. Future persistence must scope/enforce payment idempotency and ensure duplicate webhooks cannot create repeated effects. No key-scope or provider-retry policy is invented here.

Plan sequencing: phase 6 adds RabbitMQ/MassTransit, OutboxMessage, and ProcessedMessage; phase 7 adds notifications/cancellation application policy and Refund processing. Their entity schemas/workers/events are intentionally not implemented now. Changing snapshot or transition contracts can affect those later consumers.
