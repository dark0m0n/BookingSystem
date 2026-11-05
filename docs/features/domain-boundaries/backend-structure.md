# Backend structure

The existing [Domain project](../../../src/TicketBooking.Domain/TicketBooking.Domain.csproj) has no project/package dependencies. Its seven folders are documented by the [domain map](../../../src/TicketBooking.Domain/README.md). Namespaces follow `TicketBooking.Domain.Identity`, `.Catalogue`, `.Venue`, `.Reservation`, `.Booking`, `.Payment`, and `.Notification`.

The original boundary organization consists of Markdown guides. Subsequent [Venue/Catalogue entities](../venue-catalogue-model/FEATURE.md) and [hold/booking/payment lifecycles](../reservation-booking-payment/FEATURE.md) add local domain code, not executable services. No HTTP handlers, repositories, transactions, concurrency controls, background workers, or configuration values are added. Local ownership/deadline and payment-replay guards are documented in the lifecycle bundle; they are not API authorization or global idempotency. The [persistence feature](../local-persistence/FEATURE.md) now supplies DbSets, mappings, and relational constraints in the existing shared DbContext.

No architecture rule is automatically enforced by these directories. Existing architecture tests contain an empty fact; no meaningful boundary-isolation test is introduced for this documentation/folder change. Domain constructor validation tests are documented separately in the model bundle. A Domain build checks that the existing library still builds; it does not prove future isolation or business behavior.

[Identity persistence](../identity-persistence/FEATURE.md) now implements ApplicationUser and framework stores in Infrastructure, registered by the API. Domain.Identity remains free of ASP.NET/EF dependencies; GUID references cross the boundary.

Domain.Identity now defines RoleNames.Customer/Admin constants. The API owns named authorization policies; the Infrastructure data migration owns persisted definitions. Public registration assigns only Customer in a database transaction, without introducing an ASP.NET/EF dependency into Domain. See [roles and policies](../identity-persistence/roles-and-policies.md).
