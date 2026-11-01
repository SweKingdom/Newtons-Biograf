# Newtons Biograf

## 1. Overview

Newtons Biograf is an internal web app plus API for a cinema. Staff manage movies, auditoriums and showings; customers browse showings, book tickets and pick their seats.

| Persona | What they can do |
|---|---|
| Visitor (not logged in) | Browse and filter showings, open a movie page, register |
| Customer | Everything a visitor can, plus book 1–8 seats, see and cancel their own bookings |
| Staff | Work depends on the permissions an admin gave them: `ManageMovies`, `ManageShowings`, `ManageBookings` |
| Admin | Everything staff can do, plus manage auditoriums and create staff accounts / edit their permissions |

In scope: browsing, booking with a seat map, cancellation, movie/auditorium/showing management, role- and permission-based access. Out of scope: online payment, e-mail notifications, production hosting.

## 2. Technology

- .NET 8 Web API (controllers, Swagger in Development)
- EF Core with SQLite, code-first migrations
- ASP.NET Core Identity with JWT bearer tokens (HS256)
- Blazor WebAssembly client
- xUnit tests (`WebApplicationFactory`, real SQLite)

## 3. Prerequisites

The .NET 8 SDK. Trust the HTTPS development certificate once if you have not already: `dotnet dev-certs https --trust`.

## 4. Run

Use two terminals from the repository root:

```
dotnet run --project src/NewtonsBiograf.Api
dotnet run --project src/NewtonsBiograf.Client
```

| Part | URL (from `Properties/launchSettings.json`) |
|---|---|
| API | https://localhost:7000 (also http://localhost:7001) |
| Swagger (Development only) | https://localhost:7000/swagger |
| Client | https://localhost:5001 (also http://localhost:5002) |

The client calls the API at `ApiBaseUrl` (`src/NewtonsBiograf.Client/wwwroot/appsettings.json`, `https://localhost:7000/`). The API only accepts the client origin `Cors:ClientOrigin` (`https://localhost:5001`), so use the https client URL.

The API uses the SQLite file named by `ConnectionStrings:Default` (`Data Source=newtons.db`, relative to the folder the API runs in, `src/NewtonsBiograf.Api`). That file is committed with demo data, so a fresh clone runs straight away and you can log in with the accounts in section 6. In Development the API applies pending migrations and seeds the demo data at startup; if the file is missing it is created and seeded. To start over, stop the API and delete `newtons.db*` (or restore it with `git checkout src/NewtonsBiograf.Api/newtons.db`).

## 5. Configuration

API settings (`src/NewtonsBiograf.Api/appsettings.json`):

| Key | Default | Notes |
|---|---|---|
| `ConnectionStrings:Default` | `Data Source=newtons.db` | SQLite database file, relative to the folder the API runs in |
| `Cors:ClientOrigin` | `https://localhost:5001` | Only origin allowed by CORS |
| `Jwt:Issuer` / `Jwt:Audience` | `NewtonsBiograf` / `NewtonsBiograf.Client` | Token issuer and audience |
| `Jwt:Key` | none | **Secret**, at least 32 characters; the API refuses to start without it. Set it with user-secrets or `Jwt__Key`. `appsettings.Development.json` holds a clearly marked development-only key. |
| `Jwt:ExpiryMinutes` | `60` | Token lifetime in minutes |
| `Pricing:StandardSeat` / `Pricing:VipSeat` | `140` / `200` | Seat prices in SEK |
| `Booking:CancellationLimitHours` | `2` | A customer may cancel until this many hours before the start |
| `Showings:CleaningMinutes` | `15` | Added to the film length to get a showing's end time |
| `Showings:DisplayTimeZone` | `Europe/Stockholm` | Time zone used to show times and to filter by date |
| `Seed:Password` | `Newtons-Demo-2026` | Password of every seeded demo account (demo only, see section 6) |
| `Seed:Enabled` | `true` | Development only: set `false` to skip the startup migration and seeding |

Example: `dotnet user-secrets set "Jwt:Key" "<random string of 32+ characters>" --project src/NewtonsBiograf.Api`

## 6. Seeded accounts

The SQLite file `src/NewtonsBiograf.Api/newtons.db` is committed with demo data. **The password of every account below is the demo value `Newtons-Demo-2026` (`Seed:Password`). It is for demonstration only; never use it outside a demo.**

| Email | Role | Permissions | Password |
|---|---|---|---|
| `admin@newtons.local` | Admin | All (implicit) | `Newtons-Demo-2026` |
| `staff1@newtons.local` | Staff | `ManageMovies`, `ManageShowings` | `Newtons-Demo-2026` |
| `staff2@newtons.local` | Staff | `ManageBookings` | `Newtons-Demo-2026` |
| `customer1@newtons.local` | Customer | none | `Newtons-Demo-2026` |
| `customer2@newtons.local` | Customer | none | `Newtons-Demo-2026` |
| `customer3@newtons.local` | Customer | none | `Newtons-Demo-2026` |

Content: three auditoriums (`Salong 1` 8×12, `Salong 2` 6×10, `Salong 3 VIP` 8×10 whose last two rows are VIP), ten movies in several genres and age ratings, three showings per auditorium on each of the next 14 days (Stockholm days, starting 12:00, each ending film length + 15 minutes after its start), and a few bookings: six Confirmed on upcoming showings, one Cancelled, and one Confirmed on a showing that has already been shown (booking history). Prices come from `Pricing` (Standard 140, VIP 200).

Startup behaviour (**Development only; Production data is never touched**):

- The API applies pending migrations, then runs the seed. The seed is idempotent: each account, auditorium, movie and showing is only added if it is not there yet, so running it twice changes nothing.
- The committed database is the starting snapshot. At each start the seed adds showings for any of the next 14 days (Stockholm time) that have none, with the same slots and the same no-overlap rule. Existing showings and bookings are left as they are, so past showings remain as booking history. The top-up adds no bookings.

## 7. User scenarios

The scenarios describe the screens as they exist in the client. They use the accounts from section 6. Log in through **Log in** in the top menu (fields **Email** and **Password**).

### Scenario 1: A customer books seats

1. Log in as `customer1@newtons.local`.
2. On **Home** ("Now showing at Newtons Biograf") use **Find a showing**: pick a **Genre**, a **Date**, an **Auditorium** or type part of a **Title**. **Clear filters** resets them.
3. Click **Book** on the showing you want (a sold-out showing says "Sold out" and cannot be booked).
4. On **Choose your seats** click between 1 and 8 free seats on the seat map. VIP rows cost more; the summary shows the number of seats and the **Total**.
5. Click **Book**. **Booking confirmed** shows the 8-character **Reference**, the seats and the total.
6. Click **View my bookings** (or **My bookings** in the menu) to see it under **Upcoming**. Cancelled and past bookings are under **History**.

### Scenario 2: Staff creates a showing

1. Log in as `staff1@newtons.local` (permission `ManageShowings`).
2. Open **Showings** in the menu and click **Schedule showing**.
3. Choose a **Movie**, an **Auditorium**, a **Date** in the future and a **Start time**. The page previews the end: film length plus the cleaning time (15 minutes).
4. Click **Save**. The showing appears in the list with status Scheduled; its end is start + film length + 15 minutes.
5. Schedule another showing in the same auditorium at an overlapping time. The form shows an error that names the conflicting showing, for example `Overlaps with '<title>' 19:00–20:55 in <auditorium>`, and nothing is saved.

### Scenario 3: An admin creates a staff account

1. Log in as `admin@newtons.local`.
2. Open **Staff** in the menu. Under **Create staff account** fill in **Name**, **Email** and **Password** (at least 8 characters) and tick the permissions the person should have.
3. Click **Create staff account**. The new person appears under **Staff accounts**; **Edit permissions** changes them later (they apply at the person's next login).
4. Log out, log in as the new staff member and check that the menu only shows the areas for the ticked permissions (for example **Movies** for `ManageMovies`). The API enforces the same rules: a request without the permission gets `403`.

## 8. Technique map

| Technique | Where | What |
|---|---|---|
| Generics | `src/NewtonsBiograf.Api/Data/IRepository.cs`, `Data/Repository.cs` | `IRepository<T>` / `Repository<T>` with `Expression<Func<T, bool>>` filters; registered as an open generic in `Program.cs` (`AddScoped(typeof(IRepository<>), typeof(Repository<>))`) |
| Generics | `Controllers/MoviesController.cs`, `Controllers/ShowingsController.cs`, `Controllers/AuditoriumsController.cs` | Constructors take `IRepository<Movie>`, `IRepository<Showing>`, `IRepository<Auditorium>` and others |
| Generics | `src/NewtonsBiograf.Client/Services/PublicApiClient.cs` | `PublicApiClient.GetAsync<T>` typed JSON GET, used by `BookingApiClient` |
| Delegates / events | `src/NewtonsBiograf.Api/Events/BookingEvents.cs` | Custom delegate `BookingEventHandler`, event `BookingCreated`, `BookingEventPublisher.RaiseCreated` calls each subscriber through `GetInvocationList()` and logs one that throws |
| Delegates / events | `Events/BookingLogSubscriber.cs` | Subscriber: `publisher.BookingCreated += OnBookingCreated` |
| Delegates / events | `Services/BookingService.cs` | `BookingService.CreateAsync` raises `BookingCreated`; cancellation is reported through the `IBookingEvents.BookingCancelledAsync` subscriber interface (`LoggingBookingEvents`) |
| Delegates / events | `src/NewtonsBiograf.Client/Pages/Staff/Showings.razor` | `RunAsync(Func<Task<ShowingResult>> call, …)` takes the cancel/delete call as a delegate |
| LINQ | `Controllers/PublicController.cs` | `Showings` builds the search/filter query (`Where`, `OrderBy`, `Select`); `Genres`, `Auditoriums` |
| LINQ | `Controllers/ShowingsController.cs` | `List` (date range, auditorium, `GroupBy` for booking counts) |
| LINQ | `Services/BookingService.cs` | `GetMineAsync` (upcoming / history lists), `SearchAsync`, `CreateAsync` (seat price lines), the `ToDto` projection |
| async/await | `Data/Repository.cs`, all controllers | `ToListAsync`, `FindAsync`, `SaveChangesAsync`, `AnyAsync` with `CancellationToken` |
| async/await | `Services/ShowingService.cs` | `CancelShowingAsync` runs a database transaction asynchronously |
| async/await | `src/NewtonsBiograf.Client/Services/BookingApiClient.cs` | `CancelAsync` and the other `HttpClient` calls; `Program.cs` ends with `await builder.Build().RunAsync()` |

## 9. Testing

```
dotnet test
```

The suite covers the API, the client services and the core business rules (booking, double booking, cancellation limits, showing overlap, access) against a real SQLite database. `npm run typecheck`, `npm run test` and `npm run build` run the same solution-level checks. The scenario list, the test names and how to run one class or one scenario are in [docs/TESTING.md](docs/TESTING.md). The test run sets `Seed__Enabled=false`, so the tests never seed or touch `newtons.db`; `SeedTests` calls the seeder directly on an in-memory database.

## 10. Project structure

| Project | Content |
|---|---|
| `src/NewtonsBiograf.Api` | `Program.cs`, `Controllers/`, `Services/`, `Events/`, `Auth/`, `Pricing/`, `Data/` (`AppDbContext`, entities, `IRepository<T>`, `DbSeeder`), `Migrations/`, `newtons.db` (committed demo database) |
| `src/NewtonsBiograf.Client` | Blazor WebAssembly: `Layout/`, `Components/`, `Pages/`, `Services/` (typed API clients), `Auth/` |
| `src/NewtonsBiograf.Shared` | DTOs, enums, permission constants shared by API and client |
| `tests/NewtonsBiograf.Tests` | xUnit tests: API, client, and `Scenarios/` for the core business rules |

## API notes

Short notes on behaviour you may meet in Swagger (Swagger is the API reference).

- **Registration and login.** `POST /api/auth/register` `{ name, email, password }` creates a `Customer` (password at least 8 characters) and does not log in. `POST /api/auth/login` `{ email, password }` returns `{ token, expiresAt, name, role, permissions }`; wrong credentials return `401` "Invalid email or password." The roles `Admin`, `Staff` and `Customer` must exist in the database, otherwise registration fails with `500`; the Development seed creates them.
- **Staff permissions.** `PUT /api/admin/staff/{id}/permissions` `{ permissions: [...] }` (Admin only) replaces a staff member's permissions and returns `204`. Valid names: `ManageMovies`, `ManageShowings`, `ManageBookings`. Errors: `400` for an unknown permission or a target that is not Staff, `404` for an unknown user, `401`/`403` for non-admins. Changes apply at the staff member's next login; tokens are not revoked and keep the old permissions until they expire.
- **Public browsing (no login).** `GET /api/public/showings` lists non-cancelled showings starting after now and up to 14 days ahead, each with `freeSeats`. Optional filters, combined with AND: `genre` (any case), `date` (`yyyy-MM-dd`, a day in `Showings:DisplayTimeZone`), `auditoriumId`, `title` (partial, any case, at most 100 characters). A malformed `date` or `auditoriumId`, or a too long `title`, returns `400`; an unknown genre or auditorium, or a date outside the 14 days, returns `200` with `[]`. `GET /api/public/genres` and `GET /api/public/auditoriums` feed the filter dropdowns. The staff route `GET /api/showings` still requires `ManageShowings`.
