# Money and server-calculated pricing

Status: **partial**. Money precision/currency and the domain pricing pipeline are implemented; HTTP contracts and authoritative inventory loading are absent. The [persistence mappings](../local-persistence/FEATURE.md) now store Money amount/currency columns.

Working tree inspected on top of commit `5ba7f54`. The requirement is explicit monetary precision/currency with totals calculated on the server. Trusted domain/application callers can price seats, snapshot a hold, create a booking, and derive a payment amount without passing a total or independent charge amount.

## Acceptance and verification

- Immutable Money carries decimal Amount and explicit Currency, with nonnegative decimal(18,2) limits and supported UAH/EUR/USD codes.
- Invalid scale/range and mixed-currency totals are rejected; no silent rounding, float arithmetic, default currency, or exchange conversion occurs.
- Hold/booking item snapshots capture independent amount/currency copies. Totals and payment amounts are derived from those values without aliasing or reading current catalogue prices; see the [snapshot contract](price-snapshots.md).
- MoneyTests covers exact sums, limits, currency handling, snapshot preservation, availability/session guards, and payment eligibility. Pricing pipeline cases include zero, repeated cents, and the maximum total in every supported currency; the expanded domain suite passed 159 tests.

Run `dotnet test tests/TicketBooking.Domain.Tests/TicketBooking.Domain.Tests.csproj --configuration Release` for domain checks. PostgreSQL integration tests additionally verify actual tracked repricing and reloaded historical values when TICKETBOOKING_TEST_CONNECTION_STRING targets an isolated database. The tests require no provider, database, or secrets. Existing [local startup](../../../README.md#local-startup) remains unchanged. No refund/messaging entities or services are added.

## Navigation

- [Business context and rules](business-context.md)
- [End-to-end pricing flow](end-to-end-flow.md)
- [Entry points](entry-points.json)
- [Application responsibilities](per-app-responsibility.md)
- [Frontend structure](frontend-structure.md)
- [Backend structure](backend-structure.md)
- [Data model](data-model.md)
- [Integration points](integration-points.md)
- [Known gotchas](known-gotchas.md)

- [Price snapshot contract and repricing verification](price-snapshots.md)

## Related decision

See the [seat ownership, price history and transition decision record](../../decisions/0001-seat-ownership-price-history-and-transitions.md) for rationale, enforcement boundaries and remaining transaction work.
