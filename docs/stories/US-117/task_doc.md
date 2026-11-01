# US-117: Data model, generic repository and initial migration
**Status:** Ready for UAT · **Category:** development · **Points:** 5 · **Sprint:** not recorded

## Summary
The database schema for the cinema domain (movies, auditoriums, seats, showings, bookings and booking seats) and the Identity tables now exist as an EF Core model with a SQLite initial migration. Features share one generic async repository, `IRepository<T>`, for data access. No end-user change is visible yet.

## What was built
- `Shared/Enums.cs`: `Genre`, `AgeRating`, `SeatType`, `ShowingStatus`, `BookingStatus`. Stored as strings.
- `Api/Data/ApplicationUser.cs`: `ApplicationUser : IdentityUser`, adds `DisplayName` (max 100, required).
- `Api/Data/Entities.cs`: `Movie`, `Auditorium`, `Seat`, `Showing`, `Booking`, `BookingSeat`.
- `Api/Data/AppDbContext.cs`: `IdentityDbContext<ApplicationUser>` with DbSets and fluent configuration, including the unique and filtered indexes.
- `Api/Data/IRepository.cs`, `Api/Data/Repository.cs`: generic async repository over `AppDbContext`.
- `Api/Program.cs`: SQLite `AddDbContext` from `ConnectionStrings:Default`; open-generic `IRepository<>` registration.
- `Api/Migrations/20261101090000_InitialCreate.cs` and `AppDbContextModelSnapshot.cs`: hand-written initial migration and snapshot.
- `tests/NewtonsBiograf.Tests/DataTests.cs`: in-memory SQLite tests.

## How it works
- **AC 1 (schema):** Applying the migration creates all entity tables, the Identity tables and the constraints. Not yet verified by running the migration (see Known limitations).
- **AC 2 (unique constraints):**
  - `IX_Auditorium_Name` rejects a second Auditorium with the same name (`DbUpdateException`).
  - A unique index on `BookingSeat (ShowingId, SeatId)` filtered by `IsActive = 1` rejects a second active booking seat for the same showing and seat.
  - A booking seat with `IsActive = false` does not block a new active one.
- **AC 3 (repository):** `IRepository<T>` provides `GetByIdAsync`, `ListAsync`, `Query()`, `AddAsync`, `Update`, `Remove` and `SaveChangesAsync`. Async methods accept a `CancellationToken`. It is registered once as an open generic.

## How to test
- Run `dotnet test NewtonsBiograf.sln` (DataTests, AC 1 to AC 3). Note: this is the first compile of the code; no checks have been run yet.
- **AC 1:** Run `dotnet ef database update --project src/NewtonsBiograf.Api`. **Verify:** `newtonsbiograf.db` contains Movie, Auditorium, Seat, Showing, Booking, BookingSeat and the AspNet* tables.
- **AC 2:** In the database, insert two Auditorium rows with the same Name. Insert two BookingSeat rows with the same ShowingId and SeatId and IsActive = 1. **Verify:** both inserts fail with a UNIQUE constraint error. Set the first row's IsActive to 0; the second insert then succeeds.
- **AC 3:** **Verify:** `IRepository<T>` resolves from DI for any entity, and every Task-returning method takes a `CancellationToken`.

## Deployment notes
- **Post-deploy:** run `dotnet ef database update --project src/NewtonsBiograf.Api`. Startup auto-migrate arrives with the seed story.
- **Post-deploy check:** the migration and snapshot were written by hand. Run `dotnet ef migrations add Check --project src/NewtonsBiograf.Api`. It must generate an empty migration. Then remove it with `dotnet ef migrations remove`. If it is not empty, regenerate `InitialCreate`.
- **Configuration:** `ConnectionStrings:Default` = `Data Source=newtonsbiograf.db`. Local `*.db` files are excluded in `.gitignore`.

## Known limitations
- The migration has not been run. The `.Designer.cs` file is missing.
- `BookingSeat.IsActive` has no database-level default; the `true` default is set only on the CLR property.
- Prices are stored as doubles (`HasConversion<double>()`). Prices are whole SEK, so the risk is low.
- The project has no checks configured. Nothing has been compiled or tested yet.
- Business services and seed data are out of scope and deferred.
