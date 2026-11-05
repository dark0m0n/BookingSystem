# Known gotchas

- **Observed:** Constructors reject empty IDs but do not check foreign-object existence, seat/auditorium agreement, or duplicate seats. Cross-object invariants need application and persistence enforcement when implemented.
- **Observed:** Available/Held/Booked/Unavailable can be selected at construction; there are no transition methods or hold/booking ownership checks. Do not treat constructing Held as a successful reservation.
- **Observed:** Version is a nonnegative long, not an active concurrency mechanism. Do not infer double-booking protection or automatic increments.
- **Observed:** Get-only properties prevent ordinary caller mutation. Persistence mapping and update workflows still need explicit design; no EF compatibility test or mappings were added.
- **Inferred-risk:** Decimal price without currency/precision rules is insufficient for payment settlement. Resolve currency and rounding before integration.
- **Unverified policy:** Seat types, Event/Session status values, integer minimum-age interpretation, and local validation ranges are implementation choices. Confirm or revise them before exposing a public contract.

Domain constructor behavior is tested separately; database round trips, API behavior, lifecycle transitions, and cross-request consistency are not verified or implemented by this feature.
