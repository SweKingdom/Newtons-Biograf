# Newtons Biograf

Internal web app plus API for managing movies, auditoriums and showings, and for customers to browse and book seats.

## API configuration

| Key | Default | Notes |
|---|---|---|
| `ConnectionStrings:Default` | `Data Source=newtonsbiograf.db` | SQLite database |
| `Cors:ClientOrigin` | `https://localhost:5001` | Only origin allowed by CORS |
| `Jwt:Issuer` | `NewtonsBiograf` | Token issuer |
| `Jwt:Audience` | `NewtonsBiograf.Client` | Token audience |
| `Jwt:Key` | none | **Secret**, at least 32 characters. Set it with user-secrets or the environment variable `Jwt__Key`. The API refuses to start without it. `appsettings.Development.json` holds a clearly marked development-only key. |
| `Jwt:ExpiryMinutes` | `60` | Lifetime of a login token in minutes |

Example (local): `dotnet user-secrets set "Jwt:Key" "<random string of 32+ characters>" --project src/NewtonsBiograf.Api`

## Authentication

- `POST /api/auth/register` `{ name, email, password }` creates a customer account (role `Customer`; password at least 8 characters). It returns `201` and does not log in.
- `POST /api/auth/login` `{ email, password }` returns `{ token, expiresAt, name, role, permissions }`. Wrong credentials return `401` "Invalid email or password."
- The roles `Admin`, `Staff` and `Customer` must exist in the database; registration fails with `500` otherwise.

## Staff permissions

- `PUT /api/admin/staff/{id}/permissions` `{ permissions: [...] }` (Admin only) replaces the full permission set of a staff user and returns `204`. An empty list removes all permissions. Valid names: `ManageMovies`, `ManageShowings`, `ManageBookings`.
- Errors: `400` for an unknown permission or when the target is not in role `Staff` (an Admin or Customer), `404` for an unknown user, `401`/`403` for callers who are not an admin.
- **Changes apply on the staff member's next login.** Tokens are not revoked, so a token issued earlier keeps the old permissions until it expires (`Jwt:ExpiryMinutes`, default 60 minutes).

## Public browsing (no login)

- `GET /api/public/showings` lists non-cancelled showings starting after now and up to 14 days ahead, by start time, each with `freeSeats`. Optional filters, combined with AND: `genre` (for example `Horror` or `Sci-Fi`, any case), `date` (`yyyy-MM-dd`, a day in `Showings:DisplayTimeZone`), `auditoriumId`, `title` (partial, any case, at most 100 characters). A malformed `date` or `auditoriumId`, or a too long `title`, returns `400`; an unknown genre or auditorium, or a date outside the 14 days, returns `200` with `[]`.
- `GET /api/public/genres` and `GET /api/public/auditoriums` return the values for the filter dropdowns.
- The staff route `GET /api/showings` is unchanged and still requires `ManageShowings`.

## Testing

Run the tests locally with `dotnet test` from the repository root. The scenario list, the test names and how to run one class or one scenario are in [docs/TESTING.md](docs/TESTING.md).
