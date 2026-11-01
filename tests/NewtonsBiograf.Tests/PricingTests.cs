using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NewtonsBiograf.Api.Pricing;
using NewtonsBiograf.Shared;

namespace NewtonsBiograf.Tests;

public sealed class PricingTests : IDisposable
{
    private readonly WebApplicationFactory<Program> _factory = new();

    public void Dispose() => _factory.Dispose();

    private WebApplicationFactory<Program> With(params (string Key, string? Value)[] settings) =>
        _factory.WithWebHostBuilder(b => b.ConfigureAppConfiguration((_, config) =>
            config.AddInMemoryCollection(settings.Select(s => new KeyValuePair<string, string?>(s.Key, s.Value)))));

    private static decimal Price(WebApplicationFactory<Program> factory, SeatType type) =>
        factory.Services.GetRequiredService<IPriceCalculator>().GetPrice(type);

    [Fact]
    public void Ac3_Calculator_returns_the_prices_from_appsettings()
    {
        Assert.Equal(140m, Price(_factory, SeatType.Standard));
        Assert.Equal(200m, Price(_factory, SeatType.VIP));
    }

    [Fact]
    public void Ac3_Changing_configuration_changes_the_prices_without_code_changes()
    {
        using var factory = With(("Pricing:StandardSeat", "95"), ("Pricing:VipSeat", "310"));

        Assert.Equal(95m, Price(factory, SeatType.Standard));
        Assert.Equal(310m, Price(factory, SeatType.VIP));
    }

    [Fact]
    public void Ac3_Calculator_rejects_an_unknown_seat_type()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Price(_factory, (SeatType)99));
    }

    [Fact]
    public async Task Ac3_Pricing_endpoint_is_anonymous_and_returns_the_configured_prices()
    {
        using var factory = With(("Pricing:VipSeat", "250"));

        var response = await factory.CreateClient().GetAsync("/api/pricing");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var prices = await response.Content.ReadFromJsonAsync<PricingDto>();
        Assert.Equal(new PricingDto(140m, 250m), prices);
    }

    [Theory]
    [InlineData("Pricing:StandardSeat", "0")]
    [InlineData("Pricing:VipSeat", "-5")]
    [InlineData("Pricing:VipSeat", "")]
    [InlineData("Pricing:StandardSeat", "")]
    public void Ac5_Startup_fails_with_a_clear_message_for_a_missing_or_non_positive_price(string key, string value)
    {
        using var factory = With((key, value));

        var error = Record.Exception(() => factory.CreateClient());

        Assert.NotNull(error);
        Assert.Contains($"'{key}'", error.ToString());
    }

    /// <summary>Column-length limits such as HasMaxLength(200) are not prices, so lines that declare a length are ignored.</summary>
    [Fact]
    public void Ac4_Production_code_has_no_price_literals()
    {
        var api = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../src/NewtonsBiograf.Api"));
        var literal = new Regex(@"(?<![\w.])(140|200)(?![\w.])");
        var length = new Regex(@"MaxLength|StringLength");
        var skipped = new[] { "obj", "bin", "Migrations" }.Select(d => $"{Path.DirectorySeparatorChar}{d}{Path.DirectorySeparatorChar}");

        var offenders = Directory.EnumerateFiles(api, "*.cs", SearchOption.AllDirectories)
            .Where(f => !skipped.Any(f.Contains))
            .SelectMany(f => File.ReadLines(f)
                .Where(l => literal.IsMatch(l) && !length.IsMatch(l))
                .Select(l => $"{Path.GetFileName(f)}: {l.Trim()}"))
            .ToList();

        Assert.Empty(offenders);
    }
}
