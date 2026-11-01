using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NewtonsBiograf.Api.Data;

namespace NewtonsBiograf.Tests;

/// <summary>US-140: the seeder runs on a fresh migrated in-memory database, with the real Identity managers.</summary>
public sealed class SeedTests : IDisposable
{
    private const string Password = "Demo-Pass-1234";
    private static readonly DateTime Now = new(2026, 11, 1, 8, 0, 0, DateTimeKind.Utc);
    private static readonly TimeZoneInfo Zone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Stockholm");

    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly ServiceProvider _provider;

    public SeedTests()
    {
        _connection.Open();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<AppDbContext>(options => options.UseSqlite(_connection));
        services.AddIdentityCore<ApplicationUser>(options =>
            {
                options.User.RequireUniqueEmail = true;
                options.Password.RequiredLength = 8;
                options.Password.RequireDigit = false;
                options.Password.RequireUppercase = false;
                options.Password.RequireLowercase = false;
                options.Password.RequireNonAlphanumeric = false;
            })
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<AppDbContext>();
        _provider = services.BuildServiceProvider();
        using var scope = _provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.Migrate();
    }

    public void Dispose()
    {
        _provider.Dispose();
        _connection.Dispose();
    }

    private async Task SeedAsync(DateTime now, string? password = Password)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Seed:Password"] = password,
            ["Pricing:StandardSeat"] = "140",
            ["Pricing:VipSeat"] = "200"
        }).Build();
        using var scope = _provider.CreateScope();
        var sp = scope.ServiceProvider;
        await DbSeeder.SeedAsync(sp.GetRequiredService<AppDbContext>(), sp.GetRequiredService<UserManager<ApplicationUser>>(),
            sp.GetRequiredService<RoleManager<IdentityRole>>(), config, now);
    }

    private async Task<T> Read<T>(Func<AppDbContext, Task<T>> read)
    {
        using var scope = _provider.CreateScope();
        return await read(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    private Task<(int Users, int Roles, int Claims, int Auditoriums, int Seats, int Movies, int Showings, int Bookings, int BookingSeats)> Counts() =>
        Read(async db => (await db.Users.CountAsync(), await db.Roles.CountAsync(), await db.UserClaims.CountAsync(),
            await db.Auditoriums.CountAsync(), await db.Seats.CountAsync(), await db.Movies.CountAsync(),
            await db.Showings.CountAsync(), await db.Bookings.CountAsync(), await db.BookingSeats.CountAsync()));

    private static (DateTime Start, DateTime End) LocalDay(DateTime now, int offset)
    {
        var date = DateTime.SpecifyKind(TimeZoneInfo.ConvertTimeFromUtc(now, Zone).Date.AddDays(offset), DateTimeKind.Unspecified);
        return (TimeZoneInfo.ConvertTimeToUtc(date, Zone), TimeZoneInfo.ConvertTimeToUtc(date.AddDays(1), Zone));
    }

    private static void AssertNoOverlaps(List<Showing> showings)
    {
        foreach (var group in showings.GroupBy(s => s.AuditoriumId))
        {
            var ordered = group.OrderBy(s => s.StartTime).ToList();
            for (var i = 1; i < ordered.Count; i++)
            {
                Assert.True(ordered[i].StartTime >= ordered[i - 1].EndTime,
                    $"Showings {ordered[i - 1].Id} and {ordered[i].Id} overlap in auditorium {group.Key}.");
            }
        }
    }

    [Fact]
    public async Task Ac1_Accounts_can_log_in_with_the_documented_password_and_have_the_documented_role_and_permissions()
    {
        await SeedAsync(Now);

        using var scope = _provider.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var expected = new[]
        {
            ("admin@newtons.local", Roles.Admin, Array.Empty<string>()),
            ("staff1@newtons.local", Roles.Staff, new[] { Permissions.ManageMovies, Permissions.ManageShowings }),
            ("staff2@newtons.local", Roles.Staff, new[] { Permissions.ManageBookings }),
            ("customer1@newtons.local", Roles.Customer, Array.Empty<string>()),
            ("customer2@newtons.local", Roles.Customer, Array.Empty<string>()),
            ("customer3@newtons.local", Roles.Customer, Array.Empty<string>())
        };
        foreach (var (email, role, permissions) in expected)
        {
            var user = await users.FindByEmailAsync(email);
            Assert.NotNull(user);
            Assert.True(await users.CheckPasswordAsync(user, Password), email);
            Assert.Equal(new[] { role }, (await users.GetRolesAsync(user)).ToArray());
            var claims = (await users.GetClaimsAsync(user)).Where(c => c.Type == Permissions.ClaimType).Select(c => c.Value);
            Assert.Equal(permissions.Order().ToArray(), claims.Order().ToArray());
        }
        Assert.Equal(6, (await Counts()).Users);
    }

    [Fact]
    public async Task Ac1_A_missing_seed_password_fails_with_a_clear_message_and_creates_nothing()
    {
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => SeedAsync(Now, password: null));

        Assert.Contains("Seed:Password", error.Message);
        Assert.Equal(0, (await Counts()).Users);
    }

    [Fact]
    public async Task Ac2_Content_has_three_auditoriums_with_vip_rows_ten_movies_and_showings_on_each_of_the_next_14_days()
    {
        await SeedAsync(Now);

        await Read(async db =>
        {
            var names = await db.Auditoriums.OrderBy(a => a.Name).Select(a => a.Name).ToListAsync();
            Assert.Equal(new[] { "Salong 1", "Salong 2", "Salong 3 VIP" }, names.ToArray());
            var vip = await db.Seats.Where(s => s.Type == SeatType.VIP).Include(s => s.Auditorium).ToListAsync();
            Assert.All(vip, s => Assert.Equal("Salong 3 VIP", s.Auditorium!.Name));
            Assert.Equal(new[] { 7, 8 }, vip.Select(s => s.RowNumber).Distinct().Order().ToArray());
            Assert.Equal(8 * 12 + 6 * 10 + 8 * 10, await db.Seats.CountAsync());

            var movies = await db.Movies.ToListAsync();
            Assert.Equal(10, movies.Count);
            Assert.True(movies.Select(m => m.Genre).Distinct().Count() >= 5);
            Assert.True(movies.Select(m => m.AgeRating).Distinct().Count() >= 3);
            Assert.All(movies, m => Assert.InRange(m.LengthMinutes, 90, 170));

            var showings = await db.Showings.AsNoTracking().ToListAsync();
            for (var day = 0; day < 14; day++)
            {
                var (start, end) = LocalDay(Now, day);
                Assert.True(showings.Count(s => s.Status == ShowingStatus.Scheduled && s.StartTime >= start && s.StartTime < end) >= 2,
                    $"Day {day} has fewer than two scheduled showings.");
            }

            var bookings = await db.Bookings.ToListAsync();
            Assert.True(bookings.Count(b => b.Status == BookingStatus.Confirmed) >= 5);
            Assert.True(bookings.Count(b => b.Status == BookingStatus.Cancelled) >= 1);
            return 0;
        });
    }

    [Fact]
    public async Task Ac3_Data_is_valid_no_overlaps_end_times_unique_active_seats_matching_totals_and_future_bookings()
    {
        await SeedAsync(Now);

        await Read(async db =>
        {
            var showings = await db.Showings.Include(s => s.Movie).AsNoTracking().ToListAsync();
            Assert.All(showings, s => Assert.Equal(s.StartTime.AddMinutes(s.Movie!.LengthMinutes + 15), s.EndTime));
            AssertNoOverlaps(showings);
            Assert.Single(showings, s => s.StartTime <= Now);

            var active = await db.BookingSeats.Where(bs => bs.IsActive).Select(bs => new { bs.ShowingId, bs.SeatId }).ToListAsync();
            Assert.NotEmpty(active);
            Assert.Equal(active.Count, active.Distinct().Count());

            var bookings = await db.Bookings.Include(b => b.Seats).ThenInclude(bs => bs.Seat).Include(b => b.Showing).AsNoTracking().ToListAsync();
            Assert.All(bookings, b =>
            {
                Assert.Equal(b.Seats.Sum(s => s.Price), b.TotalPrice);
                Assert.All(b.Seats, s => Assert.Equal(s.Seat!.Type == SeatType.VIP ? 200m : 140m, s.Price));
                Assert.Matches("^[ABCDEFGHJKLMNPQRSTUVWXYZ23456789]{8}$", b.Reference);
                Assert.Equal(b.Status == BookingStatus.Confirmed, b.Seats.All(s => s.IsActive));
                Assert.InRange(b.Seats.Count, 1, 4);
            });
            var past = Assert.Single(bookings, b => b.Showing!.StartTime <= Now);
            Assert.Equal(BookingStatus.Confirmed, past.Status);
            return 0;
        });
    }

    [Fact]
    public async Task Ac4_Running_the_seed_twice_leaves_every_row_count_unchanged()
    {
        await SeedAsync(Now);
        var first = await Counts();

        await SeedAsync(Now);

        Assert.Equal(first, await Counts());
        Assert.True(first.Showings > 0 && first.Bookings > 0);
    }

    [Fact]
    public async Task Ac4_A_later_start_tops_up_missing_days_and_keeps_existing_showings_and_bookings()
    {
        await SeedAsync(Now);
        var before = await Read(db => db.Showings.Select(s => s.Id).ToListAsync());
        var bookingsBefore = (await Counts()).Bookings;
        var later = Now.AddDays(20);

        await SeedAsync(later);

        await Read(async db =>
        {
            var showings = await db.Showings.AsNoTracking().ToListAsync();
            Assert.True(showings.Count > before.Count);
            Assert.All(before, id => Assert.Contains(showings, s => s.Id == id));
            for (var day = 0; day < 14; day++)
            {
                var (start, end) = LocalDay(later, day);
                Assert.True(showings.Any(s => s.Status == ShowingStatus.Scheduled && s.StartTime >= start && s.StartTime < end),
                    $"Day {day} has no showing after the top-up.");
            }
            AssertNoOverlaps(showings);
            Assert.Equal(bookingsBefore, await db.Bookings.CountAsync());
            return 0;
        });
    }

    [Fact]
    public void Ac5_The_seeded_database_is_not_ignored_and_the_connection_string_points_to_it()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "NewtonsBiograf.sln")))
        {
            root = root.Parent;
        }
        Assert.NotNull(root);

        var ignore = File.ReadAllLines(Path.Combine(root.FullName, ".gitignore")).Select(l => l.Trim()).ToList();
        Assert.True(ignore.IndexOf("!newtons.db") > ignore.IndexOf("*.db"), "'!newtons.db' must follow '*.db'.");
        Assert.Contains("*.db-shm", ignore);
        Assert.Contains("*.db-wal", ignore);
        var settings = File.ReadAllText(Path.Combine(root.FullName, "src", "NewtonsBiograf.Api", "appsettings.json"));
        Assert.Contains("\"Data Source=newtons.db\"", settings);
    }
}
