# Frontend structure

Not applicable: this repository has no frontend. Authenticated clients can select session-seat IDs from the anonymous priced seat map and send the [hold request](hold-http-contract.md) with a bearer token. Refresh the seat map after a seat_not_available response; clients must use the returned server total and expiry. Owners can follow the POST Location to GET historical hold details, or DELETE that route to cancel before expiry. A successful cancellation returns 204; the client can refresh the public seat map. There are no browser components, client state stores, frontend tests or automatic countdown/refresh implementation.

The background worker may commit expiry after the displayed deadline. Clients must treat expiresAt as an eligibility cutoff, refresh stored hold/seat-map state, and never infer that seats were released until server state says so. No polling UI is implemented by the worker.

A repeated successful owner DELETE returns 204, including after the original expiry. It acknowledges the prior cancellation and never releases a later allocation. Converted holds still return hold_not_active. No booking UI or production booking API is introduced by the conversion service.
