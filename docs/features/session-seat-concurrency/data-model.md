# Data model

SessionSeat.Version remains a nonnullable long/bigint with a nonnegative SQL CHECK and default domain value 0. It is an EF concurrency token with ValueGenerated.Never; the application advances it, not a sequence/trigger/xmin mapping. Increment beyond long.MaxValue is rejected rather than wrapped.

Status and identifiers are unchanged. Explicit allocation updates use:

```text
WHERE Id = seatId AND Version = expectedVersion AND Status = expectedStatus
SET Status = targetStatus, Version = expectedVersion + 1
```

Tracked UPDATE/DELETE use the original Version as EF's optimistic-concurrency predicate. All tracked Modified updates advance it, including price changes or an explicit same-status Modified write. A hold→booking transfer must still perform a row write even though Held stays Held; omitting that touch would leave the old fencing token valid.

[AddSessionSeatOptimisticConcurrency](../../../src/TicketBooking.Infrastructure/Persistence/Migrations/20261105144851_AddSessionSeatOptimisticConcurrency.cs) adds the nonnegative CHECK; its snapshot also captures the concurrency metadata. Existing Version values are preserved. No allocation-owner IDs, item claim-version fields, source-hold relationship, or confirming-payment association are added. Those durable links remain future application/schema work.

Migration execution is verified only in isolated test schemas/database. Developer/deployment migration state is Not verified. No customer PII or retention-policy change is introduced.
