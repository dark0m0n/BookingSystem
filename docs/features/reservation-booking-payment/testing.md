# Reservation and booking integration coverage

Tests run the HTTP pipeline against PostgreSQL using migrated isolated schemas and a manual TimeProvider. Set TICKETBOOKING_TEST_CONNECTION_STRING to a disposable test database; PostgreSQL cases skip when it is absent. The database user needs schema-creation privileges. Tests must not target development or deployed data.

## Booking regression matrix

The test-only follow-up adds [AuthEndpointsTests.BookingRegressions](../../../tests/TicketBooking.IntegrationTests/AuthEndpointsTests.BookingRegressions.cs) and strengthens existing DomainError/no-write assertions. No production code, contract or migration changes are introduced by this follow-up.

| Requested behavior | Evidence and assertions |
| --- | --- |
| Expired holds | CustomerBooking expiry tests cover one tick before/at/after expiry while cleanup lags; rejected conversion leaves no booking/items. BookingRegression_ExpiredHoldCannotConvertOrDisturbReacquiredSeats expires a hold, reacquires its seats and rejects old-hold conversion with hold_expired while preserving successor tokens/history. |
| Wrong owners | CustomerBooking rejects another customer even with a spoofed owner field; HoldCleanup tests reject another Admin and assert no booking/items or allocation changes. BookingTests now assert exact ResourceNotOwned/HoldExpired/HoldNotActive DomainError values and unchanged hold state. |
| Repeated conversion | BookingRegression_RepeatedConversionPreservesTheSingleBookingAndItsDeadline retries before and after original hold expiry, asserting hold_not_active, one unchanged booking/source/owner, original deadlines/prices and unchanged allocation tokens. Existing gated concurrent-conversion tests verify one winner. |
| Unchanged price snapshots | BookingRegression_ConversionAndCatalogueChangesPreserveIndependentPriceSnapshots captures 12.50 and 7.25 EUR, converts to a 19.75 EUR booking, then changes current inventory to USD. Owner reads and persisted snapshots preserve the original amounts/currency and independent hold/booking tokens. Existing PostgreSQL repricing tests also exercise tracked Money isolation across capture stages. |
| Unpaid booking expiry | BookingRegression_UnpaidBookingExpiresAtItsCapturedConfiguredDeadline binds a seven-minute payment policy into the production conversion service, then runs expiry in a host configured for fifteen minutes. Original hold expiry does not release seats; one tick before the captured deadline remains pending/Held, equality expires/releases once, HTTP history shows Expired and no payment is invented. Existing worker cases cover the tick after, rollback, stale tokens, session boundaries and concurrent release. |

The configured-window case uses a test ICurrentUser carrying the real registered hold owner's ID to call the production conversion service with the independently configured policy; it reads results through the production HTTP route and invokes production expiry processors. The price-isolation case uses test-only tracked EF catalogue mutations, which advance inventory versions; it does not expose allocated-price editing or refresh captured ownership tokens.

Observed verification: **148 focused integration cases with PostgreSQL enabled and all 199 domain cases passed, zero failures/skips**. Time advances through the injected clock without sleeps. Only disposable storage was migrated; development/deployed state is not verified.

To rerun the booking groups after setting TICKETBOOKING_TEST_CONNECTION_STRING:

```bash
dotnet test tests/TicketBooking.Domain.Tests --configuration Release
dotnet test tests/TicketBooking.IntegrationTests --configuration Release --filter 'FullyQualifiedName~AuthEndpointsTests.Booking|FullyQualifiedName~AuthEndpointsTests.CustomerBooking_|FullyQualifiedName~AuthEndpointsTests.HoldCleanup_|FullyQualifiedName~AuthEndpointsTests.HoldUse_|FullyQualifiedName~PostgresPersistenceTests|FullyQualifiedName~PersistenceModelTests|FullyQualifiedName~BookingTimingConfigurationTests|FullyQualifiedName~BookingExpiryConfigurationTests'
```

## Hold coverage

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

The added lifecycle cases are in [AuthEndpointsTests.HoldLifecycle.cs](../../../tests/TicketBooking.IntegrationTests/AuthEndpointsTests.HoldLifecycle.cs). Its SQL trigger/function are created only in the fixture's generated schema and removed with that schema. The trigger rejects an actual multi-seat child insert after allocation/header writes, exercising PostgreSQL savepoint and outer-transaction rollback rather than only an EF interceptor before saving. Test-only DDL is not registered in the production API. Conversion tests now use the production POST /api/bookings route.

The HTTP fixture registers expiry processing without starting the worker automatically; worker tests explicitly invoke batches or host the service. Deadline assertions advance injected time without sleeps. Command gates make partial-write and race cases deterministic.

Run the focused group with:

```bash
dotnet test tests/TicketBooking.IntegrationTests --filter 'FullyQualifiedName~HoldRaces_|FullyQualifiedName~ParallelHolds_|FullyQualifiedName~HoldLifecycle_|FullyQualifiedName~HoldCleanup_|FullyQualifiedName~HoldExpiry|FullyQualifiedName~HoldOwner|FullyQualifiedName~CustomerHold_|FullyQualifiedName~HoldUse_|FullyQualifiedName~PersistenceModelTests|FullyQualifiedName~PostgresPersistenceTests'
```

See [FEATURE.md](FEATURE.md#current-verification) for observed runtime results and remaining implementation gaps.

The twenty-user test pauses inventory-query readers before allocation. It records DbContext IDs, PostgreSQL backend PIDs and transaction IDs to prove independent database connections/transactions, rather than relying on Task.WhenAll alone. Each customer uses a separate SocketsHttpHandler and real registration/login JWT. Assertions tie the winning response to its persisted owner and verify unselected inventory remains unchanged. The barrier is disabled in all other fixture cases and is not production code.

OverlapAndExpiryRaces uses two customers and separate HTTP handlers for {A,B}/{B,C}. The first request pauses after claiming A, and the second pauses after reading B/C; gates then force either complete selection to win, including reversed request order. Its expiry/conversion case pauses the database commit after conversion's final eligibility check one tick before expiry. A worker becoming due at the cutoff sees the old committed Active row but skips the conversion lock; after commit it ignores Converted and leaves booking tokens intact. Existing tests separately prove a conversion crossing expiry before its final check rolls back, allowing expiry to win.

Source-hold duplicate prevention adds PostgreSQL tests for duplicate known source rejection (with different booking numbers), allowed multiple unknown legacy sources, a deterministic two-request hold-lock race, and full conversion rollback when an inconsistent Active hold already has a booking. The domain factory test asserts captured SourceHoldId; metadata/snapshot checks include the new index and optional restrictive foreign key.

AuthEndpointsTests.BookingExpiry covers booking retention after original hold expiry, failed/retried payment attempts without deadline extension, one tick before/at/after PaymentDeadline, session-start release status, terminal states, stale/null/booked tokens, save/commit rollback, competing workers, successor reacquisition, stale tracked confirmation, poison-row cursor progress and immediate hosted-service work/shutdown. BookingExpiryConfigurationTests validates polling/batch limits and scoped lifetimes. See [worker](booking-expiry-worker.md).

AuthEndpointsTests.BookingReads covers creation Location, all stored statuses, independent historical price snapshots, Customer/Admin/roleless access, owner-field spoofing, internal-data exclusion, filtered totals, stable newest-first/tied-ID order, empty/beyond-end/default/maximum pages, malformed/repeated/overflowing inputs, OpenAPI and deterministic concurrent insertion between count and page. Creation timestamp tests deliberately use sub-microsecond clock ticks and expect storage-compatible capture.

AuthEndpointsTests.AdminBookings covers authorization precedence, invalid tokens, Customer/removed-Admin denial, all-owner/all-status history, stable tied ordering, stored totals, admin-only owner identifiers, pagination validation, empty/beyond-end pages, concurrent count/page consistency, OpenAPI and preserved customer ownership boundaries. Administration_AllRoutesRequireAuthenticationAndAdminRoleBeforeWrites includes the new route.
