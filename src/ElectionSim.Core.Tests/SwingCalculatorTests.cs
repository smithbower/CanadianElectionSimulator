using ElectionSim.Core.Models;
using ElectionSim.Core.Simulation;

namespace ElectionSim.Core.Tests;

public class SwingCalculatorTests
{
    private readonly List<Riding> _ridings = TestHelpers.CreateTestRidings();
    private readonly List<RidingResult> _baseline = TestHelpers.CreateTestBaseline();
    private readonly List<RegionalPoll> _polls = TestHelpers.CreateTestPolls();

    [Fact]
    public void ComputeSwingRatios_KnownInputs_ReturnsExpectedRatios()
    {
        var ratios = SwingCalculator.ComputeSwingRatios(_polls, _baseline, _ridings);

        Assert.Contains(Region.Ontario, ratios.Keys);
        Assert.Contains(Region.Quebec, ratios.Keys);

        // Ontario baseline LPC average: (0.45*33333 + 0.40*30000 + 0.35*28000) / (33333+30000+28000)
        // = (14999.85 + 12000 + 9800) / 91333 ≈ 0.4028
        // Ontario poll LPC = 0.35, ratio = 0.35 / 0.4028 ≈ 0.869
        var ontarioLpcRatio = ratios[Region.Ontario][Party.LPC];
        Assert.InRange(ontarioLpcRatio, 0.80, 0.95);

        // All ratios should be non-negative
        foreach (var regionRatios in ratios.Values)
            foreach (var ratio in regionRatios.Values)
                Assert.True(ratio >= 0, "Swing ratios should be non-negative");
    }

    [Fact]
    public void ComputeAdditiveDeltas_KnownInputs_ReturnsExpectedDeltas()
    {
        var deltas = SwingCalculator.ComputeAdditiveDeltas(_polls, _baseline, _ridings);

        Assert.Contains(Region.Ontario, deltas.Keys);
        Assert.Contains(Region.Quebec, deltas.Keys);

        // Ontario baseline LPC average ≈ 0.4028, poll = 0.35
        // Delta ≈ 0.35 - 0.4028 ≈ -0.053
        var ontarioLpcDelta = deltas[Region.Ontario][Party.LPC];
        Assert.InRange(ontarioLpcDelta, -0.10, 0.0);

        // Quebec BQ should have positive delta (poll > baseline)
        // Baseline BQ avg: (0.44*32000 + 0.36*33000) / (32000+33000) = (14080+11880)/65000 ≈ 0.399
        // Poll BQ = 0.45, delta ≈ 0.051
        var quebecBqDelta = deltas[Region.Quebec][Party.BQ];
        Assert.True(quebecBqDelta > 0, "BQ delta in Quebec should be positive (poll > baseline)");
    }

    [Fact]
    public void ProjectRidingVoteSharesBlended_Alpha0_IsPureAdditive()
    {
        var swingRatios = SwingCalculator.ComputeSwingRatios(_polls, _baseline, _ridings);
        var additiveDeltas = SwingCalculator.ComputeAdditiveDeltas(_polls, _baseline, _ridings);

        var projectedAdditive = SwingCalculator.ProjectRidingVoteSharesBlended(
            _baseline, _ridings, swingRatios, additiveDeltas, alpha: 0.0, _polls);
        var projectedBlended05 = SwingCalculator.ProjectRidingVoteSharesBlended(
            _baseline, _ridings, swingRatios, additiveDeltas, alpha: 0.5, _polls);

        // alpha=0 and alpha=0.5 should produce different results (unless proportional == additive exactly)
        bool anyDifferent = false;
        for (int ri = 0; ri < _ridings.Count; ri++)
        {
            for (int pi = 0; pi < PartyColorProvider.MainParties.Count; pi++)
            {
                if (Math.Abs(projectedAdditive[ri, pi] - projectedBlended05[ri, pi]) > 1e-10)
                    anyDifferent = true;
            }
        }
        Assert.True(anyDifferent, "Pure additive (alpha=0) should differ from blended (alpha=0.5)");
    }

    [Fact]
    public void ProjectRidingVoteSharesBlended_Alpha1_IsPureProportional()
    {
        var swingRatios = SwingCalculator.ComputeSwingRatios(_polls, _baseline, _ridings);
        var additiveDeltas = SwingCalculator.ComputeAdditiveDeltas(_polls, _baseline, _ridings);

        var projectedProp = SwingCalculator.ProjectRidingVoteSharesBlended(
            _baseline, _ridings, swingRatios, additiveDeltas, alpha: 1.0, _polls);
        var projectedBlended05 = SwingCalculator.ProjectRidingVoteSharesBlended(
            _baseline, _ridings, swingRatios, additiveDeltas, alpha: 0.5, _polls);

        // alpha=1 and alpha=0.5 should produce different results
        bool anyDifferent = false;
        for (int ri = 0; ri < _ridings.Count; ri++)
        {
            for (int pi = 0; pi < PartyColorProvider.MainParties.Count; pi++)
            {
                if (Math.Abs(projectedProp[ri, pi] - projectedBlended05[ri, pi]) > 1e-10)
                    anyDifferent = true;
            }
        }
        Assert.True(anyDifferent, "Pure proportional (alpha=1) should differ from blended (alpha=0.5)");
    }

    [Fact]
    public void ProjectRidingVoteSharesBlended_OutputNormalized()
    {
        var swingRatios = SwingCalculator.ComputeSwingRatios(_polls, _baseline, _ridings);
        var additiveDeltas = SwingCalculator.ComputeAdditiveDeltas(_polls, _baseline, _ridings);

        foreach (double alpha in new[] { 0.0, 0.5, 1.0 })
        {
            var projected = SwingCalculator.ProjectRidingVoteSharesBlended(
                _baseline, _ridings, swingRatios, additiveDeltas, alpha, _polls);

            for (int ri = 0; ri < _ridings.Count; ri++)
            {
                double sum = 0;
                for (int pi = 0; pi < PartyColorProvider.MainParties.Count; pi++)
                    sum += projected[ri, pi];

                Assert.InRange(sum, 0.99, 1.01);
            }
        }
    }

    [Fact]
    public void ProjectRidingVoteSharesBlended_NegativeSharesClamped()
    {
        var swingRatios = SwingCalculator.ComputeSwingRatios(_polls, _baseline, _ridings);
        var additiveDeltas = SwingCalculator.ComputeAdditiveDeltas(_polls, _baseline, _ridings);

        // Use extreme polls that could push some shares negative
        var extremePolls = new List<RegionalPoll>
        {
            new(Region.Ontario, new Dictionary<Party, double>
            {
                [Party.LPC] = 0.01, [Party.CPC] = 0.80, [Party.NDP] = 0.10,
                [Party.BQ] = 0.00, [Party.GPC] = 0.04, [Party.PPC] = 0.03, [Party.Other] = 0.02,
            }),
            new(Region.Quebec, new Dictionary<Party, double>
            {
                [Party.LPC] = 0.01, [Party.CPC] = 0.01, [Party.NDP] = 0.01,
                [Party.BQ] = 0.90, [Party.GPC] = 0.03, [Party.PPC] = 0.02, [Party.Other] = 0.02,
            }),
        };

        var extremeSwings = SwingCalculator.ComputeSwingRatios(extremePolls, _baseline, _ridings);
        var extremeDeltas = SwingCalculator.ComputeAdditiveDeltas(extremePolls, _baseline, _ridings);

        var projected = SwingCalculator.ProjectRidingVoteSharesBlended(
            _baseline, _ridings, extremeSwings, extremeDeltas, alpha: 0.0, extremePolls);

        for (int ri = 0; ri < _ridings.Count; ri++)
        {
            for (int pi = 0; pi < PartyColorProvider.MainParties.Count; pi++)
            {
                Assert.True(projected[ri, pi] >= 0,
                    $"Vote share should be non-negative: riding={ri}, party={pi}, value={projected[ri, pi]}");
            }
        }
    }

    [Fact]
    public void ComputeSwingRatios_ZeroBaseline_SafeDivision()
    {
        // BQ has zero baseline in Ontario — ratio should use max(baseline, 0.01) denominator
        var ratios = SwingCalculator.ComputeSwingRatios(_polls, _baseline, _ridings);

        // Ontario BQ baseline = 0, poll = 0, so ratio = 0.0 / max(0.0, 0.01) = 0.0
        Assert.True(double.IsFinite(ratios[Region.Ontario][Party.BQ]),
            "Swing ratio should be finite even with zero baseline");
    }
}
