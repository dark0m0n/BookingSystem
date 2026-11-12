# Frontend structure

No admin UI is added. Authorized admin clients can request /api/admin/bookings with page/pageSize and render summaries across owners, using stored UserId as the owner reference. Responses are private/no-store; the customer details URL still requires ownership even for Admin.

Clients can follow the booking creation Location to GET /api/bookings/{bookingId}. GET /api/me/bookings?page=1&pageSize=20 returns caller-only summaries and standard pagination metadata; fetch details for seat item snapshots. Defaults are page 1/size 20, maximum size 100, newest first with Id tie-breaker. No frontend screens or state-management implementation is added.

Pending booking seats remain reserved through the returned paymentDeadline. At the cutoff, payment/confirmation eligibility ends even if polling cleanup has not committed; clients must refresh server availability and cannot infer seat release from countdown expiry. No frontend countdown or booking polling UI is implemented.

Not applicable: this repository has no frontend. Authenticated clients can select session-seat IDs from the anonymous priced seat map and send the [hold request](hold-http-contract.md) with a bearer token. Refresh the seat map after a seat_not_available response; clients must use the returned server total and expiry. Owners can follow the POST Location to GET historical hold details, or DELETE that route to cancel before expiry. A successful cancellation returns 204; the client can refresh the public seat map. There are no browser components, client state stores, frontend tests or automatic countdown/refresh implementation.

The background worker may commit expiry after the displayed deadline. Clients must treat expiresAt as an eligibility cutoff, refresh stored hold/seat-map state, and never infer that seats were released until server state says so. No polling UI is implemented by the worker.

A repeated successful owner DELETE returns 204, including after the original expiry. It acknowledges the prior cancellation and never releases a later allocation. Converted holds still return hold_not_active. No booking UI exists. Clients can send only holdId to [POST /api/bookings](booking-http-contract.md) and use its returned number, snapshots and payment deadline. Repeated conversion conflicts; clients must not blindly retry an uncertain successful response.
