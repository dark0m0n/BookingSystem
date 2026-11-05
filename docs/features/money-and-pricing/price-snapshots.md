# Price snapshot contract

Status: **implemented for domain construction and EF persistence**; public catalogue/checkout handlers and atomic allocation workflows remain absent. Working tree inspected with the existing Money, persistence, timing, and concurrency changes. No new schema fields or migration are required for this change.

## Capture points and authority

| Value | Capture rule | Later catalogue price/currency changes |
| --- | --- | --- |
| SessionSeat.Price | Independent copy of its supplied Money value for current inventory. | Can change through a version-checked tracked catalogue update; does not share the input object with another row. |
| SeatHoldItem.PriceSnapshot | Copy amount and currency from the selected server-resolved SessionSeat when creating the hold. | Cannot reprice the existing item or hold total. |
| SeatHold.TotalAmount | Sum the captured item values in their one currency, returning a separate Money instance even for one item. | Remains based on the hold's original prices. |
| BookingItem.PriceSnapshot | Copy amount and currency from the valid hold item at conversion; never read current SessionSeat.Price. | Remains the original hold price, even if catalogue currency changed before conversion. |
| Booking.TotalAmount | Sum copied booking item snapshots, not a current-inventory join or request total. | Remains fixed for that booking. |
| Payment.Amount | Independent copy of the booking's captured total. | Remains the amount/currency for that attempt; no catalogue repricing. |

The amounts remain decimal(18,2), with explicit supported currency and no silent rounding or conversion. Mixed-currency holds reject at creation. A later catalogue currency edit does not convert an existing hold/booking into a new currency.

Prices are guaranteed by the hold only while it remains eligible for conversion. Expired/cancelled/converted holds cannot create a new booking through the factory. A customer who needs a new hold receives current server-resolved prices; an existing hold is never silently repriced. A successful conversion carries the agreed snapshots into the booking beyond the original hold expiry. Current allocation/version checks can still reject a conversion attempt independently of its historical prices; snapshots do not bypass availability or deadline rules.

## Why values are copied

Money is publicly immutable, but private setters let EF materialize and update mapped complex values. Sharing one Money object between SessionSeat.Price, item snapshots, totals, and payment amounts would allow a tracked complex-property edit to change several objects in memory and potentially persist unintended repricing.

The public constructors of [SeatHoldItem](../../../src/TicketBooking.Domain/Reservation/SeatHoldItem.cs) and [BookingItem](../../../src/TicketBooking.Domain/Booking/BookingItem.cs) therefore create new Money values from Amount/Currency. SessionSeat and Payment capture independent values too. [Money.Sum](../../../src/TicketBooking.Domain/Money.cs) starts with a copy instead of returning a single item's reference. Public setters, repricing methods, and mutable item collections are not exposed.

## Storage and query rules

SeatHoldItems and BookingItems each store their own PriceSnapshot numeric(18,2) and Currency varchar(3). Parent totals and Payment.Amount are also stored separately. SessionSeatId is an identity/reference link, not an instruction to recalculate historical prices. Future booking/history queries must return those stored snapshots/totals rather than joining current inventory prices for financial amounts.

Cancellation does not erase price history. Any later refund implementation must use the recorded charge amount/currency, not a catalogue price at refund time. No refund, tax, discount, exchange, retroactive price-adjustment, or snapshot-edit workflow is added.

These guarantees cover the normal creation/update paths. Arbitrary raw SQL or deliberate EF writes to historical fields can still change them; no database immutability trigger or automatic repair of manually edited history is introduced. Future handlers must load authoritative inventory and coordinate allocation/booking transactions; this feature does not create those handlers.

## Verification

[PostgresPersistenceTests.CatalogueRepricing_DoesNotChangeHoldBookingOrPaymentSnapshots](../../../tests/TicketBooking.IntegrationTests/PostgresPersistenceTests.cs) covers one-seat and two-seat holds. It changes the actual tracked catalogue amount/currency before booking creation and again after payment creation, saves each change, clears tracking, and reloads all records. Current inventory changes to USD while hold/booking snapshots, totals, and payment retain the original UAH values. The two-seat case also verifies independent catalogue rows initialized from a common Money value.

Existing domain tests verify price calculation, currency rejection, and capture from inventory. Real database tests require TICKETBOOKING_TEST_CONNECTION_STRING pointing to a disposable PostgreSQL database; CI supplies it. No live development data or real payment provider is used.
