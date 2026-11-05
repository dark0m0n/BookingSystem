# Business context

Actor: a developer needing a persistent local database for future application work. Prerequisites: Docker Compose, an available host port, local password configuration, and separately configured API secrets.

| Rule | Current policy | Enforcement and evidence |
| --- | --- | --- |
| BR-01 | Require a PostgreSQL password before provisioning. | [docker-compose.yml](../../../docker-compose.yml), required `POSTGRES_PASSWORD` interpolation. |
| BR-02 | Publish PostgreSQL only on host loopback. | Compose `127.0.0.1` port mapping; runtime network exposure Not verified. |
| BR-03 | Keep database files across ordinary container replacement. | Compose named `postgres_data` volume; README describes lifecycle. |
| BR-04 | Require API connection configuration at startup. | [Program.cs](../../../src/TicketBooking.Api/Program.cs), missing `DefaultConnection` guard and `UseNpgsql`. |

These are infrastructure policies, not ticketing business rules. No booking eligibility, reservation lifecycle, pricing, refunds, or customer permissions exist in the inspected implementation. Production access policies, backup policy, retention, and future entity requirements are unresolved.
