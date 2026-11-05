# Data model

| Member | Representation | Constraint/source |
| --- | --- | --- |
| Money.Amount | decimal | 0..9,999,999,999,999,999.99; at most two effective fractional digits. |
| Money.Currency | string | Required, normalized uppercase UAH/EUR/USD. Unsupported codes rejected. |
| SessionSeat.Price | Money | Current inventory price; required. |
| SeatHoldItem.PriceSnapshot | Money | Independent amount/currency copy from a selected SessionSeat. |
| SeatHold.TotalAmount | Money | Calculated independent Money sum of its snapshots; cannot be supplied. |
| BookingItem.PriceSnapshot | Money | Independent amount/currency copy from the hold without repricing. |
| Booking.TotalAmount | Money | Calculated independent Money sum of booking snapshots. |
| Payment.Amount | Money | Independent copy of the pending booking total. |

Money has value equality: normalized same-currency equivalent decimal values compare equally. It is a sealed reference record, avoiding a default struct with missing currency; null is rejected at monetary property boundaries. Private setters prevent caller mutation through record initializers while allowing EF materialization. Same-currency zero is supported; a currencyless/empty sum is not.

The [persistence mapping](../local-persistence/data-model.md) now uses required numeric(18,2) amount and varchar(3) currency columns. The schema migration was applied to an isolated test database; no SQL CHECK constraints for supported currency/nonnegative amounts or cross-row sums are added. Existing IDs, UTC timestamps, and status enums are unchanged. Actual database state is Not verified. The value contains no customer PII and defines no retention policy.

No monetary object is intentionally shared across catalogue rows, item snapshots, totals, or payment captures. See [snapshot contract](price-snapshots.md); copying values changes neither column names nor the migration model.
