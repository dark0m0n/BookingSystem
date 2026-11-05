# Backend structure

Public sealed classes with constructor validation and get-only properties live under [Venue](../../../src/TicketBooking.Domain/Venue/README.md) and [Catalogue](../../../src/TicketBooking.Domain/Catalogue/README.md). Constructors accept IDs explicitly; no hidden ID generation or clock access occurs. Session start/end inputs are normalized to UTC without changing their instants; see [time conventions](../../time-conventions.md). The internal [DomainGuard](../../../src/TicketBooking.Domain/DomainGuard.cs) centralizes nonempty GUID, nonblank string, and defined-enum checks without external dependencies.

`EventStatus`: Draft (default), Published, Archived. `SessionStatus`: Scheduled (default), Cancelled, Completed. `SeatType`: Standard (default), Premium, Accessible. These values are provisional implementation choices because the request did not specify their vocabularies. `SessionSeatStatus` exactly follows the supplied list.

No route registration, DTO, authorization, persistence mapping, background worker, mutation method, transaction, or optimistic concurrency implementation is added. Version is a stored in-memory value only. There is no relationship-existence, seat/auditorium consistency, schedule overlap, or duration-match check.

[CatalogueTests](../../../tests/TicketBooking.Domain.Tests/CatalogueTests.cs) checks composition, duration/age/time validation, different time offsets, all four seat statuses, price/version limits, and undefined statuses. [VenueTests](../../../tests/TicketBooking.Domain.Tests/VenueTests.cs) checks physical ownership, missing names, empty IDs, seat number limits, and undefined seat types. The test project now references Domain; other test projects remain scaffolds.
