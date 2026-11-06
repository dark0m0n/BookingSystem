# Development sample catalogue

Implemented as an explicit command; normal API startup never seeds sample data.

After configuring the existing local connection and applying migrations:

```bash
dotnet run --project src/TicketBooking.Api --no-launch-profile -- --environment Development --seed-sample-data
```

First use creates:

- **Sample Cinema**, at the fictional address **1 Sample Street, Kyiv**.
- **Sample Auditorium**, with 24 seats: rows A–C, numbers 1–8. Row B is Premium; C1/C2 are Accessible; the other seats are Standard.
- **Sample Movie: Beyond the Horizon**, represented by the existing Event type, Published, 120 minutes, minimum age label 12.
- A Scheduled session approximately one day ahead, captured in UTC at whole-minute precision, plus one independent Available/version-zero inventory row per seat at **150 UAH**.

The command prints venue/auditorium/movie/session IDs and the UTC start time, then exits. It needs ConnectionStrings:DefaultConnection through existing configuration, but no JWT signing key or Admin account. Other environments fail before persistence; the seeder also enforces the Development restriction. Combining this switch with --initialize-admin is rejected. No users or roles are created.

Use the returned IDs with GET /api/events/{id}, GET /api/events/{id}/sessions and GET /api/sessions/{id}/seats after starting the API normally. GET /api/events?query=Sample also finds the Published movie. These reads are anonymous.

## Reruns and existing data

Fixed root IDs identify only this sample venue, auditorium and movie. Other names/records are untouched. A transaction-scoped advisory lock serializes concurrent seed runs; all graph writes commit or roll back together. After creating missing roots, the seeder locks the auditorium then the event, matching the Admin scheduling order.

Existing nonempty layouts and sample names/metadata are preserved. An existing future Scheduled session with matching inventory is returned unchanged, including prices, availability/version tokens and reservations. If none remains, a fresh future session/inventory is added; older sessions and captured hold/booking history are never moved or rewritten. This also means a previously booked future sample session is not reset for demo convenience.

An empty unused layout can be initialized. An empty used auditorium, changed root auditorium/venue relationship, archived/draft sample movie or incomplete future inventory is refused. Repair through an appropriate explicit management operation; the command does not republish, reset, release seats, truncate data or recreate the database. Failures return a nonzero exit with a generic diagnostic, without printing connection strings/provider exceptions. Migrations are an operator prerequisite, not a seed side effect.

Evidence: [command](../../../src/TicketBooking.Api/Development/DevelopmentSampleDataCommand.cs), [seeder](../../../src/TicketBooking.Api/Development/DevelopmentSampleDataSeeder.cs), [result](../../../src/TicketBooking.Api/Development/DevelopmentSampleDataResult.cs), [tests](../../../tests/TicketBooking.IntegrationTests/DevelopmentSampleDataTests.cs). Tests use a controllable clock and isolated PostgreSQL schemas; deployed data is not inferred from tests.
