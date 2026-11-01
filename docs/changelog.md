# Changelog

## Unreleased

- 2026-11-01 — US-135 Create booking with database-enforced double-booking protection: `POST /api/bookings` lets customers book 1–8 seats for a future showing; a unique index prevents double booking and a `BookingCreated` event is raised after commit.
