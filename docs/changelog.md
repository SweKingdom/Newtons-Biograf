# Changelog

## Unreleased

- {2026-11-01} — US-124 Movie API: create, view, edit, delete with validation: staff with ManageMovies can create, edit and delete movies via the API; list and detail are public; invalid input returns per-field errors; delete is blocked (409) when the movie has future showings or any past or cancelled showings.
- {2026-11-01} — US-123 Blazor login, registration and admin staff screens: login, registration, logout, role-based menu, route guard and admin staff permission screen added to the web app.
