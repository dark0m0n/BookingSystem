# Session-seat optimistic concurrency

Status: **partial**. Versioned row updates/deletes, explicit fenced allocation writes and HTTP conflict mapping are implemented and tested. Full hold/booking transactions, durable allocation-owner links and expiry workers remain absent.

Working tree inspected on top of `5ba7f54`, including uncommitted persistence/timing/Money work. Trusted callers can claim inventory, transfer a held allocation, confirm it, or release it without silently overwriting a newer row version. All logic stays in the existing projects/application.

## Acceptance and verification

- SessionSeat.Version is an application-managed long EF concurrency token; SQL updates/deletes check the originally loaded value.
- All tracked SessionSeat updates advance Version once per save; explicit allocation writes compare ID/version/status and advance it in the same statement.
- Held → Held transfer advances Version, preventing stale original-hold release and release/reacquire races.
- PostgreSQL tests cover 20 competing claims (exactly one winner), transfer fencing, confirmation/release races, tracked writes/deletes, batch rollback, and stale tracking. No automatic retry or token refresh occurs.

Run `dotnet test TicketBooking.slnx --configuration Release` with TICKETBOOKING_TEST_CONNECTION_STRING targeting an isolated PostgreSQL database for actual concurrency tests. They otherwise skip; metadata/input-validation tests run without it. CI already supplies PostgreSQL. Schema tests create/drop uniquely named schemas and require schema-creation privileges.

See [persistence](../local-persistence/FEATURE.md) for local startup/migration instructions and [seat retention](../booking-timing/seat-retention-and-late-payments.md) for allocation policy versus remaining workflow gaps.

## Navigation

- [Business context and rules](business-context.md)
- [End-to-end flow](end-to-end-flow.md)
- [Entry points](entry-points.json)
- [Application responsibilities](per-app-responsibility.md)
- [Frontend structure](frontend-structure.md)
- [Backend structure](backend-structure.md)
- [Data model](data-model.md)
- [Integration points](integration-points.md)
- [Known gotchas](known-gotchas.md)

## Related decision

See the [seat ownership, price history and transition decision record](../../decisions/0001-seat-ownership-price-history-and-transitions.md) for rationale, enforcement boundaries and remaining transaction work.
