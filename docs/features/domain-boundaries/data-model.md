# Data model

The boundary-organization step introduces no schema. Subsequent [Venue/Catalogue entities](../venue-catalogue-model/data-model.md) define initial fields, identifiers, and status values. No migrations or mapped tables are introduced; tracked folders and documentation alone require no database changes.

Reservation, Booking, and Payment now have domain fields/enums and guarded transitions in the [lifecycle model](../reservation-booking-payment/data-model.md); this ownership document does not duplicate them. Cross-boundary identifier/value references are a convention for future models, not an implemented schema. PII handling, retention, transaction consistency, and applied database state are not established here.

See [local persistence](../local-persistence/FEATURE.md) for the mapped schema, foreign keys, uniqueness scopes, and migration.
