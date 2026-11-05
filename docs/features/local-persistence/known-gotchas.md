# Known gotchas

- **Observed:** The original InitialCreate is empty. Apply the later AddDomainForeignKeysAndUniqueConstraints migration to create tables; merely finding the old migration ID does not prove the schema exists.
- **Observed:** Business UserId still has no FK despite the new Identity tables; user backfill and deletion compatibility remain unresolved. Do not infer user existence or ownership authorization from stored UUIDs.
- **Observed:** FK existence plus unique session-seat pairs do not prove auditorium/session consistency or prevent concurrent active holds/double booking. Version-based row fencing now exists in the [concurrency feature](../session-seat-concurrency/FEATURE.md); durable allocation ownership and complete transactional workflows remain future work.
- **Observed:** Item keys are scoped to their parent to preserve history and allow reuse after expiry/cancellation. A global unique item SessionSeatId would incorrectly prohibit later bookings of released inventory.
- **Observed:** Database storage limits are 128 characters for booking numbers and 256 for payment keys/references. There is no corresponding HTTP validation layer yet. Row is case-sensitive without trimming/normalization.
- **Observed:** Money columns enforce precision/required currency length, not every domain rule or cross-row sum. Raw SQL can bypass supported-currency/nonnegative checks and numeric may round subscale inputs. Use validated server Money values.
- **Observed:** DB tests skip without TICKETBOOKING_TEST_CONNECTION_STRING and apply migrations to the supplied database when enabled. Use a dedicated test database; CI provisions one. Test-data transactions roll back, while schema migration history remains until the disposable database is removed.
- **Observed configuration behavior (README):** Changing initialization credentials in .env does not update an existing PostgreSQL volume; keep API credentials aligned. Port conflicts require changing the host port/connection string together.

Real customer endpoints, durable owner-linked allocation workflows, expiry workers, provider idempotency, refunds, backups, and retention policy are not implemented by these constraints. Developer/deployed database state was not changed or verified; only isolated PostgreSQL tests exercised the migration.
