# End-to-end flow

Trigger: a contributor implements a use case after obtaining its requirements.

1. Consult the [domain map](../../../src/TicketBooking.Domain/README.md) and select the owning boundary (BR-01/BR-02).
2. Place domain types in that folder with `TicketBooking.Domain.<Boundary>` namespaces. Venue and Catalogue now have initial entity types; see the [model bundle](../venue-catalogue-model/FEATURE.md).
3. Keep local invariants with their owning model. Use identifiers or immutable values for references; coordinate changes spanning owners in Application (BR-03).
4. Put persistence/provider adapters in Infrastructure and HTTP registration in API as needed by the implemented use case.
5. Validate the feature and document its actual behavior using `document-feature`.

This is the contributor workflow, not an implemented customer booking flow. There are no runtime entry IDs, permission checks, state changes, transactions, retries, background jobs, or handoffs for this organizational feature. Exact integration contracts and consistency decisions remain unresolved until implementation.
