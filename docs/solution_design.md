# Solution Design — Newtons Biograf

Scope of this version: foundation (FT-106: US-116, US-117, US-118). Other features add sections later.

## Architecture overview
Blazor WebAssembly client (`NewtonsBiograf.Client`) ↔ .NET 8 Web API (`NewtonsBiograf.Api`, controllers, JWT bearer) ↔ EF Core + SQLite. Shared DTOs/enums in `NewtonsBiograf.Shared`. xUnit scenario tests in `NewtonsBiograf.Tests` using `WebApplicationFactory`. No external integrations.

## Module map
| Project | Content |
|---|---|
| `src/NewtonsBiograf.Api` | Program.cs, Controllers/, Data/ (AppDbContext, ApplicationUser, Entities, IRepository, Repository), Migrations/ |
| `src/NewtonsBiograf.Client` | Layout/, Components/ (AppButton, FormField, AlertBox, Spinner, Card), Pages/, wwwroot/css/app.css |
| `src/NewtonsBiograf.Shared` | DTOs, enums (Genre, AgeRating, SeatType, ShowingStatus, BookingStatus) |
| `tests/NewtonsBiograf.Tests` | xUnit tests; in-memory SQLite with migrations |

## Data model
Movie, Auditorium (unique Name), Seat (unique Auditorium+Row+Number), Showing (UTC times), Booking (unique 8-char Reference), BookingSeat (filtered unique ShowingId+SeatId WHERE IsActive=1), ApplicationUser (Identity + DisplayName). Enums stored as strings. Details in US-117.

## API inventory

| Route | Auth | Story |
|---|---|---|
| `GET /api/health` | anonymous | US-116 |
| `POST /api/auth/register` | anonymous; 201, 400 | US-119 |
| `POST /api/auth/login` | anonymous; 200 `{Token, ExpiresAt, Name, Role, Permissions[]}`, 401 | US-119 |
| `POST /api/admin/staff` | policy `AdminOnly`; 201, 400 | US-121 |
| `GET /api/admin/staff` | policy `AdminOnly` | US-121 |
| `PUT /api/admin/staff/{id}/permissions` | policy `AdminOnly`; 204, 400, 404 | US-122 |

Further routes defined by feature stories. All errors use ProblemDetails / ValidationProblemDetails (`application/problem+json`).

## Security model

- **Authentication:** ASP.NET Core Identity (`AddIdentityCore` + roles, EF stores); HS256 JWT from `Auth/TokenService.cs`. Claims: `sub`, `email`, `name`, role (`ClaimTypes.Role`, one of Admin/Staff/Customer), one `permission` claim per permission. `MapInboundClaims=false`. Default lifetime 60 min (`Jwt:ExpiryMinutes`, assumption).
- **Passwords:** length ≥ 8 only; unique email = username. Login returns the same 401 message for unknown user and wrong password.
- **Authorization (US-120):** policies `AdminOnly`, `CustomerOnly`, `ManageMovies`, `ManageShowings`, `ManageBookings`. Permission policies are `RequireAssertion`: Admin role OR claim `permission`=name. Constants in `Shared/Permissions.cs` (claim type, 3 values, `All`) used by Api and Client. No global fallback policy; each non-public controller has explicit `[Authorize]`.
- **Errors:** 401/403 return `application/problem+json` via `JwtBearerEvents.OnChallenge/OnForbidden`; no redirects.
- **Privilege:** registration always yields Customer and ignores role fields; only Admin creates Staff and edits permissions (US-121/122); permission edits only for users in role Staff.
- **Token revocation:** none; permission changes apply on next login/token expiry (accepted).
- **Client:** token in `localStorage` (`nb_token`), attached by `AuthMessageHandler`; UI hiding is cosmetic, API enforces. XSS exposure of localStorage accepted for this internal app.
- CORS restricted to `Cors:ClientOrigin`. Swagger only in Development.

## Configuration

`ConnectionStrings:Default`, `Cors:ClientOrigin` (Api); `ApiBaseUrl` (Client). `Jwt:Issuer` (`NewtonsBiograf`), `Jwt:Audience` (`NewtonsBiograf.Client`), `Jwt:Key` (secret, ≥32 chars, user-secrets/env `Jwt__Key`; startup fails if missing), `Jwt:ExpiryMinutes` (60). Prices in appsettings (later story).

## Integration inventory
None external.

## Key decisions

| Decision | Reason | Alternatives |
|---|---|---|
| Controllers rather than minimal APIs | Declarative authorization, familiar structure | Minimal APIs |
| Single generic `IRepository<T>`, no per-entity repos/unit of work | Meets generics requirement, lean | Specific repositories |
| Keep BookingSeat rows on cancel with `IsActive` flag + filtered unique index | Preserves history, frees seats, DB still prevents double booking | Delete rows on cancel |
| Enums stored as strings | Readable DB, safe against reorder | int |
| UTC datetimes everywhere | Avoid timezone bugs; client converts for display | local time |
| Custom CSS variables design, no Bootstrap | Requirement: not template-like | Bootstrap theme |
| Permission policies via `RequireAssertion` (Admin OR claim), no requirement/handler classes | One rule, fewer files | `IAuthorizationHandler` |
| Identity used directly in controllers (no StaffService/AuthService) | Thin logic, lean | Service layer |
| Permissions as Identity user claims (type `permission`) copied into the JWT | Standard Identity storage; admin editing needs no new tables | Custom permission table |
| No token revocation; permissions effective on next login | Not specified; short lifetime | Refresh tokens / security stamp check per request |
| Register returns 201 without token | Keeps login a single path | Auto-login |
| Permission constants in `Shared` | Client and API share one definition | Duplicate strings |

## Risks
| Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|
| SQLite lacks native decimal / limited ALTER support in migrations | Med | Low | Whole-SEK prices with converter; keep migrations simple; recreate DB during development |
| Filtered index provider support | Low | High | Covered by a test with real SQLite |
| No checks/CI configured | High | Med | `dotnet build`/`dotnet test` run manually; CI out of scope |
| Committed SQLite file conflicts with migrations | Med | Med | Regenerate seed DB after each migration change |
