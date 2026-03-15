using ElectionSim.Core.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;

namespace ElectionSim.Server.Tests;

internal static class TestHelpers
{
    internal static string FindSolutionRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null)
        {
            if (Directory.GetFiles(dir, "*.slnx").Length > 0)
                return dir;
            dir = Directory.GetParent(dir)?.FullName;
        }
        throw new InvalidOperationException(
            "Could not find solution root (no .slnx file found in parent directories).");
    }

    internal static string WebRootPath =>
        Path.Combine(FindSolutionRoot(), "src", "ElectionSim.Web", "wwwroot");

    internal static Dictionary<Region, Dictionary<Party, double>> MakeDominantPolling(Party dominant)
    {
        var regions = Enum.GetValues<Region>();
        var polling = new Dictionary<Region, Dictionary<Party, double>>();
        foreach (var region in regions)
        {
            var shares = new Dictionary<Party, double>
            {
                [Party.LPC] = 0.15,
                [Party.CPC] = 0.15,
                [Party.NDP] = 0.10,
                [Party.BQ] = region == Region.Quebec ? 0.20 : 0.0,
                [Party.GPC] = 0.03,
                [Party.PPC] = 0.02,
            };
            shares[dominant] = region == Region.Quebec && dominant != Party.BQ ? 0.45 : 0.55;
            polling[region] = shares;
        }
        return polling;
    }

    internal static Dictionary<Party, double> MakeUniformUncertainty(double value) =>
        PartyColourProvider.MainParties.ToDictionary(p => p, _ => value);

    internal static double TotalSpread(SimulationSummary results) =>
        results.SeatDistributions.Values.Sum(d => d.P95 - d.P5);
}

internal class StubWebHostEnvironment : IWebHostEnvironment
{
    public string WebRootPath { get; set; } = "";
    public IFileProvider WebRootFileProvider { get; set; } = null!;
    public string ApplicationName { get; set; } = "Test";
    public IFileProvider ContentRootFileProvider { get; set; } = null!;
    public string ContentRootPath { get; set; } = "";
    public string EnvironmentName { get; set; } = "Test";
}
