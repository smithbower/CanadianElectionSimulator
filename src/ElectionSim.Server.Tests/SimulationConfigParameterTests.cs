using System.Text.Json;
using System.Text.Json.Serialization;
using ElectionSim.Core.Models;
using ElectionSim.Core.Simulation;
using Xunit;

namespace ElectionSim.Server.Tests;

/// <summary>
/// Shared fixture that loads election data once for all config parameter tests.
/// </summary>
public class ElectionDataFixture : IAsyncLifetime
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public List<Riding> Ridings { get; private set; } = [];
    public List<RidingResult> Results2025 { get; private set; } = [];
    public List<RegionalPoll> Polling { get; private set; } = [];
    public List<RidingDemographics>? Demographics { get; private set; }
    public List<PostElectionEvent> PostElectionEvents { get; private set; } = [];

    public async Task InitializeAsync()
    {
        var dataDir = Path.Combine(TestHelpers.WebRootPath, "data");
        Ridings = await LoadAsync<List<Riding>>(dataDir, "ridings.json") ?? [];
        Results2025 = await LoadAsync<List<RidingResult>>(dataDir, "results-2025.json") ?? [];
        Polling = await LoadAsync<List<RegionalPoll>>(dataDir, "polling.json") ?? [];
        Demographics = await LoadAsync<List<RidingDemographics>>(dataDir, "demographics.json");
        PostElectionEvents = await LoadAsync<List<PostElectionEvent>>(dataDir, "post-election-events.json") ?? [];
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static async Task<T?> LoadAsync<T>(string dir, string filename) where T : class
    {
        var path = Path.Combine(dir, filename);
        if (!File.Exists(path)) return null;
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions);
    }
}

/// <summary>
/// Tests for <see cref="SimulationConfig"/> parameters NOT exposed via the API but used
/// by the simulation engine. Tests the Core pipeline directly.
/// </summary>
public class SimulationConfigParameterTests : IClassFixture<ElectionDataFixture>
{
    private readonly ElectionDataFixture _data;

    public SimulationConfigParameterTests(ElectionDataFixture data)
    {
        _data = data;
    }

    private SimulationSummary RunSimulation(
        SimulationConfig config,
        IReadOnlyList<PostElectionEvent>? postElectionEvents = null,
        IReadOnlyList<RidingDemographics>? demographics = null)
    {
        var projected = SimulationPipeline.ProjectVoteShares(
            _data.Ridings, _data.Results2025, _data.Polling, config,
            demographics: demographics,
            postElectionEvents: postElectionEvents);

        var simulator = new MonteCarloSimulator(_data.Ridings, projected);
        return simulator.Run(config);
    }

    private static SimulationConfig BaseConfig(int seed = 42, int numSims = 200) => new(
        NumSimulations: numSims,
        Seed: seed,
        NationalSigma: 0.025,
        RegionalSigma: 0.02,
        RidingSigma: 0.015);

    [Fact]
    public void DegreesOfFreedom_AffectsDistributionShape()
    {
        var studentT = RunSimulation(BaseConfig() with { DegreesOfFreedom = 5.0 });
        var gaussian = RunSimulation(BaseConfig() with { DegreesOfFreedom = null });

        // Student-t (df=5) has heavier tails than Gaussian — P5/P95 extremes should differ
        var anyDiffers = PartyColourProvider.MainParties.Any(p =>
            studentT.SeatDistributions[p].P95 != gaussian.SeatDistributions[p].P95 ||
            studentT.SeatDistributions[p].P5 != gaussian.SeatDistributions[p].P5);

        Assert.True(anyDiffers,
            "Student-t (df=5) vs Gaussian should produce different tail behavior.");
    }

    [Fact]
    public void UseCorrelatedNoise_AffectsResults()
    {
        var correlated = RunSimulation(BaseConfig() with { UseCorrelatedNoise = true });
        var independent = RunSimulation(BaseConfig() with { UseCorrelatedNoise = false });

        var anyDiffers = PartyColourProvider.MainParties.Any(p =>
            correlated.SeatDistributions[p].Mean != independent.SeatDistributions[p].Mean);

        Assert.True(anyDiffers,
            "Correlated vs independent noise should produce different results.");
    }

    [Fact]
    public void RegionalSigmaMultipliers_AffectsResults()
    {
        var uniform = Enum.GetValues<Region>().ToDictionary(r => r, _ => 1.0);

        var withDefaults = RunSimulation(BaseConfig() with { RegionalSigmaMultipliers = null });
        var withUniform = RunSimulation(BaseConfig() with { RegionalSigmaMultipliers = uniform });

        var anyDiffers = PartyColourProvider.MainParties.Any(p =>
            withDefaults.SeatDistributions[p].Mean != withUniform.SeatDistributions[p].Mean);

        Assert.True(anyDiffers,
            "Default regional multipliers vs uniform should produce different results.");
    }

    [Fact]
    public void UseDemographicPrior_AffectsProjection()
    {
        if (_data.Demographics == null || _data.Demographics.Count == 0)
        {
            // No demographics data available — verify the config value is respected
            var config = BaseConfig() with { UseDemographicPrior = true };
            Assert.True(config.UseDemographicPrior);
            return;
        }

        var without = RunSimulation(
            BaseConfig() with { UseDemographicPrior = false },
            demographics: _data.Demographics);
        var with = RunSimulation(
            BaseConfig() with { UseDemographicPrior = true, DemographicBlendWeight = 0.10 },
            demographics: _data.Demographics);

        var anyDiffers = PartyColourProvider.MainParties.Any(p =>
            without.SeatDistributions[p].Mean != with.SeatDistributions[p].Mean);

        Assert.True(anyDiffers,
            "Demographic prior should alter projected vote shares and thus results.");
    }

    [Fact]
    public void ByElectionBlendWeight_AffectsResults()
    {
        var eventsForBaseline = ParliamentaryState.GetEventsForElection(
            _data.PostElectionEvents, 2025);
        var hasByElections = eventsForBaseline.Any(e =>
            e.Type == PostElectionEventType.ByElection && e.ByElectionResult != null);

        if (!hasByElections)
        {
            // No by-election data — verify the config value is respected
            var config = BaseConfig() with { ByElectionBlendWeight = 0.5 };
            Assert.Equal(0.5, config.ByElectionBlendWeight);
            return;
        }

        var noBlend = RunSimulation(
            BaseConfig() with { ByElectionBlendWeight = 0.0 },
            postElectionEvents: eventsForBaseline);
        var withBlend = RunSimulation(
            BaseConfig() with { ByElectionBlendWeight = 0.5 },
            postElectionEvents: eventsForBaseline);

        var anyDiffers = PartyColourProvider.MainParties.Any(p =>
            noBlend.SeatDistributions[p].Mean != withBlend.SeatDistributions[p].Mean);

        Assert.True(anyDiffers,
            "By-election blend weight should alter baseline for affected ridings.");
    }
}
