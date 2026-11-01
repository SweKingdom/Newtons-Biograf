using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using NewtonsBiograf.Api.Auth;
using NewtonsBiograf.Api.Data;
using NewtonsBiograf.Api.Events;
using NewtonsBiograf.Api.Pricing;
using NewtonsBiograf.Api.Services;
using NewtonsBiograf.Shared;

var builder = WebApplication.CreateBuilder(args);

var clientOrigin = builder.Configuration["Cors:ClientOrigin"]
    ?? throw new InvalidOperationException("Configuration value 'Cors:ClientOrigin' is missing.");
var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("Configuration value 'ConnectionStrings:Default' is missing.");

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddDbContext<AppDbContext>(options => options.UseSqlite(connectionString));
builder.Services.AddScoped(typeof(IRepository<>), typeof(Repository<>));
builder.Services.AddOptions<JwtOptions>()
    .BindConfiguration("Jwt")
    .Validate(o => o.Key.Length >= 32, "Configuration value 'Jwt:Key' is missing or shorter than 32 characters.")
    .Validate(o => o.ExpiryMinutes > 0, "Configuration value 'Jwt:ExpiryMinutes' must be greater than 0.")
    .ValidateOnStart();
builder.Services.AddSingleton<TokenService>();
builder.Services.AddOptions<PricingOptions>()
    .BindConfiguration(PricingOptions.Section)
    .Validate(o => o.StandardSeat > 0, "Configuration value 'Pricing:StandardSeat' is missing or not greater than 0.")
    .Validate(o => o.VipSeat > 0, "Configuration value 'Pricing:VipSeat' is missing or not greater than 0.")
    .ValidateOnStart();
builder.Services.AddSingleton<IPriceCalculator, PriceCalculator>();
builder.Services.AddOptions<ShowingOptions>()
    .BindConfiguration(ShowingOptions.Section)
    .Validate(o => o.CleaningMinutes >= 0, "Configuration value 'Showings:CleaningMinutes' must not be negative.")
    .Validate(o => TimeZoneInfo.TryFindSystemTimeZoneById(o.DisplayTimeZone, out _),
        "Configuration value 'Showings:DisplayTimeZone' is not a known time zone id.")
    .ValidateOnStart();
builder.Services.AddOptions<BookingOptions>()
    .BindConfiguration(BookingOptions.Section)
    .Validate(o => o.CancellationLimitHours >= 0, "Configuration value 'Booking:CancellationLimitHours' must not be negative.")
    .ValidateOnStart();
builder.Services.AddScoped<ShowingService>();
builder.Services.AddScoped<BookingService>();
builder.Services.AddScoped<IBookingEvents, LoggingBookingEvents>();
builder.Services.AddSingleton<IBookingEventPublisher, BookingEventPublisher>();
builder.Services.AddSingleton<BookingLogSubscriber>();
builder.Services.AddProblemDetails();
builder.Services.AddIdentityCore<ApplicationUser>(options =>
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
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false; // keep sub/email/role names as written by TokenService
        options.Events = new JwtBearerEvents
        {
            OnChallenge = async context =>
            {
                context.HandleResponse();
                await WriteProblem(context.Response, StatusCodes.Status401Unauthorized, "Authentication required",
                    "https://tools.ietf.org/html/rfc9110#section-15.5.2");
            },
            OnForbidden = context => WriteProblem(context.Response, StatusCodes.Status403Forbidden,
                "You do not have access to this resource", "https://tools.ietf.org/html/rfc9110#section-15.5.4")
        };
    });
// The signing key comes from validated configuration, so it is resolved when the options are first used.
builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<IOptions<JwtOptions>>((options, jwtOptions) =>
    {
        var jwt = jwtOptions.Value;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwt.Issuer,
            ValidAudience = jwt.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Key)),
            ClockSkew = TimeSpan.FromMinutes(1),
            RoleClaimType = TokenService.RoleClaimType,
            NameClaimType = "name"
        };
    });
builder.Services.AddAuthorization(options => options.AddAppPolicies());
builder.Services.AddControllers()
    .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new StrictEnumConverterFactory()))
    .ConfigureApiBehaviorOptions(options =>
    {
        var defaultFactory = options.InvalidModelStateResponseFactory;
        options.InvalidModelStateResponseFactory = context =>
        {
            // Body conversion errors (e.g. an unknown enum string) are keyed by JSON path ("$.genre");
            // key them by property name ("Genre") like every other validation error.
            foreach (var key in context.ModelState.Keys.Where(k => k.StartsWith("$.") && k.Length > 2).ToArray())
            {
                var field = char.ToUpperInvariant(key[2]) + key[3..];
                context.ModelState.ClearValidationState(key);
                context.ModelState.AddModelError(field, $"{field} has an invalid value.");
            }
            return defaultFactory(context);
        };
    });
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo { Title = "Newtons Biograf API", Version = "v1" });
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Paste the JWT returned by POST /api/auth/login."
    });
    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
            },
            Array.Empty<string>()
        }
    });
});
builder.Services.AddCors(options =>
    options.AddPolicy("Client", policy => policy
        .WithOrigins(clientOrigin)
        .AllowAnyHeader()
        .AllowAnyMethod()));

var app = builder.Build();

// Development only: bring the database up to date and add the demo data (accounts, movies, showings, bookings).
// "Seed:Enabled" = false turns it off (the test project does, because its tests migrate their own databases).
if (app.Environment.IsDevelopment() && app.Configuration.GetValue("Seed:Enabled", true))
{
    using var scope = app.Services.CreateScope();
    var services = scope.ServiceProvider;
    var db = services.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
    await DbSeeder.SeedAsync(db, services.GetRequiredService<UserManager<ApplicationUser>>(),
        services.GetRequiredService<RoleManager<IdentityRole>>(), app.Configuration, DateTime.UtcNow);
}

// Creating the subscriber attaches it to the booking events.
app.Services.GetRequiredService<BookingLogSubscriber>();

// Unhandled exceptions become a 500 application/problem+json response (no stack trace for the caller).
app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors("Client");
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();

static Task WriteProblem(HttpResponse response, int status, string title, string type)
{
    response.StatusCode = status;
    return response.WriteAsJsonAsync(
        new Microsoft.AspNetCore.Mvc.ProblemDetails { Type = type, Title = title, Status = status },
        (System.Text.Json.JsonSerializerOptions?)null,
        "application/problem+json");
}

public partial class Program { }
