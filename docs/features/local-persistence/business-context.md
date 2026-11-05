# Business context

Developers need repeatable local persistence, and the database must reject duplicate inventory identities and orphan records independently of application validation. Prerequisites remain Docker Compose, database configuration, and API User Secrets for local startup.

| Rule | Policy | Enforcement/evidence |
| --- | --- | --- |
| BR-01 | Require a PostgreSQL password before local provisioning. | Compose required POSTGRES_PASSWORD interpolation. |
| BR-02 | Publish the local database on loopback only. | docker-compose.yml port mapping. |
| BR-03 | Preserve data across ordinary container replacement. | Named postgres_data volume; README lifecycle instructions. |
| BR-04 | Require API connection configuration. | Program.cs DefaultConnection guard and UseNpgsql registration. |
| BR-05 | One seat coordinate per auditorium; one inventory row per session/physical-seat pair. | SeatConfiguration and SessionSeatConfiguration unique indexes. |
| BR-06 | All mapped parent references must exist; referenced history is not cascade-deleted. | Thirteen required FKs with Restrict delete behavior. |
| BR-07 | Each hold/booking contains a session seat once, but historical parents may reuse it. | Composite item primary keys; no global unique item SessionSeatId. |
| BR-08 | Booking numbers are unique. Payment idempotency is scoped to (BookingId, IdempotencyKey). | Unique indexes; keys are exact/case-sensitive. |
| BR-09 | A nonnull provider reference identifies one payment for the current single provider. | Filtered unique ProviderReference index; multiple nulls allowed. |

Seat Row values are exact, case-sensitive labels; no trimming/case folding is introduced. Identity has no User table yet, so UserId is not a database FK. Constraints do not implement authorization, session/auditorium consistency, globally unique active holds, or double-sale protection; those need further model/transaction work.
