# Frontend structure

Not applicable: no frontend application, account screens or client API implementation exists. Callers use the JSON contracts in [http-contract.md](http-contract.md). A future client must handle field validation, account conflicts and generic login failure; send returned access tokens through Authorization:Bearer and use expiresAt.

No cookies, Identity UI or MapIdentityApi routes are registered. Browser token storage, client refresh coordination/logout, CSRF integration and MFA screens remain future work. The server refresh and logout routes now exist; clients must serialize refresh requests and replace both returned tokens after each successful rotation. These documents prescribe no browser persistence strategy.

A future client should POST its retained refresh token to /api/auth/logout and discard both token types after success. Logout can be retried safely; refresh reuse remains strict and must not be transparently retried. Server logout revokes renewal rights, not already-issued access JWTs.

The client cannot select its role during registration. Hiding admin UI does not authorize a request; management APIs must enforce the Admin policy. Customer/Admin membership claims reflect server state at token issuance. No admin-provisioning or role-management screen exists.

Resource ownership is server-enforced through reusable checks; there are no business ownership screens/routes yet. A future client handles 401 by authenticating and 403 resource_not_owned as denied access, without resubmitting an asserted owner ID.
