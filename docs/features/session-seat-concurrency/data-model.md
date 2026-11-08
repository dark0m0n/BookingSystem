# Data model

SessionSeat.Version remains a nonnullable long/bigint with a nonnegative SQL CHECK and default domain value 0. It is an EF concurrency token with ValueGenerated.Never; the application advances it, not a sequence/trigger/xmin mapping. Increment beyond long.MaxValue is rejected rather than wrapped.

Status and identifiers are unchanged. Explicit allocation updates use:

```text
WHERE Id = seatId AND Version = expectedVersion AND Status = expectedStatus
SET Status = targetStatus, Version = expectedVersion + 1
```

Tracked UPDATE/DELETE use the original Version as EF's optimistic-concurrency predicate. All tracked Modified updates advance it, including price changes or an explicit same-status Modified write. A hold→booking transfer must still perform a row write even though Held stays Held; omitting that touch would leave the old fencing token valid.

[AddSessionSeatOptimisticConcurrency](../../../src/TicketBooking.Infrastructure/Persistence/Migrations/20261105144851_AddSessionSeatOptimisticConcurrency.cs) adds the nonnegative CHECK; its snapshot also captures the concurrency metadata. Existing Version values are preserved. Customer hold creation now stores the returned claim token in SeatHoldItem.AllocationVersion, with a restrictive item-to-hold/inventory relationship. AddHoldItemAllocationVersion adds that positive-or-null bigint column without fabricating tokens for legacy rows. BookingItem now retains the successful transfer token. Booking source-hold replay correlation, pending-booking release and confirming-payment associations remain future work.

Migration execution is verified only in isolated test schemas/database. Developer/deployment migration state is Not verified. No customer PII or retention-policy change is introduced.

SeatHold.Status is an EF concurrency token, so stale tracked transitions include the observed status in their predicate. AddBookingItemAllocationVersion records that model metadata without altering the Status column and adds nullable positive-or-null BookingItem allocation tokens for transferred ownership. Lock-based commands remain primary; concurrency failures must not be blindly retried.
