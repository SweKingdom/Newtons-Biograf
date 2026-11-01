# Project Brief — Newtons Biograf

## Goal
Deliver an internal web app plus API so cinema staff can manage movies, auditoriums and showings, and customers can browse, book tickets and choose seats. Source: requirements document (newtons-biograf-requirements.pdf).

## Scope
Movies CRUD, auditoriums (create/view/edit, seat types, config prices), showings (create/edit/cancel, overlap and past-time rules), public search/filter (14 days), booking with seat map, cancellation, users/roles/permissions, seed data with committed SQLite DB, xUnit scenario tests, README and test documentation.

## Personas
- **Visitor** – not logged in; browses movies and showings.
- **Customer** – registers, books, views and cancels own bookings.
- **Staff** – permissions via claims: ManageMovies, ManageShowings, ManageBookings.
- **Admin** – all rights; manages auditoriums and staff accounts/permissions.

## Business processes
1. **Manage movies:** staff with ManageMovies creates/edits/deletes; validation per field; delete blocked if future showings exist.
2. **Manage auditoriums:** admin creates (name, rows, seats/row), marks rows VIP, edits.
3. **Manage showings:** staff with ManageShowings picks movie + auditorium + start; end = start + length + 15 min; reject past start and overlaps (error names conflicting showing); cancel showing cancels all its bookings; showing with bookings cannot be deleted.
4. **Browse:** anyone sees showings for next 14 days; filter by genre/date/auditorium, search title (case-insensitive partial), filters combinable.
5. **Book:** logged-in customer opens seat map, picks 1–8 seats on a not-started showing; booking gets short reference, total price (Standard 140 / VIP 200 SEK, configurable), status Confirmed; unique DB constraint prevents double booking; loser gets error + refreshed seat map. No payment.
6. **My bookings:** upcoming and history with status.
7. **Cancel:** customer up to 2h before start; staff with ManageBookings/admin any time; booking kept as Cancelled, seats freed.
8. **Access:** register/login (Identity + JWT, password ≥ 8); admin creates staff and assigns/changes/removes permission claims; API returns 401 unauthenticated, 403 without role/permission.

## Data entities
Movie, Auditorium, Seat (row, number, type), Showing, Booking (reference, status, total price, user, showing), BookingSeat (unique ShowingId+SeatId), ApplicationUser (Identity), roles, permission claims, Price configuration.

## Integrations
None external. Internal: Blazor WASM frontend ↔ .NET 8 Web API (JWT bearer).

## Non-functional requirements
- .NET 8 Web API, EF Core, SQLite, code-first migrations; Blazor WebAssembly with custom design, no template boilerplate.
- Code must clearly show generics (generic repository), delegates/events (booking created/cancelled), LINQ, async/await.
- Comments where needed; permissions enforced server-side.
- Seed: 1 admin, 2 staff (different permissions), 3 customers, 3 auditoriums (one with VIP rows), 10 movies, showings for next 14 days, some bookings; SQLite file committed.
- xUnit scenario tests; README with run instructions, seeded accounts, three user scenarios; test documentation.

## Assumptions
- A "future showing" is one whose start time is after now.
- Only Admin manages auditoriums and staff accounts; Staff manage movies/showings/bookings per claims.
- Admin implicitly holds all permissions.
- Showing "cancel" sets status Cancelled; delete allowed only when showing has no bookings.
- Prices are in SEK, stored in appsettings.
- Booking reference is a short random alphanumeric code (see open questions).

## Out of scope
Real payment, email notifications, printed tickets, mobile app, discounts, loyalty. Change requests are handled later as new backlog items.
