# Technical overview

## Solution structure

- `src/NewtonsBiograf.Shared/Enums.cs`: shared enums `Genre`, `AgeRating`, `SeatType`, `ShowingStatus`, `BookingStatus` (stored as strings).
- `src/NewtonsBiograf.Api/Data/`: `AppDbContext` (Identity-based), `ApplicationUser`, entities in `Entities.cs`, and the generic repository `IRepository<T>` / `Repository<T>`.
- `src/NewtonsBiograf.Api/Migrations/`: `InitialCreate` (hand-written, see Known gaps) and the model snapshot.
- `tests/NewtonsBiograf.Tests/DataTests.cs`: data layer tests using in-memory SQLite.

### Data model (US-117)
| Entity | Key points |
|---|---|
| Movie | Title (200, required); Genre; AgeRating; LengthMinutes (> 0 check); Description (2000) |
| Auditorium | Name (100, required, unique `IX_Auditorium_Name`); Rows; SeatsPerRow |
| Seat | AuditoriumId FK (cascade); RowNumber; SeatNumber; Type; unique (AuditoriumId, RowNumber, SeatNumber) |
| Showing | MovieId FK and AuditoriumId FK (restrict); StartTime and EndTime (UTC); Status (default Scheduled); index (AuditoriumId, StartTime) |
| Booking | Reference (8, required, unique); UserId FK to AspNetUsers (restrict); ShowingId FK (restrict); TotalPrice (decimal(10,2), stored as double); Status; CreatedAt; CancelledAt (nullable) |
| BookingSeat | BookingId FK (cascade); ShowingId; SeatId FK (restrict); Price; IsActive (default true); filtered unique (ShowingId, SeatId) where IsActive = 1 |

Enum values are stored as strings: Genre (Action, Comedy, Drama, Horror, SciFi, Animation, Documentary, Thriller); AgeRating (AllAges, Age7, Age11, Age15); SeatType (Standard, VIP); ShowingStatus (Scheduled, Cancelled); BookingStatus (Confirmed, Cancelled). All dates are UTC.

### Repository (US-117)
`IRepository<T> where T : class` is implemented once by `Repository<T>` over `AppDbContext`. It is registered open-generic (`AddScoped(typeof(IRepository<>), typeof(Repository<>))`). There is no unit of work and no per-entity repository. Services use `IRepository<T>` directly.

## Configuration
| Key | Value | Notes |
|---|---|---|
| `ConnectionStrings:Default` | `Data Source=newtonsbiograf.db` | SQLite file; local `*.db` files are git-ignored |

## Configuration
| Key | File | Default | Purpose |
|---|---|---|---|
| `Cors:ClientOrigin` | `src/NewtonsBiograf.Api/appsettings.json` | `https://localhost:5001` | Origin allowed by the `Client` CORS policy |
| `ApiBaseUrl` | `src/NewtonsBiograf.Client/wwwroot/appsettings.json` | `https://localhost:7000/` | Base address of the API for the named `HttpClient` |

## API endpoints
| Method | Route | Auth | Response |
|---|---|---|---|
| GET | `/api/health` | Anonymous | 200 `{ "status": "ok" }` |

## Known gaps

- US-117: the initial migration `InitialCreate` and its snapshot are hand-written and have no `.Designer.cs`. They have not been run. Before release: run `dotnet test NewtonsBiograf.sln`, then `dotnet ef migrations add Check` (must be empty), then `dotnet ef migrations remove`. Regenerate `InitialCreate` if the check is not empty.
- US-117: `BookingSeat.IsActive` has no database-level default; the default exists only in the CLR property.
- No checks are configured for the project; no build or test has been run yet.

## Client design system and layout (US-118)

- **Styles:** `wwwroot/css/app.css` holds all design tokens on `:root` (`--color-*`, `--font-body`, `--font-heading`, `--space-1..6`, radius, shadow). No external fonts. The only stylesheet linked from `wwwroot/index.html` is `app.css` (plus the scoped Blazor bundle). Bootstrap is not used. Mobile breakpoint: 640px.
- **Layout:** `Layout/MainLayout.razor` (+ `.razor.css`) provides header, top nav, `<main>@Body</main>` and footer. Below 640px the nav collapses behind a `<button aria-expanded>` toggle. No sidebar.
- **Components** (`Components/`): `AppButton` (Variant Primary/Secondary/Danger), `FormField` (Label, Id, Error with `role="alert"`), `AlertBox` (Kind Info/Success/Error), `Spinner` (`role="status"`), `Card` (Title).
- **Package references:** `NewtonsBiograf.Client.csproj` has an explicit `Microsoft.Extensions.Http` reference; the Client does not compile without it.
- **Build and test scripts:** `package.json` exposes `typecheck`, `test` and `build`, which wrap `dotnet build` and `dotnet test`. No lint script.
- **Tests:** `tests/NewtonsBiograf.Tests/DesignSystemTests.cs` checks the CSS and Razor source; components are not rendered.
- **Known gaps:** FormField error is not linked via `aria-describedby`; Home.razor handles only `HttpRequestException`; visual review at 375px and desktop is pending.
