# US-140: Seed data and committed SQLite database
**Status:** Ready for UAT · **Category:** data_migration · **Points:** 3 · **Sprint:** —

## Summary
A reviewer can open the committed `newtons.db` and log in immediately with six demo accounts. The database holds three auditoriums, ten movies, showings for the next 14 days and sample bookings. In Development, startup tops up showings for any missing day in the next 14 (Stockholm time), without touching existing data.

## What was built
- `Data/DbSeeder.cs`: `SeedAsync` creates roles, accounts with permission claims, auditoriums with seats, movies, showings and bookings. It is idempotent.
- `Program.cs`: in Development, runs `Database.MigrateAsync()` and then `SeedAsync` (controlled by `Seed:Enabled`, default true).
- `Services/BookingService.cs`: static `GenerateReference()` as the shared 8-character reference generator; `NewReference()` delegates to it.
- `newtons.db` (API project root): generated seed database, committed to git.
- `appsettings.json`: `ConnectionStrings:Default = Data Source=newtons.db` and `Seed:Password` (demo value).
- `.gitignore`: `!newtons.db` added after `*.db`; `*.db-shm` and `*.db-wal` remain ignored.
- `tests/NewtonsBiograf.Tests/SeedTests.cs`: tests for each AC, the top-up and the missing-password case.
- `tests/NewtonsBiograf.Tests/Infrastructure/TestEnvironment.cs`: sets `Seed__Enabled=false` so test hosts do not seed.
- `README.md`: seeded accounts, demo password, startup seeding and top-up, new config keys.

## How it works
- **AC 1 Accounts:** one Admin (`admin@newtons.local`, no claims, full access); two Staff: `staff1@newtons.local` with `ManageMovies` and `ManageShowings`, `staff2@newtons.local` with `ManageBookings`; three Customers (`customer1..3@newtons.local`). All use the one demo password from `Seed:Password`.
- **AC 2 Content counts:** three auditoriums (Salong 1, Salong 2, Salong 3 VIP; the VIP one has VIP rows at the end), ten movies across several genres and age ratings, at least two showings per day for the next 14 days, and Confirmed and Cancelled bookings.
- **AC 3 Valid data:** showings in an auditorium never overlap. Each showing ends at start + length + 15 min. No two active bookings hold the same seat for the same showing. Booking totals match seat prices (Standard 140, VIP 200).
- **AC 4 Idempotent:** accounts, auditoriums and movies are guarded by natural key (email, name, title). If any showing exists, showings and bookings are skipped. A second run leaves the row counts unchanged.
- **AC 5 Committed DB:** `newtons.db` is tracked by git. A fresh clone runs in Development with seeded data.
- **Top-up (Development only):** at startup, for each of the next 14 days (Stockholm time) with no showing, the seeder adds showings using the same generator and overlap guard. Existing showings and bookings are left unchanged, so past showings stay as booking history. Production never runs the seeder.

## How to test
**Checks:** `npm run typecheck --silent`, `npm run test --silent`, `npm run build --silent`. Seeder tests only: `dotnet test --filter "FullyQualifiedName~NewtonsBiograf.Tests.SeedTests"`.

- **AC 1 Accounts:** run `dotnet run --project src/NewtonsBiograf.Api`, then log in through `POST /api/auth/login` for each of the six accounts with password `Newtons-Demo-2026`. **Verify:** each returns 200; admin role is Admin; staff1 has ManageMovies and ManageShowings; staff2 has ManageBookings; customers have no permissions.
- **AC 2 Content counts:** call `GET /api/auditoriums`, `GET /api/movies`, `GET /api/public/showings`. **Verify:** three auditoriums (Salong 3 VIP has VIP rows), ten movies, showings on each of the next 14 days. As staff2, `GET /api/bookings?showingId=…` shows a Confirmed and a Cancelled booking.
- **AC 3 Valid data:** `SeedTests.Ac3_…`. **Verify:** test passes. In the seat map of a seeded booked showing, seats are taken and prices are 140 and 200.
- **AC 4 Idempotent:** restart the API twice. **Verify:** showing and booking counts stay the same (`SeedTests.Ac4_Running_the_seed_twice…`).
- **AC 5 DB committed:** `git ls-files src/NewtonsBiograf.Api/newtons.db` lists the file. Then fresh clone and `dotnet run`. **Verify:** log in as every account with no setup.
- **Top-up:** start the API on a copy whose showings are older than 14 days. **Verify:** new showings exist for the next 14 days; old showings and bookings are unchanged.

## Deployment notes
- **Pre-deploy:** none.
- **Post-deploy:** none for production. The seed never runs in Production.
- **Configuration:** `ConnectionStrings:Default = Data Source=newtons.db`; `Seed:Enabled` (default true, Development only); `Seed:Password` (demo value, documented in README).
- **Regenerating the file:** delete local `newtons.db*`, run `dotnet run --project src/NewtonsBiograf.Api` in Development until it listens, stop it, then `git add src/NewtonsBiograf.Api/newtons.db`. Do not commit `*.db-shm` or `*.db-wal`.
- **Without the committed file:** the app creates and seeds `newtons.db` on the first Development start.

## Known limitations
- The seed password is for demo use only and must not be used anywhere else.
- The top-up runs only in Development. The committed file is a snapshot; in other environments, showings do not refresh.
- Showings are seeded for the next 14 days from the seed date; the top-up keeps future coverage in Development only.
- The fresh-clone `dotnet run` and login check (AC 5) is a manual step, not covered by automated tests.
- The binary contents of `newtons.db` are not tested; tests seed an in-memory database instead.
