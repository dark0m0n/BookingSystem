# Integration points

| Boundary | Contract | Authority and failure behavior |
| --- | --- | --- |
| Developer → Compose/API | Existing .env/secret configuration and startup chain. | Compose provisions PostgreSQL; API supplies connection configuration. |
| DbContext → PostgreSQL | Npgsql SQL against mapped tables and migrations. | Database enforces references/identity; duplicate keys return 23505, missing references/restricted deletes 23503. No HTTP mapping/retry policy is implemented yet. |
| Infrastructure → Domain | Private materialization API and explicit complex Money mapping. | Domain creation remains validated for normal callers; EF loads stored state without clocks or recomputation. |
| CI/tests → disposable PostgreSQL | TICKETBOOKING_TEST_CONNECTION_STRING. | Tests apply migrations and rollback test rows; environment must target an isolated database. CI supplies a dedicated service. |

[Catalogue](../venue-catalogue-model/FEATURE.md), [lifecycle entities](../reservation-booking-payment/FEATURE.md), [money](../money-and-pricing/FEATURE.md), and [timing](../booking-timing/FEATURE.md) now have mapped storage. Their public API endpoints and transactional workflows remain absent. [API diagnostics](../api-diagnostics/FEATURE.md) still checks only liveness.

Provider-reference uniqueness currently assumes the single simulated provider; a future multi-provider model must scope references by provider. Database uniqueness prevents duplicate payment rows, not duplicate external charges: future request handling must insert/claim the idempotent attempt and coordinate provider calls safely. No RabbitMQ, Redis, refunds, callbacks, events, or background processing is added here.
