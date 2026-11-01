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
Further routes defined by feature stories.

## Security model
JWT bearer; roles Admin/Staff/Customer plus permission claims (ManageMovies, ManageShowings, ManageBookings); enforced server-side via policies (later stories). CORS restricted to `Cors:ClientOrigin`. Swagger only in Development.

## Configuration
`ConnectionStrings:Default`, `Cors:ClientOrigin` (Api); `ApiBaseUrl` (Client). Prices in appsettings (later story).

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

## Risks
| Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|
| SQLite lacks native decimal / limited ALTER support in migrations | Med | Low | Whole-SEK prices with converter; keep migrations simple; recreate DB during development |
| Filtered index provider support | Low | High | Covered by a test with real SQLite |
| No checks/CI configured | High | Med | `dotnet build`/`dotnet test` run manually; CI out of scope |
| Committed SQLite file conflicts with migrations | Med | Med | Regenerate seed DB after each migration change |
