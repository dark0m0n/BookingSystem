# PostgreSQL catalogue HTTP coverage

Status: implemented and PostgreSQL-tested; working tree inspected. Tests use actual PostgreSQL and Kestrel HTTP, not an in-memory EF provider. Each AuthEndpointsTests case migrates a uniquely named schema, configures a schema-specific Npgsql data source, injects a controllable clock and drops its schema after disposal.

| Area | Behavioral coverage | Test evidence |
| --- | --- | --- |
| Filters | Published-only visibility; case-insensitive trimmed literal title/description search; escaped wildcard/SQL-like text; UTC day bounds; date/venue on one Scheduled session; no duplicate events; combined filters before paging/count. | [PublicEvents](../../../tests/TicketBooking.IntegrationTests/AuthEndpointsTests.PublicEvents.cs), [CatalogueRegression](../../../tests/TicketBooking.IntegrationTests/AuthEndpointsTests.CatalogueRegression.cs). |
| Pagination | Defaults and 1–100 bounds; stable title/ID or time/ID ordering; filtered totals; totalPages/navigation flags; empty, last and beyond-end pages; Int32-safe offsets. | [PublicEvents](../../../tests/TicketBooking.IntegrationTests/AuthEndpointsTests.PublicEvents.cs), [PublicDetails](../../../tests/TicketBooking.IntegrationTests/AuthEndpointsTests.PublicDetails.cs), [ResponseContracts](../../../tests/TicketBooking.IntegrationTests/AuthEndpointsTests.ResponseContracts.cs). |
| Invalid input | Malformed/repeated/oversized filters; simultaneous field errors; invalid dates/GUIDs/prices/enums; indexed seat errors; malformed/null JSON and unsupported media; stable ProblemDetails with no partial writes; unsupported Accept still receives problem JSON. | [Validation](../../../tests/TicketBooking.IntegrationTests/AuthEndpointsTests.Validation.cs), [Administration](../../../tests/TicketBooking.IntegrationTests/AuthEndpointsTests.Administration.cs), [CatalogueRegression](../../../tests/TicketBooking.IntegrationTests/AuthEndpointsTests.CatalogueRegression.cs). |
| Admin permissions | All 18 Admin routes reject anonymous, Customer-only, roleless, tampered-role/signature and expired tokens before writes. Trusted server-side Admin assignment permits creation. Role and lifetime claims come from real issued JWTs; roles are changed only through Identity managers in fixtures. | Shared AdministrationRequests in [Administration](../../../tests/TicketBooking.IntegrationTests/AuthEndpointsTests.Administration.cs), extended matrix in [CatalogueRegression](../../../tests/TicketBooking.IntegrationTests/AuthEndpointsTests.CatalogueRegression.cs). |
| Seat maps | Exact DTO fields; physical/inventory identifiers; ordered coordinates/types; all four persisted statuses and version tokens; independent prices across sessions; free/one-cent/maximum prices in public responses; guest/Customer parity; owner-data exclusion; no-store; hidden/missing/inconsistent maps; snapshots during moves. Reading at stored hold expiry must not release seats or rewrite history. | [PublicDetails](../../../tests/TicketBooking.IntegrationTests/AuthEndpointsTests.PublicDetails.cs), [ResponseContracts](../../../tests/TicketBooking.IntegrationTests/AuthEndpointsTests.ResponseContracts.cs), [CatalogueRegression](../../../tests/TicketBooking.IntegrationTests/AuthEndpointsTests.CatalogueRegression.cs). |

Existing event/session/seat management suites additionally cover parent relationships, safe-edit restrictions, immutable used layouts, rollback and version-fenced allocation races. The new tests add seven cases without changing application behavior. Inconsistent/history fixtures and time travel are confined to isolated test schemas.

## Running

Set `TICKETBOOKING_TEST_CONNECTION_STRING` to an isolated PostgreSQL database permitting schema creation, using the existing CI/local test setup. Do not point tests at development or production data.

```bash
dotnet test tests/TicketBooking.IntegrationTests/TicketBooking.IntegrationTests.csproj --filter 'FullyQualifiedName~CatalogueRegression_'
dotnet test tests/TicketBooking.IntegrationTests/TicketBooking.IntegrationTests.csproj --filter 'FullyQualifiedName~AuthEndpointsTests'
```

The first command runs the seven additions; the second runs the shared HTTP fixture's full test set, including authentication. Without the connection setting, PostgreSQL test attributes skip; a passing run with skips is not evidence of database behavior. Current execution uses a separate disposable PostgreSQL 17 container and leaves the seeded local Compose database untouched.

## Latest execution

Seven new regressions passed with zero skips. The combined run selects all cases from PublicEvents, PublicDetails, Administration, ResponseContracts, Validation, CatalogueRegression, SeatLayouts, Events and Sessions and emits a TRX report. The combined run passed **142 cases across 58 methods**, with 142 executed/passed, zero failed and zero not-executed/skipped, verified from the generated TRX counters. The seven new cases are included in that count; no unrelated full-solution test result is claimed for this run. Formatting and documentation checks passed.
