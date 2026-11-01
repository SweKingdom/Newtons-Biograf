using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using NewtonsBiograf.Shared;

namespace NewtonsBiograf.Tests;

public class HealthTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public HealthTests(WebApplicationFactory<Program> factory) => _factory = factory;

    private HttpClient CreateClient(string environment) =>
        _factory.WithWebHostBuilder(b => b.UseEnvironment(environment))
            .CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    [Fact]
    public async Task Ac3_Health_returns_200_with_status_ok_without_authentication()
    {
        var response = await CreateClient("Development").GetAsync("/api/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<HealthResponse>();
        Assert.Equal("ok", body?.Status);
    }

    [Fact]
    public async Task Ac3_Swagger_is_served_in_Development()
    {
        var response = await CreateClient("Development").GetAsync("/swagger/v1/swagger.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        Assert.Contains("/api/health", json);
        Assert.Contains("bearer", json);
    }

    [Fact]
    public async Task Ac3_Swagger_is_not_served_in_Production()
    {
        var response = await CreateClient("Production").GetAsync("/swagger/v1/swagger.json");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Ac3_Cors_allows_the_configured_client_origin_only()
    {
        var client = CreateClient("Development");

        var allowed = new HttpRequestMessage(HttpMethod.Get, "/api/health");
        allowed.Headers.Add("Origin", "https://localhost:5001");
        var allowedResponse = await client.SendAsync(allowed);
        Assert.Equal("https://localhost:5001", allowedResponse.Headers.GetValues("Access-Control-Allow-Origin").Single());

        var other = new HttpRequestMessage(HttpMethod.Get, "/api/health");
        other.Headers.Add("Origin", "https://evil.example");
        var otherResponse = await client.SendAsync(other);
        Assert.False(otherResponse.Headers.Contains("Access-Control-Allow-Origin"));
    }
}
