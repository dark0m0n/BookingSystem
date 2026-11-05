# Business context

The supplied field list defines the initial model for cinema/event inventory. A physical seat belongs to an auditorium; a SessionSeat describes that seat's price and availability for a session. The implemented actor is a code caller constructing domain objects, not an authenticated customer or operator endpoint.

| Rule | Requirement or implementation choice | Enforcement |
| --- | --- | --- |
| BR-01 | Required entity/relationship identifiers are nonempty GUIDs. Implementation choice. | Constructors call `DomainGuard.Id`. |
| BR-02 | Names, title, address, and row are nonblank; description is nonnull but may be empty. Implementation choice. | `DomainGuard.Text`; Event constructor. |
| BR-03 | Duration and seat number are positive; age rating, price, and version are nonnegative. Implementation choices. | Event, Seat, SessionSeat constructors. |
| BR-04 | Session ends strictly after it starts, comparing actual instants. Implementation choice. | Session constructor with DateTimeOffset comparison. |
| BR-05 | Session seat states are Available, Held, Booked, Unavailable. User requirement. | `SessionSeatStatus` and enum validation. |
| BR-06 | Other enums accept only their defined members. Provisional modeling choice. | `DomainGuard.Defined`. |

Evidence: linked source files in [data-model.md](data-model.md). AgeRating is an integer minimum-age label, not a jurisdictional classification or enforced admission check. Currency/precision now follow the [Money policy](../money-and-pricing/FEATURE.md). Hold/payment/cancellation windows now follow the [timing policy](../booking-timing/FEATURE.md). Dynamic pricing, SessionSeat transitions, database seat uniqueness, overlap rules, and refund rules remain future work. No permission, eligibility, or payment enforcement is claimed.
