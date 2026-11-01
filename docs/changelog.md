# Changelog

## Unreleased

- 2026-11-01 — US-136 My bookings: upcoming and history API: customers list their own upcoming or past bookings via `GET api/bookings/mine?scope=`; `GET api/bookings/{id}` returns own bookings (403 for others, 404 for unknown ids); staff with ManageBookings can search by showing or reference.
- 2026-11-01 — US-135 Create booking with database-enforced double-booking protection: `POST /api/bookings` lets customers book 1–8 seats for a future showing; a unique index prevents double booking and a `BookingCreated` event is raised after commit.
