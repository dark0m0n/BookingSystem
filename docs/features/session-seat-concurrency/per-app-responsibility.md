# Application responsibilities

- **Catalogue Domain** retains SessionSeat's private setters and validated construction. No EF dependency or public arbitrary mutation setter is added to Domain.
- **Infrastructure** owns concurrency metadata, the tracked-save version stamping, and SessionSeatAllocationWriter. The writer changes inventory rows only; it does not create/confirm bookings or mutate holds/payments.
- **API** registers the writer as scoped beside the scoped DbContext. There are no new endpoints or conflict-response handlers.
- **Application**, when implemented, must validate owner/deadline/payment evidence, retain the allocation's fencing token, and begin one transaction spanning all affected seats/aggregates.
- **PostgreSQL** atomically evaluates each conditional UPDATE. CI's disposable database runs the real tests; no extra production service is introduced.

Booking/refund/messaging ownership and deployment boundaries remain unchanged. No human ownership assignment or background component is introduced.
