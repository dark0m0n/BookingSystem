# Backend structure

The existing [Domain project](../../../src/TicketBooking.Domain/TicketBooking.Domain.csproj) has no project/package dependencies. Its seven folders are documented by the [domain map](../../../src/TicketBooking.Domain/README.md). Namespaces follow `TicketBooking.Domain.Identity`, `.Catalogue`, `.Venue`, `.Reservation`, `.Booking`, `.Payment`, and `.Notification`.

The original boundary organization consists of Markdown guides. Subsequent [Venue/Catalogue entities](../venue-catalogue-model/FEATURE.md) add constructors and enums, not executable services. No route registrations, handlers, repositories, permission checks, transactions, concurrency controls, background workers, configuration values, or idempotency mechanisms are added. The existing DbContext is unchanged.

No architecture rule is automatically enforced by these directories. Existing architecture tests contain an empty fact; no meaningful boundary-isolation test is introduced for this documentation/folder change. Domain constructor validation tests are documented separately in the model bundle. A Domain build checks that the existing library still builds; it does not prove future isolation or business behavior.
