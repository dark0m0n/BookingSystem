# Venue and catalogue domain model

Status: **partial**. Six domain entities and construction validation are implemented; persistence, API use cases, and state transitions are absent. Working tree inspected on top of commit `9f0a19d` and the uncommitted domain-boundary organization.

Contributors can represent a venue, auditorium, physical seat, event/movie, session/screening, and priced session seat. Canonical names are `Event` and `Session`; no duplicate Movie or Screening types are introduced. Venue entities live in Venue; event scheduling and session inventory live in Catalogue within the existing assembly.

## Acceptance and verification

- Requested fields and the four SessionSeat statuses are implemented; see [data model](data-model.md).
- Constructors reject invalid local values; targeted xUnit tests cover representative invalid inputs, status values, relationships, and time offsets.
- ORM mappings and reference/identity constraints are now supplied by [persistence](../local-persistence/FEATURE.md); row-level concurrency is now implemented by the [concurrency feature](../session-seat-concurrency/FEATURE.md), while full workflows and endpoints remain unimplemented. Test execution results are reported separately.

Run `dotnet test tests/TicketBooking.Domain.Tests/TicketBooking.Domain.Tests.csproj` after restoring dependencies. This exercises pure domain behavior without a database or API secrets.

## Navigation

- [Business context](business-context.md)
- [End-to-end flow](end-to-end-flow.md)
- [Entry points](entry-points.json)
- [Application responsibilities](per-app-responsibility.md)
- [Frontend structure](frontend-structure.md)
- [Backend structure](backend-structure.md)
- [Data model](data-model.md)
- [Integration points](integration-points.md)
- [Known gotchas](known-gotchas.md)
