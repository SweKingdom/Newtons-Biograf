# Newtons Biograf

Internal web app plus API for managing movies, auditoriums and showings, and for customers to browse and book seats.

## API configuration

| Key | Default | Notes |
|---|---|---|
| `ConnectionStrings:Default` | `Data Source=newtons.db` | SQLite database, relative to the folder the API runs in (`src/NewtonsBiograf.Api`) |
| `Cors:ClientOrigin` | `https://localhost:5001` | Only origin allowed by CORS |
| `Jwt:Issuer` | `NewtonsBiograf` | Token issuer |
| `Jwt:Audience` | `NewtonsBiograf.Client` | Token audience |
| `Jwt:Key` | none | **Secret**, at least 32 characters. Set it with user-secrets or the environment variable `Jwt__Key`. The API refuses to start without it. `appsettings.Development.json` holds a clearly marked development-only key. |
| `Jwt:ExpiryMinutes` | `60` | Lifetime of a login token in minutes |
| `Seed:Password` | `Newtons-Demo-2026` | Password of every seeded demo account (demo only, see below) |
| `Seed:Enabled` | `true` | Development only: set `false` to skip the startup migration and seeding |

Example (local): `dotnet user-secrets set "Jwt:Key" "<random string of 32+ characters>" --project src/NewtonsBiograf.Api`

## Seeded demo data

The SQLite file `src/NewtonsBiograf.Api/newtons.db` is committed with demo data, so a fresh clone runs straight away: `dotnet run --project src/NewtonsBiograf.Api`. If the file is missing, the API creates and seeds it on startup.

**The password of every account below is the demo value `Newtons-Demo-2026` (`Seed:Password`). It is for demonstration only; never use it outside a demo.**

| Account | Role | Permissions |
|---|---|---|
| `admin@newtons.local` | Admin | everything |
| `staff1@newtons.local` | Staff | `ManageMovies`, `ManageShowings` |
| `staff2@newtons.local` | Staff | `ManageBookings` |
| `customer1@newtons.local`, `customer2@newtons.local`, `customer3@newtons.local` | Customer | none |

Content: three auditoriums (`Salong 1` 8×12, `Salong 2` 6×10, `Salong 3 VIP` 8×10 whose last two rows are VIP), ten movies in several genres and age ratings, three showings per auditorium on each of the next 14 days (Stockholm days, starting 12:00, each ending film length + 15 minutes after its start), and a few bookings: six Confirmed on upcoming showings, one Cancelled, and one Confirmed on a showing that has already been shown (booking history). Prices come from `Pricing` (Standard 140, VIP 200).

Startup behaviour (**Development only; Production data is never touched**):

- The API applies pending migrations, then runs the seed. The seed is idempotent: each account, auditorium, movie and showing is only added if it is not there yet, so running it twice changes nothing.
- The committed database is the starting snapshot. At each start the seed adds showings for any of the next 14 days (Stockholm time) that have none, with the same slots and the same no-overlap rule. Existing showings and bookings are left as they are, so past showings remain as booking history. The top-up adds no bookings.

## Authentication

- `POST /api/auth/register` `{ name, email, password }` creates a customer account (role `Customer`; password at least 8 characters). It returns `201` and does not log in.
- `POST /api/auth/login` `{ email, password }` returns `{ token, expiresAt, name, role, permissions }`. Wrong credentials return `401` "Invalid email or password."
- The roles `Admin`, `Staff` and `Customer` must exist in the database; registration fails with `500` otherwise. The Development seed creates them.

## Staff permissions

- `PUT /api/admin/staff/{id}/permissions` `{ permissions: [...] }` (Admin only) replaces the full permission set of a staff user and returns `204`. An empty list removes all permissions. Valid names: `ManageMovies`, `ManageShowings`, `ManageBookings`.
- Errors: `400` for an unknown permission or when the target is not in role `Staff` (an Admin or Customer), `404` for an unknown user, `401`/`403` for callers who are not an admin.
- **Changes apply on the staff member's next login.** Tokens are not revoked, so a token issued earlier keeps the old permissions until it expires (`Jwt:ExpiryMinutes`, default 60 minutes).

## Public browsing (no login)

- `GET /api/public/showings` lists non-cancelled showings starting after now and up to 14 days ahead, by start time, each with `freeSeats`. Optional filters, combined with AND: `genre` (for example `Horror` or `Sci-Fi`, any case), `date` (`yyyy-MM-dd`, a day in `Showings:DisplayTimeZone`), `auditoriumId`, `title` (partial, any case, at most 100 characters). A malformed `date` or `auditoriumId`, or a too long `title`, returns `400`; an unknown genre or auditorium, or a date outside the 14 days, returns `200` with `[]`.
- `GET /api/public/genres` and `GET /api/public/auditoriums` return the values for the filter dropdowns.
- The staff route `GET /api/showings` is unchanged and still requires `ManageShowings`.

## Testing

Run the tests locally with `dotnet test` from the repository root. The scenario list, the test names and how to run one class or one scenario are in [docs/TESTING.md](docs/TESTING.md). The test run sets `Seed__Enabled=false`, so the tests never seed or touch `newtons.db`; `SeedTests` calls the seeder directly on an in-memory database.
