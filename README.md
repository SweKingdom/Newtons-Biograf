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
