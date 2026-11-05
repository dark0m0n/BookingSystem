# Integration points

The in-process chain is Catalogue SessionSeat.Price → Reservation snapshots/total → Booking snapshots/total → Payment.Amount. All values use the same Money type; no implicit decimal conversion or currency exchange exists. Snapshot ownership passes by immutable value, not a mutable aggregate graph.

See [catalogue model](../venue-catalogue-model/FEATURE.md), [lifecycle entities](../reservation-booking-payment/FEATURE.md), [domain boundaries](../domain-boundaries/FEATURE.md), and [persistence foundation](../local-persistence/FEATURE.md). Existing API diagnostics and UTC clock registration are unaffected.

These signatures deliberately change: monetary parameters/properties now use Money, SeatHold accepts selected SessionSeat objects, and Payment creation uses CreateForBooking without an amount argument and with an injected clock for its captured deadline check. No external API contract existed for these paths. Future handlers must resolve requested IDs server-side; constructing arbitrary SessionSeat objects from customer prices would bypass authority despite correct local totals.

Future payment adapters must serialize amount/currency consistently and verify reported values against the stored attempt. No authentication, provider payload format, retries, idempotency key scope, or network integration is implemented here. Refund/messaging work remains deferred.
