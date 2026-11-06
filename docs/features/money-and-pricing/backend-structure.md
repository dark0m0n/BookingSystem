# Backend structure

[Money.cs](../../../src/TicketBooking.Domain/Money.cs) defines a sealed record with publicly read-only Amount/Currency and private EF setters, Precision=18, Scale=2, and MaxAmount. Add validates matching currencies and constructs a checked result. Sum starts with a separate value copy, including one-element totals, and requires a nonempty sequence to establish currency; nulls, incompatible currencies, and invalid totals are rejected.

[SeatHold](../../../src/TicketBooking.Domain/Reservation/SeatHold.cs) now accepts IEnumerable<SessionSeat>, not caller-created SeatHoldItems. It checks local inventory eligibility, builds immutable Money snapshots, and calculates TotalAmount. This does not load inventory or mutate availability.

[Booking](../../../src/TicketBooking.Domain/Booking/Booking.cs) uses Money.Sum on copied snapshots; no total parameter or setter exists. [Payment](../../../src/TicketBooking.Domain/Payment/Payment.cs) has a private constructor and public CreateForBooking factory, derives Amount from Booking.TotalAmount, and rejects non-pending or payment-overdue bookings using an injected clock. Provider/lifecycle methods are otherwise unchanged.

[MoneyTests](../../../tests/TicketBooking.Domain.Tests/MoneyTests.cs) covers precision/range, currencies, exact arithmetic, complete price flow, historical snapshots, unavailable inventory, invalid totals, and payment eligibility. Existing constructor/lifecycle/time tests use explicit Money values.

[Admin session creation](../venue-catalogue-model/admin-http-contract.md) now accepts validated price/currency, resolves the stored auditorium layout and captures a separate Money value in each generated SessionSeat, committing all rows with the session. It requires the Admin policy. Customer hold/checkout routes, provider requests, discounts, tax calculations and conversion rates remain absent. The [persistence layer](../local-persistence/FEATURE.md) now maps Money as a shared complex value and supplies its migration. All work stays in the existing projects without extra packages.

[Snapshot capture](price-snapshots.md) now copies values at SessionSeat, SeatHoldItem, BookingItem, and Payment construction so private EF complex-property edits cannot mutate another stage through a shared Money reference. PostgreSQL regressions edit tracked catalogue amount/currency, save/reload the history, and cover single-item and multiple-item totals. No schema changes are needed; the existing separate snapshot/total columns are used.

MoneyTests.PricePipeline_PreservesExactBoundaryTotalsAndIndependentSnapshots adds full inventory→hold→booking→payment checks for a free single seat, repeated one-cent seats, and a split total exactly at Money.MaxAmount, for each supported currency. It asserts exact expected totals and independent snapshot/total instances, including the single-item case. The expanded domain suite passed 159 tests without a database.
