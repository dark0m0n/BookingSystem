# Known gotchas

- **Observed:** Constructors reject empty IDs but do not check foreign-object existence, seat/auditorium agreement, or duplicate seats. Cross-object invariants need application and persistence enforcement when implemented.
- **Observed:** Available/Held/Booked/Unavailable can be selected at construction; there are no transition methods or hold/booking ownership checks. Do not treat constructing Held as a successful reservation.
- **Observed:** Version is now a nonnegative EF concurrency token with tracked-save and explicit allocation-write increments. See [concurrency](../session-seat-concurrency/FEATURE.md). It does not replace durable owner links or a transaction spanning hold/booking/seat changes.
- **Observed:** Public properties remain read-only, with private setters/constructors for EF rehydration. Persistence mappings and real database round-trip tests now exist; application update workflows remain absent.
- **Observed:** Price now uses explicit-currency Money with decimal(18,2) limits and no silent rounding. Additional currencies, taxes, discounts, and provider settlement still need explicit integration policies; see [Money](../money-and-pricing/FEATURE.md).
- **Unverified policy:** Seat types, Event/Session status values, integer minimum-age interpretation, and local validation ranges are implementation choices. Confirm or revise them before exposing a public contract.

Domain constructor behavior is tested separately; database round trips, API behavior, lifecycle transitions, and cross-request consistency are not verified or implemented by this feature.
