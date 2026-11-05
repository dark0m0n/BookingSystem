# End-to-end flow

1. Load SessionSeat and retain the version observed for the intended allocation. Do not replace an old owner's token with a current public availability version merely to force a write (BR-02/06).
2. Choose a tracked EF update or an explicit allocation operation:
   - `allocation-hold`: Available → Held.
   - `allocation-transfer`: Held → Held, advancing the token for booking ownership.
   - `allocation-confirm`: Held → Booked.
   - `allocation-release`: Held/Booked → Available/Unavailable; caller supplies policy-approved source/target states.
3. Each explicit operation updates where Id, Version, and expected Status all match, setting new Status and Version=expected+1 in one SQL statement. It returns the new version; zero matches throws a concurrency exception (BR-04/05).
4. For tracked changes, `tracked-save` detects modifications and sets candidate Version=OriginalValue+1. EF retains the original version in its WHERE clause. Tracked deletes likewise compare original Version but need not increment a removed row (BR-02/03).
5. On conflict, no automatic retry/reload occurs. A future application transaction must roll back its other hold/booking/inventory changes. EF's implicit SaveChanges transaction rolls back a conflicting multi-row tracked save; multiple explicit writer calls require a caller-owned transaction.

A failed tracked save retains the old original token and the same candidate next value on retry, so retrying does not accidentally overwrite the winner. Bulk writer operations do not synchronize ChangeTracker: subsequent saves of a stale tracked seat conflict rather than silently refreshing its token.

PostgreSQL tests exercise competing rows/transactions only. No user HTTP entry, ownership-record transfer, booking confirmation transaction, expiry worker, or provider workflow is added. Existing TimeProvider deadline checks must still be applied by future callers after obtaining appropriate database access.
