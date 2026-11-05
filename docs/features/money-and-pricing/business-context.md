# Business context and rules

Users must not determine checkout totals or charge amounts by submitting different values. The project plan also requires historical price snapshots. Present implementation is domain code for trusted server callers; no customer HTTP request path exists yet.

| Rule | Current policy | Enforcement |
| --- | --- | --- |
| BR-01 | Money uses decimal precision 18, scale 2; values range from 0 to 9,999,999,999,999,999.99 inclusive. | Money constructor and all summed results. |
| BR-02 | Currency must be explicit and one of UAH, EUR, USD; normalize whitespace/case. | Money constructor; no default currency parameter. Supported currencies are a scoped implementation choice. |
| BR-03 | Nonzero fractional digits beyond two are rejected, never silently rounded. | Money constructor checks rounded equality; equivalent trailing zeros are accepted. |
| BR-04 | A total contains one currency, even when an item is free. | Money.Add/Sum; hold total calculation. |
| BR-05 | Hold prices come from selected session inventory; snapshots remain historical. | SeatHold accepts SessionSeat objects, checks session/availability/duplicates, and constructs its own items. |
| BR-06 | Booking and payment callers cannot supply independent totals/amounts. | Booking factory sums snapshots; Payment.CreateForBooking derives Amount from a pending booking. |
| BR-07 | Price snapshots and totals capture separate amount/currency values; later tracked catalogue edits cannot change history. | Item constructors, SessionSeat/Payment construction, and Money.Sum copy values rather than sharing complex-value instances. |

Zero-price seats/payments remain representable, without defining a free-checkout workflow. No taxes, fees, discounts, exchange rates, fractional-cent allocation, or refund policy is introduced. Authorization, real inventory loading, and persistent request deduplication remain Application/API responsibilities.
