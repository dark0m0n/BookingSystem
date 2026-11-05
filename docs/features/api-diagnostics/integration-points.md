# Integration points

| Boundary | Contract / authority | Failure behavior |
| --- | --- | --- |
| Client → API | ProblemDetails `code` defined in Contracts; API owns HTTP mapping. | Branch on code, not title/detail or exception text. No automatic client retry exists. |
| Domain → API | DomainRuleException.Error, independent of HTTP/Contracts. | Explicit enum mapping; unknown values fail safely as internal_error. |
| Infrastructure → API | SeatAllocationConflictException or EF concurrency entries. | Allocation and SessionSeat-only conflicts → 409 seat_not_available; other concurrency → 409 concurrency_conflict. |
| API → ASP.NET Core | AddProblemDetails, exception/status middleware, JSON fallback. | Unsupported Accept still receives JSON for handled exceptions/bodyless errors. |
| API → persistence configuration | Required startup connection and DbContext registration. | Missing configuration stops startup; health does not query PostgreSQL. |

See [lifecycle](../reservation-booking-payment/FEATURE.md), [concurrency](../session-seat-concurrency/FEATURE.md), [timing](../booking-timing/FEATURE.md), and [persistence](../local-persistence/FEATURE.md). Business routes and provider callbacks remain absent; no external network integrations, broker or refund processing are introduced.

Changing an existing code's string or meaning breaks clients. Add a new code, enum mapping and behavioral test for a new failure. Database uniqueness violations are not globally classified as payment replay; durable idempotency requires application-level handling.
