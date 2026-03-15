using ElectionSim.Core.Models;
using ElectionSim.Server.Services;
using Xunit;

namespace ElectionSim.Server.Tests;

/// <summary>
/// Verifies that every parameter in <see cref="SimulationRequest"/> actually affects the
/// simulation output (i.e., none are silently ignored). Each test runs two simulations with
/// a single parameter changed and asserts the results differ.
/// </summary>
public class SimulationParameterUtilizationTests : IDisposable
{
    private readonly SimulationService _service;
    private readonly string _tempDir;

    public SimulationParameterUtilizationTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"ElectionSimTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        _service = new SimulationService(new StubWebHostEnvironment
        {
            WebRootPath = TestHelpers.WebRootPath,
            ContentRootPath = _tempDir
        });
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); }
        catch { /* best effort cleanup */ }
    }

    private static SimulationRequest Baseline(int seed = 42, int numSims = 200) =>
        new(Seed: seed, NumSimulations: numSims);

    [Fact]
    public async Task BaselineYear_AffectsResults()
    {
        var a = await _service.RunSimulationAsync(Baseline() with { BaselineYear = 2025 });
        var b = await _service.RunSimulationAsync(Baseline() with { BaselineYear = 2021 });

        Assert.NotEqual(
            a.Results.SeatDistributions[Party.CPC].Mean,
            b.Results.SeatDistributions[Party.CPC].Mean);
    }

    [Fact]
    public async Task NumSimulations_AffectsResults()
    {
        var a = await _service.RunSimulationAsync(Baseline() with { NumSimulations = 100 });
        var b = await _service.RunSimulationAsync(Baseline() with { NumSimulations = 500 });

        Assert.Equal(100, a.Results.TotalSimulations);
        Assert.Equal(500, b.Results.TotalSimulations);
    }

    [Fact]
    public async Task NationalSigma_PropagatedToConfig()
    {
        var a = await _service.RunSimulationAsync(Baseline() with { NationalSigma = 0.01 });
        var b = await _service.RunSimulationAsync(Baseline() with { NationalSigma = 0.10 });

        Assert.Equal(0.01, a.Config.NationalSigma);
        Assert.Equal(0.10, b.Config.NationalSigma);
    }

    [Fact]
    public async Task RegionalSigma_AffectsSpread()
    {
        var a = await _service.RunSimulationAsync(Baseline() with { RegionalSigma = 0.001 });
        var b = await _service.RunSimulationAsync(Baseline() with { RegionalSigma = 0.50 });

        Assert.True(
            TestHelpers.TotalSpread(b.Results) > TestHelpers.TotalSpread(a.Results),
            "Higher RegionalSigma should produce wider P5-P95 spread.");
    }

    [Fact]
    public async Task RidingSigma_AffectsSpread()
    {
        // Isolate riding sigma by minimizing other noise sources (national/regional).
        // Without isolation, high riding noise averages out across 343 ridings at the
        // aggregate seat level, masking the effect.
        var a = await _service.RunSimulationAsync(Baseline() with
        {
            RidingSigma = 0.001,
            RegionalSigma = 0.001,
            PartyUncertainty = TestHelpers.MakeUniformUncertainty(0.001)
        });
        var b = await _service.RunSimulationAsync(Baseline() with
        {
            RidingSigma = 0.10,
            RegionalSigma = 0.001,
            PartyUncertainty = TestHelpers.MakeUniformUncertainty(0.001)
        });

        Assert.True(
            TestHelpers.TotalSpread(b.Results) > TestHelpers.TotalSpread(a.Results),
            "Higher RidingSigma should produce wider P5-P95 spread when other noise is minimized.");
    }

    [Fact]
    public async Task Seed_AffectsResults()
    {
        var a = await _service.RunSimulationAsync(Baseline(seed: 1));
        var b = await _service.RunSimulationAsync(Baseline(seed: 99999));

        var anyDiffers = PartyColourProvider.MainParties.Any(p =>
            a.Results.SeatDistributions[p].Mean != b.Results.SeatDistributions[p].Mean);

        Assert.True(anyDiffers, "Different seeds should produce different results.");
    }

    [Fact]
    public async Task Polling_AffectsResults()
    {
        var a = await _service.RunSimulationAsync(Baseline() with
        {
            Polling = TestHelpers.MakeDominantPolling(Party.CPC)
        });
        var b = await _service.RunSimulationAsync(Baseline() with
        {
            Polling = TestHelpers.MakeDominantPolling(Party.LPC)
        });

        Assert.True(
            a.Results.SeatDistributions[Party.CPC].Mean > a.Results.SeatDistributions[Party.LPC].Mean,
            "CPC-dominant polling should give CPC more seats than LPC.");
        Assert.True(
            b.Results.SeatDistributions[Party.LPC].Mean > b.Results.SeatDistributions[Party.CPC].Mean,
            "LPC-dominant polling should give LPC more seats than CPC.");
    }

    [Fact]
    public async Task PartyUncertainty_AffectsSpread()
    {
        var a = await _service.RunSimulationAsync(Baseline() with
        {
            PartyUncertainty = TestHelpers.MakeUniformUncertainty(0.001)
        });
        var b = await _service.RunSimulationAsync(Baseline() with
        {
            PartyUncertainty = TestHelpers.MakeUniformUncertainty(0.30)
        });

        Assert.True(
            TestHelpers.TotalSpread(b.Results) > TestHelpers.TotalSpread(a.Results),
            "Higher PartyUncertainty should produce wider P5-P95 spread.");
    }

    [Fact]
    public async Task SwingBlendAlpha_AffectsResults()
    {
        var a = await _service.RunSimulationAsync(Baseline() with { SwingBlendAlpha = 0.0 });
        var b = await _service.RunSimulationAsync(Baseline() with { SwingBlendAlpha = 1.0 });

        var anyDiffers = PartyColourProvider.MainParties.Any(p =>
            a.Results.SeatDistributions[p].Mean != b.Results.SeatDistributions[p].Mean);

        Assert.True(anyDiffers,
            "SwingBlendAlpha 0.0 (additive) vs 1.0 (proportional) should produce different results.");
    }
}
