using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using NewtonsBiograf.Api.Services;
using NewtonsBiograf.Shared;

namespace NewtonsBiograf.Api.Data;

/// <summary>
/// Demo data for Development: accounts, auditoriums, movies, then showings for the next <see cref="DaysAhead"/> days
/// (Stockholm days) and a few bookings. Every entity is guarded by its natural key, so running it again changes nothing.
/// Showings are topped up for any of the next days that has none; bookings are only seeded together with the very first
/// showings. It writes through the context, so it keeps the showing and booking rules itself: no overlap, End = Start +
/// length + cleaning, one active booking per (showing, seat), totals equal to the seat prices. Times are UTC.
/// </summary>
public static class DbSeeder
{
    public const int DaysAhead = 14;
    private const int SlotsPerAuditoriumPerDay = 3;
    private static readonly TimeSpan FirstSlot = TimeSpan.FromHours(12);

    private static readonly (string Email, string Name, string Role, string[] Permissions)[] Accounts =
    [
        ("admin@newtons.local", "Admin", Roles.Admin, []),
        ("staff1@newtons.local", "Staff One", Roles.Staff, [Permissions.ManageMovies, Permissions.ManageShowings]),
        ("staff2@newtons.local", "Staff Two", Roles.Staff, [Permissions.ManageBookings]),
        ("customer1@newtons.local", "Customer One", Roles.Customer, []),
        ("customer2@newtons.local", "Customer Two", Roles.Customer, []),
        ("customer3@newtons.local", "Customer Three", Roles.Customer, [])
    ];

    private static readonly (string Name, int Rows, int SeatsPerRow, int VipRows)[] AuditoriumSeeds =
    [
        ("Salong 1", 8, 12, 0),
        ("Salong 2", 6, 10, 0),
        ("Salong 3 VIP", 8, 10, 2)
    ];

    private static readonly (string Title, Genre Genre, AgeRating Rating, int Minutes, string Description)[] MovieSeeds =
    [
        ("The Last Lighthouse", Genre.Drama, AgeRating.Age11, 118, "A keeper and a stranded sailor share one stormy winter."),
        ("Galaxy Sprouts", Genre.Animation, AgeRating.AllAges, 92, "Tiny plants from space learn to grow on Earth."),
        ("Chase Protocol", Genre.Action, AgeRating.Age15, 126, "A courier outruns a city-wide blackout."),
        ("Laugh Track", Genre.Comedy, AgeRating.Age7, 98, "A failing sitcom crew tries one last live show."),
        ("Beneath the Pines", Genre.Horror, AgeRating.Age15, 104, "A hiking trip takes a turn in a silent forest."),
        ("Orbit Nine", Genre.SciFi, AgeRating.Age11, 148, "The ninth crew of a deep-space station finds a message."),
        ("A Year in Lapland", Genre.Documentary, AgeRating.AllAges, 90, "Four seasons under the northern lights."),
        ("The Quiet Witness", Genre.Thriller, AgeRating.Age15, 134, "The only person who saw it will not speak."),
        ("Grandma's Great Escape", Genre.Comedy, AgeRating.AllAges, 101, "A retirement home breaks out for one wild weekend."),
        ("Echoes of Tomorrow", Genre.SciFi, AgeRating.Age11, 170, "A signal from the future changes a small town.")
    ];

    /// <summary>Auditorium index, nth upcoming showing in it, customer, row, first seat, seat count, cancelled.</summary>
    private static readonly (int Auditorium, int Showing, string Customer, int Row, int FirstSeat, int Count, bool Cancelled)[] BookingSeeds =
    [
        (0, 0, "customer1@newtons.local", 5, 5, 2, false),
        (0, 4, "customer2@newtons.local", 4, 1, 4, false),
        (1, 1, "customer1@newtons.local", 3, 4, 2, false),
        (1, 5, "customer3@newtons.local", 6, 5, 1, false),
        (2, 2, "customer2@newtons.local", 7, 4, 2, false),
        (2, 6, "customer3@newtons.local", 8, 3, 3, false),
        (0, 1, "customer3@newtons.local", 5, 5, 2, true)
    ];

    public static async Task SeedAsync(AppDbContext db, UserManager<ApplicationUser> users, RoleManager<IdentityRole> roles,
        IConfiguration config, DateTime nowUtc)
    {
        await SeedUsersAsync(users, roles, config);
        await SeedAuditoriumsAndMoviesAsync(db);
        await SeedShowingsAsync(db, users, config, nowUtc);
    }

    private static async Task SeedUsersAsync(UserManager<ApplicationUser> users, RoleManager<IdentityRole> roles, IConfiguration config)
    {
        var password = config["Seed:Password"];
        if (string.IsNullOrEmpty(password))
        {
            throw new InvalidOperationException("Configuration value 'Seed:Password' is missing.");
        }

        foreach (var role in new[] { Roles.Admin, Roles.Staff, Roles.Customer })
        {
            if (!await roles.RoleExistsAsync(role))
            {
                Check(await roles.CreateAsync(new IdentityRole(role)), $"role {role}");
            }
        }

        foreach (var (email, name, role, permissions) in Accounts)
        {
            if (await users.FindByEmailAsync(email) is not null)
            {
                continue;
            }
            var user = new ApplicationUser { UserName = email, Email = email, EmailConfirmed = true, DisplayName = name };
            Check(await users.CreateAsync(user, password), $"user {email}");
            Check(await users.AddToRoleAsync(user, role), $"role of {email}");
            foreach (var permission in permissions)
            {
                Check(await users.AddClaimAsync(user, new Claim(Permissions.ClaimType, permission)), $"permission of {email}");
            }
        }
    }

    private static async Task SeedAuditoriumsAndMoviesAsync(AppDbContext db)
    {
        foreach (var (name, rows, seatsPerRow, vipRows) in AuditoriumSeeds)
        {
            if (await db.Auditoriums.AnyAsync(a => a.Name == name))
            {
                continue;
            }
            db.Auditoriums.Add(new Auditorium
            {
                Name = name, Rows = rows, SeatsPerRow = seatsPerRow,
                Seats = Enumerable.Range(1, rows).SelectMany(row => Enumerable.Range(1, seatsPerRow).Select(number => new Seat
                {
                    RowNumber = row, SeatNumber = number, Type = row > rows - vipRows ? SeatType.VIP : SeatType.Standard
                })).ToList()
            });
        }

        foreach (var (title, genre, rating, minutes, description) in MovieSeeds)
        {
            if (!await db.Movies.AnyAsync(m => m.Title == title))
            {
                db.Movies.Add(new Movie { Title = title, Genre = genre, AgeRating = rating, LengthMinutes = minutes, Description = description });
            }
        }
        await db.SaveChangesAsync();
    }

    private static async Task SeedShowingsAsync(AppDbContext db, UserManager<ApplicationUser> users, IConfiguration config, DateTime nowUtc)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(config.GetValue("Showings:DisplayTimeZone", "Europe/Stockholm")!);
        var cleaning = config.GetValue("Showings:CleaningMinutes", 15);
        var firstRun = !await db.Showings.AnyAsync();

        var names = AuditoriumSeeds.Select(a => a.Name).ToArray();
        var titles = MovieSeeds.Select(m => m.Title).ToArray();
        var auditoriums = (await db.Auditoriums.Where(a => names.Contains(a.Name)).ToListAsync())
            .OrderBy(a => Array.IndexOf(names, a.Name)).ToList();
        var movies = (await db.Movies.Where(m => titles.Contains(m.Title)).ToListAsync())
            .OrderBy(m => Array.IndexOf(titles, m.Title)).ToList();
        if (auditoriums.Count == 0 || movies.Count == 0)
        {
            return;
        }

        var today = DateTime.SpecifyKind(TimeZoneInfo.ConvertTimeFromUtc(nowUtc, zone).Date, DateTimeKind.Unspecified);
        for (var day = 0; day < DaysAhead; day++)
        {
            var date = today.AddDays(day);
            var dayStart = TimeZoneInfo.ConvertTimeToUtc(date, zone);
            var dayEnd = TimeZoneInfo.ConvertTimeToUtc(date.AddDays(1), zone);
            if (await db.Showings.AnyAsync(s => s.Status == ShowingStatus.Scheduled && s.StartTime >= dayStart && s.StartTime < dayEnd))
            {
                continue;
            }

            var busy = await db.Showings.AsNoTracking()
                .Where(s => s.Status == ShowingStatus.Scheduled && s.EndTime > dayStart && s.StartTime < dayEnd).ToListAsync();
            for (var a = 0; a < auditoriums.Count; a++)
            {
                var cursor = TimeZoneInfo.ConvertTimeToUtc(date + FirstSlot, zone);
                for (var slot = 0; slot < SlotsPerAuditoriumPerDay; slot++)
                {
                    var movie = movies[(day * 7 + a * 3 + slot) % movies.Count];
                    var start = cursor;
                    var end = start.AddMinutes(movie.LengthMinutes + cleaning);
                    cursor = RoundUpToFiveMinutes(end);
                    var auditoriumId = auditoriums[a].Id;
                    if (start <= nowUtc || busy.Any(b => b.AuditoriumId == auditoriumId && b.StartTime < end && start < b.EndTime))
                    {
                        continue;
                    }
                    var showing = new Showing { MovieId = movie.Id, AuditoriumId = auditoriumId, StartTime = start, EndTime = end };
                    db.Showings.Add(showing);
                    busy.Add(showing);
                }
            }
        }

        Showing? history = null;
        if (firstRun && auditoriums.Count == AuditoriumSeeds.Length)
        {
            var start = TimeZoneInfo.ConvertTimeToUtc(today.AddDays(-1) + FirstSlot, zone);
            history = new Showing
            {
                MovieId = movies[0].Id, AuditoriumId = auditoriums[0].Id, StartTime = start,
                EndTime = start.AddMinutes(movies[0].LengthMinutes + cleaning)
            };
            db.Showings.Add(history);
        }
        await db.SaveChangesAsync();

        if (history is not null)
        {
            await SeedBookingsAsync(db, users, config, nowUtc, auditoriums, history);
        }
    }

    private static async Task SeedBookingsAsync(AppDbContext db, UserManager<ApplicationUser> users, IConfiguration config,
        DateTime nowUtc, List<Auditorium> auditoriums, Showing history)
    {
        var standard = config.GetValue("Pricing:StandardSeat", 140m);
        var vip = config.GetValue("Pricing:VipSeat", 200m);
        var references = (await db.Bookings.Select(b => b.Reference).ToListAsync()).ToHashSet();

        async Task AddAsync(Showing showing, string email, int row, int first, int count, bool cancelled, DateTime createdAt)
        {
            var user = await users.FindByEmailAsync(email) ?? throw new InvalidOperationException($"Seed user {email} is missing.");
            var seats = await db.Seats.Where(s => s.AuditoriumId == showing.AuditoriumId && s.RowNumber == row
                && s.SeatNumber >= first && s.SeatNumber < first + count).ToListAsync();
            if (seats.Count != count)
            {
                throw new InvalidOperationException($"Seed booking for showing {showing.Id} needs {count} seats in row {row}.");
            }
            string reference;
            do
            {
                reference = BookingService.GenerateReference();
            }
            while (!references.Add(reference));

            var lines = seats.Select(s => (Seat: s, Price: s.Type == SeatType.VIP ? vip : standard)).ToList();
            db.Bookings.Add(new Booking
            {
                UserId = user.Id, ShowingId = showing.Id, Reference = reference, TotalPrice = lines.Sum(l => l.Price),
                Status = cancelled ? BookingStatus.Cancelled : BookingStatus.Confirmed, CreatedAt = createdAt,
                CancelledAt = cancelled ? nowUtc.AddHours(-1) : null,
                Seats = lines.Select(l => new BookingSeat
                    { SeatId = l.Seat.Id, ShowingId = showing.Id, Price = l.Price, IsActive = !cancelled }).ToList()
            });
        }

        for (var i = 0; i < BookingSeeds.Length; i++)
        {
            var (auditorium, nth, customer, row, first, count, cancelled) = BookingSeeds[i];
            var auditoriumId = auditoriums[auditorium].Id;
            var showing = await db.Showings.Where(s => s.AuditoriumId == auditoriumId && s.StartTime > nowUtc)
                .OrderBy(s => s.StartTime).Skip(nth).FirstOrDefaultAsync();
            if (showing is not null)
            {
                await AddAsync(showing, customer, row, first, count, cancelled, nowUtc.AddHours(-3 * (i + 1)));
            }
        }

        // One booking on a showing that has already been shown, so the history views have something to list.
        await AddAsync(history, "customer1@newtons.local", 6, 6, 2, false, nowUtc.AddDays(-3));
        await db.SaveChangesAsync();
    }

    private static DateTime RoundUpToFiveMinutes(DateTime value)
    {
        var step = TimeSpan.FromMinutes(5).Ticks;
        return new DateTime((value.Ticks + step - 1) / step * step, DateTimeKind.Utc);
    }

    private static void Check(IdentityResult result, string what)
    {
        if (!result.Succeeded)
        {
            throw new InvalidOperationException($"Seeding {what} failed: {string.Join("; ", result.Errors.Select(e => e.Description))}");
        }
    }
}
