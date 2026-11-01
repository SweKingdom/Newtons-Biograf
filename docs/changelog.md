# Changelog

## Unreleased

- 2026-11-01 — US-128 Auditorium management screens: admins list, create and edit auditoriums at `/admin/auditoriums` and toggle VIP/Standard rows from a seat grid; prices are shown read-only; pages are admin-only.
- 2026-11-01 — US-127 VIP rows and configurable seat prices: admins set whole auditorium rows to VIP or Standard (`PUT /api/auditoriums/{id}/rows/{row}/type`); seat prices (140 / 200 SEK) come from the `Pricing` configuration and are checked at startup.
- 2026-11-01 — US-126 Auditorium API: create, view and edit auditoriums with generated seats. Admins create and edit (`POST`/`PUT /api/auditoriums`); anyone can view (`GET`). Layout edits are blocked with 409 when bookings would be affected.
- 2026-11-01 — US-125 Movie management screens: staff can list, add, edit and delete movies at `/staff/movies` (ManageMovies or Admin), with field-level errors and the API message when a delete is blocked.
