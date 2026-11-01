using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using NewtonsBiograf.Client;
using NewtonsBiograf.Client.Auth;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

var apiBaseUrl = builder.Configuration["ApiBaseUrl"]
    ?? throw new InvalidOperationException("Configuration value 'ApiBaseUrl' is missing.");

builder.Services.AddAuthorizationCore();
builder.Services.AddCascadingAuthenticationState();
// Singletons: the HTTP handler and the UI must share one instance so a logout reaches both.
builder.Services.AddSingleton<JwtAuthStateProvider>();
builder.Services.AddSingleton<AuthenticationStateProvider>(sp => sp.GetRequiredService<JwtAuthStateProvider>());
builder.Services.AddTransient<AuthMessageHandler>();
builder.Services.AddHttpClient("Api", client => client.BaseAddress = new Uri(apiBaseUrl))
    .AddHttpMessageHandler<AuthMessageHandler>();

await builder.Build().RunAsync();
