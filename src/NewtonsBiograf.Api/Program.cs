using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using NewtonsBiograf.Api.Auth;
using NewtonsBiograf.Api.Data;

var builder = WebApplication.CreateBuilder(args);

var clientOrigin = builder.Configuration["Cors:ClientOrigin"]
    ?? throw new InvalidOperationException("Configuration value 'Cors:ClientOrigin' is missing.");
var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("Configuration value 'ConnectionStrings:Default' is missing.");

builder.Services.AddDbContext<AppDbContext>(options => options.UseSqlite(connectionString));
builder.Services.AddScoped(typeof(IRepository<>), typeof(Repository<>));
builder.Services.AddOptions<JwtOptions>()
    .BindConfiguration("Jwt")
    .Validate(o => o.Key.Length >= 32, "Configuration value 'Jwt:Key' is missing or shorter than 32 characters.")
    .Validate(o => o.ExpiryMinutes > 0, "Configuration value 'Jwt:ExpiryMinutes' must be greater than 0.")
    .ValidateOnStart();
builder.Services.AddSingleton<TokenService>();
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
builder.Services.AddControllers();
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
