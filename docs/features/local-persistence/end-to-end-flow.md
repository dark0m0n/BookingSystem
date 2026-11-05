# End-to-end flow

1. Configure .env for Compose and API User Secrets separately, following README (BR-01/04). Host API does not automatically load Compose .env.
2. Run the README startup chain: restore the EF tool, wait for PostgreSQL readiness, then apply pending migrations. Infrastructure is the target project and API supplies Development startup configuration.
3. InitialCreate contributes no tables. The next migration creates eleven tables, primary/foreign keys, unique indexes, money columns, and UTC deadline columns (BR-05/09). AddAspNetCoreIdentity then adds seven account/role tables with six cascading dependent FKs. EF records applied migration IDs; Program.cs does not auto-migrate.
4. API registration supplies TicketBookingDbContext. OnModelCreating configures the base Identity model, then scans the Infrastructure configuration assembly (`db-context`). Domain callers can persist through DbSets; no API CRUD handlers exist.
5. EF writes parent records before dependent rows. Missing parents reject with PostgreSQL SQLSTATE 23503. Duplicate unique/primary keys reject with 23505; neither failure implies a request-level HTTP mapping exists yet.
6. Queries materialize through private EF constructors/setters and backing collections, bypassing public creation methods without exposing public mutation. Money complex values and captured UTC deadlines round-trip without recalculation.

Constraint tests use their own database transaction for data and roll it back. Migrations are applied outside that transaction and remain in the disposable test database. Development database contents/migration history were not inspected or changed. CI uses a dedicated PostgreSQL service; API liveness remains independent of database readiness.

A future handler must manage its transaction and expected conflicts; no generic retry, hold conversion, seat release, callback coordination, or HTTP conflict handling is implemented here. Ordinary docker compose down keeps the named development volume; explicit volume deletion remains destructive and is not a routine restart.
