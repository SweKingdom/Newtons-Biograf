# Changelog

## Unreleased

- 2026-11-01 — US-126 Auditorium API: create, view and edit auditoriums with generated seats. Admins create and edit (`POST`/`PUT /api/auditoriums`); anyone can view (`GET`). Layout edits are blocked with 409 when bookings would be affected.
- 2026-11-01 — US-125 Movie management screens: staff can list, add, edit and delete movies at `/staff/movies` (ManageMovies or Admin), with field-level errors and the API message when a delete is blocked.
