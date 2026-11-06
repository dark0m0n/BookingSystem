# Stable error code contract

Public constants: [ErrorCodes.cs](../../../src/TicketBooking.Contracts/Errors/ErrorCodes.cs). [ApiErrorCatalog](../../../src/TicketBooking.Api/Errors/ApiErrorCatalog.cs) maps typed failures; messages are never parsed. Clients must branch on `code`; title/detail wording can change. Existing wire strings and meanings must remain stable.

| Code | HTTP | Trigger |
| --- | --- | --- |
| account_already_exists | 409 | Registration conflicts with an existing normalized email/username, including a concurrent insert. |
| invalid_refresh_token | 401 | Unknown, expired, revoked, replayed or account-ineligible refresh credential. Replayed consumed credentials revoke their family before returning this generic error. |
| invalid_credentials | 401 | Unknown/wrong login credentials, locked account or unsupported two-factor account. |
| seat_not_available | 409 | Hold includes unavailable inventory, allocation writer loses its version/state match, or EF reports SessionSeat-only concurrency. |
| hold_expired | 409 | Active hold reaches expiry or hold is already marked Expired. |
| hold_not_active | 409 | Cancelled/converted hold cannot perform an active-only operation. |
| hold_not_expired | 409 | Expire is attempted before hold expiry. |
| resource_not_owned | 403 | Hold/booking operation belongs to another user. |
| booking_not_pending_payment | 409 | Payment/confirmation/expiry requires PendingPayment. |
| booking_not_cancellable | 409 | Booking state disallows cancellation. |
| payment_deadline_passed | 409 | Pending booking reaches its captured payment deadline. |
| payment_deadline_not_reached | 409 | Pending booking is expired before its payment deadline. |
| cancellation_deadline_passed | 409 | Cancellation reaches the captured cutoff. |
| payment_already_processed | 409 | A final payment outcome would change, or reference recording requires a pending payment. |
| payment_reference_mismatch | 409 | Provider reference differs from the one already recorded. |
| concurrency_conflict | 409 | Other EF optimistic concurrency failure. |
| validation_failed | 400 | HTTP bad request / status-based validation failure. |
| unauthorized / forbidden | 401 / 403 | Missing/invalid JWT credentials or insufficient roles on a protected route, or corresponding bodyless HTTP status. |
| not_found / method_not_allowed | 404 / 405 | Missing route/resource or unsupported method. |
| conflict | 409 | Unclassified bodyless 409. |
| internal_error | 500 | Unexpected exception, unknown domain error, or bodyless 500. |
| http_error | Original status | Other error statuses without a dedicated mapping. |

Example response (illustrative trace ID):

```json
{
  "type": "urn:ticketbooking:error:hold_expired",
  "title": "Hold expired",
  "status": 409,
  "detail": "The seat hold has expired. Create a new hold.",
  "instance": "/example",
  "code": "hold_expired",
  "traceId": "example-trace"
}
```

## Guard precedence and retries

Hold ownership is checked before state/deadline, so another user's expired hold reports resource_not_owned. An already Expired hold reports hold_expired; Cancelled/Converted reports hold_not_active. Booking state precedes its payment deadline: an already Expired booking reports booking_not_pending_payment. Deadline equality counts as passed; early-expiry checks fail only before the deadline.

Payment reference mismatch is checked before final outcome. Repeating Succeeded/Succeeded or Failed/Failed with the same reference is idempotent and emits no error. Conflicting final outcomes report payment_already_processed. This does not implement database-level request deduplication, provider callbacks or automatic refunds.

Errors do not release seats, undo database writes or retry transactions. Future handlers own authorization, validation and atomic coordination. Unexpected messages are logged server-side, never copied to the public response. [HTTP tests](../../../tests/TicketBooking.IntegrationTests/ApiErrorContractTests.cs) exercise required codes and safe payloads; business endpoints remain unimplemented.

Auth endpoints validate request fields, including canonical refreshToken shape, using validation_failed plus field errors. Registration/login use the same safe ProblemDetails metadata; see [HTTP contracts](../identity-persistence/http-contract.md). These errors use explicit endpoint codes rather than message-based exception classification.

The reusable ownership service maps AuthenticationRequiredException to 401 unauthorized for a missing/unusable caller. Domain ResourceNotOwned remains 403 resource_not_owned for another owner or mismatched payment/booking link. Neither public detail includes owner IDs; see [resource ownership](../identity-persistence/resource-ownership.md). Existing business state/deadline codes are unchanged.

Catalogue/Admin FluentValidation failures retain 400 validation_failed plus errors keyed by existing camelCase/indexed field names. Malformed JSON/null required bodies/type binding errors use the same code and may omit errors; unsupported request content types use 415 http_error. Existence/state conflicts remain 404 not_found/409 conflict, with existing concurrency-specific mappings preserved. See [validation and generated ProblemDetails documentation](../venue-catalogue-model/validation-and-problems.md).
