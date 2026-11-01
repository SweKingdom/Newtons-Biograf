using System.Runtime.CompilerServices;

namespace NewtonsBiograf.Tests.Infrastructure;

/// <summary>
/// Every test host starts the API in Development, where the API would migrate and seed the demo data. The tests build
/// their own databases, so the seeding is switched off for the whole test run. <see cref="SeedTests"/> calls the seeder directly.
/// </summary>
internal static class TestEnvironment
{
    [ModuleInitializer]
    internal static void DisableStartupSeeding() => Environment.SetEnvironmentVariable("Seed__Enabled", "false");
}
