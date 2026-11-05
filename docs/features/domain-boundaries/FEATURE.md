# Domain boundaries

Status: **implemented** for repository organization; business behavior is absent.

Working tree inspected, based on commit `9f0a19d`. The change organizes the existing Domain project into Identity, Catalogue, Venue, Reservation, Booking, Payment, and Notification folders. Each contains ownership guidance; Venue and Catalogue now also contain the [initial domain entities](../venue-catalogue-model/FEATURE.md). The actor is a contributor deciding where to place domain logic; the outcome is clear responsibility within the existing application.

Evidence: [domain map](../../../src/TicketBooking.Domain/README.md), its linked boundary guides, and [Domain project](../../../src/TicketBooking.Domain/TicketBooking.Domain.csproj). The boundary organization adds no services, projects, package references, endpoints, or migrations. Subsequent entity implementation is documented in the linked model bundle.

## Acceptance and verification

- Seven tracked ownership folders and namespace conventions: present; verified by file inspection.
- Existing single Domain assembly and deployment structure: preserved; no runtime behavior added.
- Booking workflows and access checks: not implemented by this structural change. Initial entity fields and constructor validation are documented separately in the model bundle.

Build with `dotnet build src/TicketBooking.Domain/TicketBooking.Domain.csproj --no-restore` after solution restore. Build results are reported separately from source evidence. This structure has no runtime startup command; existing [local startup](../../../README.md#local-startup) remains applicable to the API.

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
