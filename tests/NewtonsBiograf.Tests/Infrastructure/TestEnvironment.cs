using System.Runtime.CompilerServices;

namespace NewtonsBiograf.Tests.Infrastructure;

/// <summary>
/// Every test host starts the API in Development, where the API would migrate and seed the demo data. The tests build
/// their own databases, so the seeding is switched off for the whole test run. <see cref="SeedTests"/> calls the seeder directly.
/// Config file watching is switched off as well: the hosts never reload settings, and the file watchers can throw
/// a NullReferenceException in <c>FileSystemWatcher.StopRaisingEvents</c> when a factory is disposed.
/// </summary>
internal static class TestEnvironment
{
    [ModuleInitializer]
    internal static void ConfigureTestHosts()
    {
        Environment.SetEnvironmentVariable("Seed__Enabled", "false");
        Environment.SetEnvironmentVariable("DOTNET_hostBuilder__reloadConfigOnChange", "false");
    }
}
