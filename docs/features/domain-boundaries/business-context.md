# Business context

Contributors need clear ownership before implementing ticket-booking behavior. The requested boundaries are logical modules inside the existing application, not separate services.

| Rule | Responsibility | Enforcement/evidence |
| --- | --- | --- |
| BR-01 | Keep all seven boundaries in the existing Domain project. | Folder structure and unchanged project definition; one assembly. |
| BR-02 | Distinguish temporary reservations, durable bookings, and financial payment state. | Domain folder ownership guides; design convention, no business enforcement yet. |
| BR-03 | Keep framework/provider code outside Domain; Application coordinates spanning workflows. | Domain map convention and existing reference direction; not automatically enforced. |

Evidence: [domain map](../../../src/TicketBooking.Domain/README.md) and its linked guides. Hold duration, eligibility, pricing, payment confirmation, refunds, permissions, and notification retry rules are unspecified. Ownership guidance is not proof of those behaviors or a business requirement for specific lifecycle transitions.
