# Known gotchas

- **Observed configuration behavior (README):** Changing database initialization credentials in `.env` does not update an existing volume's database credentials. Symptom: connection failures after changing settings. Use credentials matching the existing database or intentionally manage credentials; deleting the volume destroys local data.
- **Inferred-risk:** A busy `POSTGRES_PORT` can prevent container startup. Change the host port and update the API connection string consistently (README).
- **Observed in source:** Compose configuration does not configure the host API connection string. Set API User Secrets separately; missing connection configuration throws before startup.
- **Observed in source:** `InitialCreate` creates no business tables. A migration filename or successful `pg_isready` check does not establish a usable booking schema.
- **Observed in source:** `dotnet run` alone does not apply migrations. Use the [README startup command](../../../README.md#start-locally), which runs the explicit EF migration step before starting the API. Its API startup project requires both configuration values even for EF tooling; a failed migration stops the chain.
- **Observed in source:** API liveness does not test database connectivity. Diagnose database availability separately with Compose status/logs or database tools.

Runtime startup, actual credentials, active database schema, data durability, and port availability are Not verified. No mapped business entities, booking persistence, backups, retention policy, or meaningful integration tests are implemented in the inspected scope. These gaps require separate implementation requirements.
