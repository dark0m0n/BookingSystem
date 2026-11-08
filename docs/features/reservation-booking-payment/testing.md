# Hold integration coverage

Tests run the HTTP pipeline against PostgreSQL using migrated isolated schemas and a manual TimeProvider. Set TICKETBOOKING_TEST_CONNECTION_STRING to a disposable test database; PostgreSQL cases skip when it is absent. The database user needs schema-creation privileges. Tests must not target development or deployed data.

| Area | Evidence | Assertions |
| --- | --- | --- |
| Lookup and historical DTOs | AuthEndpointsTests.HoldLifecycle, HoldOwnership | Correct route ID/child selection across one customer's holds with different prices/currencies; missing 404; immutable snapshots/history; no internal owner/token fields. |
| Ownership and policy | AuthEndpointsTests.HoldOwnership, HoldLifecycle | Anonymous 401; other Customer/Admin 403 resource_not_owned; a stored owner with a fresh roleless JWT receives 403 forbidden. Rejections leave holds/items/inventory unchanged. |
| Cancellation | AuthEndpointsTests.HoldOwnership, HoldConversionRaces | Eligible cancellation and repeat return 204; retain history; exact expiry rejection; release once; concurrent requests, stale tokens and later claims remain safe. |
| Expiration and worker lag | AuthEndpointsTests.HoldExpiry, HoldUse | Before/at/after deadline/start, worker-independent use guards, bounded cursor batches, failed-record isolation, multiple workers, shutdown and retained-token release. |
| Multi-seat rollback | AuthEndpointsTests.OverlapAndExpiryRaces, HoldAtomicity, HoldLifecycle, Holds | Overlapping {A,B}/{B,C} requests force either winner under both input orders: one complete hold survives, the losing exclusive seat stays free/version 0 and can be retried. A later claim conflict returns 409 and rolls back earlier claims. Save/deadline failures leave no partial hold/items or token increments. Real PostgreSQL insert failure returns safe 500 and rolls back all claims/header/items. An independent valid retry succeeds. |
| Twenty-user contention | AuthEndpointsTests.ParallelHolds | Twenty registered Customer identities, dedicated HTTP/1.1 connection pools and twenty simultaneous PostgreSQL backend sessions/transactions contend for one seat: exactly one 201 and nineteen 409 seat_not_available; one persisted owner/hold/item and one version increment. |
| Cleanup versus conversion | AuthEndpointsTests.HoldConversionRaces, OverlapAndExpiryRaces | Every winning transition; late/save-failed conversion rolls back booking/transfers; stale tracked saves cannot overwrite winners; old tokens cannot release transferred/reacquired seats. |
| Persistence contract | PersistenceModelTests, PostgresPersistenceTests | Model/snapshot consistency, keys/FKs, nullable legacy claim tokens, positive token constraints, indexes and historical value round trips. |

The added lifecycle cases are in [AuthEndpointsTests.HoldLifecycle.cs](../../../tests/TicketBooking.IntegrationTests/AuthEndpointsTests.HoldLifecycle.cs). Its SQL trigger/function are created only in the fixture's generated schema and removed with that schema. The trigger rejects an actual multi-seat child insert after allocation/header writes, exercising PostgreSQL savepoint and outer-transaction rollback rather than only an EF interceptor before saving. Test-only DDL and the conversion HTTP adapter are not registered in the production API.

The HTTP fixture registers expiry processing without starting the worker automatically; worker tests explicitly invoke batches or host the service. Deadline assertions advance injected time without sleeps. Command gates make partial-write and race cases deterministic.

Run the focused group with:

```bash
dotnet test tests/TicketBooking.IntegrationTests --filter 'FullyQualifiedName~HoldRaces_|FullyQualifiedName~ParallelHolds_|FullyQualifiedName~HoldLifecycle_|FullyQualifiedName~HoldCleanup_|FullyQualifiedName~HoldExpiry|FullyQualifiedName~HoldOwner|FullyQualifiedName~CustomerHold_|FullyQualifiedName~HoldUse_|FullyQualifiedName~PersistenceModelTests|FullyQualifiedName~PostgresPersistenceTests'
```

See [FEATURE.md](FEATURE.md#current-verification) for observed runtime results and remaining implementation gaps.

The twenty-user test pauses inventory-query readers before allocation. It records DbContext IDs, PostgreSQL backend PIDs and transaction IDs to prove independent database connections/transactions, rather than relying on Task.WhenAll alone. Each customer uses a separate SocketsHttpHandler and real registration/login JWT. Assertions tie the winning response to its persisted owner and verify unselected inventory remains unchanged. The barrier is disabled in all other fixture cases and is not production code.

OverlapAndExpiryRaces uses two customers and separate HTTP handlers for {A,B}/{B,C}. The first request pauses after claiming A, and the second pauses after reading B/C; gates then force either complete selection to win, including reversed request order. Its expiry/conversion case pauses the database commit after conversion's final eligibility check one tick before expiry. A worker becoming due at the cutoff sees the old committed Active row but skips the conversion lock; after commit it ignores Converted and leaves booking tokens intact. Existing tests separately prove a conversion crossing expiry before its final check rolls back, allowing expiry to win.
