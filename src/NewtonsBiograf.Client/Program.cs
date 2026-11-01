using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using NewtonsBiograf.Client;
using NewtonsBiograf.Client.Auth;
using NewtonsBiograf.Client.Services;
using NewtonsBiograf.Shared;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

var apiBaseUrl = builder.Configuration["ApiBaseUrl"]
    ?? throw new InvalidOperationException("Configuration value 'ApiBaseUrl' is missing.");

// Same permission policies as the API (Admin or the permission claim). Hiding in the UI is cosmetic; the API enforces.
builder.Services.AddAuthorizationCore(options =>
{
    foreach (var permission in Permissions.All)
    {
        options.AddPolicy(permission, policy => policy
            .RequireAuthenticatedUser()
            .RequireAssertion(ctx =>
                ctx.User.IsInRole(Roles.Admin) || ctx.User.HasClaim(Permissions.ClaimType, permission)));
    }
});
builder.Services.AddCascadingAuthenticationState();
// Singletons: the HTTP handler and the UI must share one instance so a logout reaches both.
builder.Services.AddSingleton<JwtAuthStateProvider>();
builder.Services.AddSingleton<AuthenticationStateProvider>(sp => sp.GetRequiredService<JwtAuthStateProvider>());
builder.Services.AddTransient<AuthMessageHandler>();
builder.Services.AddHttpClient("Api", client => client.BaseAddress = new Uri(apiBaseUrl))
    .AddHttpMessageHandler<AuthMessageHandler>();
builder.Services.AddScoped<MovieApiClient>();
builder.Services.AddScoped<AuditoriumApiClient>();
builder.Services.AddScoped<ShowingApiClient>();

await builder.Build().RunAsync();
