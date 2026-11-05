# End-to-end flow

Trigger: application/test code constructs the initial model. There is no implemented HTTP entry point.

1. Construct `Venue`, then `Auditorium` with its VenueId, then `Seat` with AuditoriumId (`venue-create`, `auditorium-create`, `seat-create`; BR-01/02/03/06).
2. Construct `Event` with its descriptive fields, duration, age label, and status (`event-create`; BR-01/02/03/06).
3. Construct `Session` with EventId, AuditoriumId, and offset-aware start/end times (`session-create`; BR-01/04/06).
4. Construct `SessionSeat` with SessionId, SeatId, Price, Status, and Version (`session-seat-create`; BR-01/03/05). Default status is Available and version is zero.
5. The caller receives objects with get-only scalar properties. Relationships are IDs; constructors do not load or validate referenced objects.

Invalid inputs throw ArgumentException (including argument-null exceptions) or ArgumentOutOfRangeException before construction completes. No partial object is returned or persisted. See [entry-points.json](entry-points.json) for exact source anchors.

No write transactions, async boundaries, retries, confirmations, expiry, cancellation, booking transitions, or handoffs are implemented. Held/Booked values can be supplied at construction; this is representation, not proof that a hold or booking exists. No method advances Version.
