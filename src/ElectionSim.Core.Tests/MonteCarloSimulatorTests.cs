using ElectionSim.Core.Models;
using ElectionSim.Core.Simulation;

namespace ElectionSim.Core.Tests;

public class MonteCarloSimulatorTests
{
    private readonly List<Riding> _ridings = TestHelpers.CreateTestRidings();
    private readonly List<RidingResult> _baseline = TestHelpers.CreateTestBaseline();
    private readonly List<RegionalPoll> _polls = TestHelpers.CreateTestPolls();

    private MonteCarloSimulator CreateSimulator()
    {
        var config = TestHelpers.CreateTestConfig();
        var swingRatios = SwingCalculator.ComputeSwingRatios(_polls, _baseline, _ridings);
        var additiveDeltas = SwingCalculator.ComputeAdditiveDeltas(_polls, _baseline, _ridings);
        var projected = SwingCalculator.ProjectRidingVoteSharesBlended(
            _baseline, _ridings, swingRatios, additiveDeltas, 0.0, _polls);
        return new MonteCarloSimulator(_ridings, projected);
    }

    [Fact]
    public void Run_WithSeed_ProducesConsistentDistributions()
    {
        // Note: Parallel.For with thread-local RNG (seed + threadId) means exact values
        // vary between runs due to thread scheduling. Instead of exact equality, verify
        // that two seeded runs produce results within a tight tolerance band.
        var simulator = CreateSimulator();
        var config = TestHelpers.CreateTestConfig(seed: 42, numSimulations: 500);

        var result1 = simulator.Run(config);
        var result2 = simulator.Run(config);

        foreach (var party in PartyColourProvider.MainParties)
        {
            // Mean seats should be close (within 0.5 seats for 5 ridings / 500 sims)
            Assert.InRange(
                Math.Abs(result1.SeatDistributions[party].Mean - result2.SeatDistributions[party].Mean),
                0, 0.5);
        }

        // Win probabilities should be close (within 5% for 500 sims)
        foreach (var ridingId in result1.RidingWinProbabilities.Keys)
        {
            foreach (var party in PartyColourProvider.MainParties)
            {
                Assert.InRange(
                    Math.Abs(result1.RidingWinProbabilities[ridingId][party] -
                             result2.RidingWinProbabilities[ridingId][party]),
                    0, 0.05);
            }
        }
    }

    [Fact]
    public void Run_SeatCountsSumToRidingCount()
    {
        var simulator = CreateSimulator();
        var config = TestHelpers.CreateTestConfig(seed: 42, numSimulations: 100);

        var result = simulator.Run(config);

        // Mean seats across all parties should sum to number of ridings
        double totalMeanSeats = result.SeatDistributions.Values.Sum(d => d.Mean);
        Assert.Equal(_ridings.Count, totalMeanSeats, precision: 3);
    }

    [Fact]
    public void Run_WinProbabilitiesSumToOne()
    {
        var simulator = CreateSimulator();
        var config = TestHelpers.CreateTestConfig(seed: 42, numSimulations: 500);

        var result = simulator.Run(config);

        foreach (var (ridingId, probs) in result.RidingWinProbabilities)
        {
            double total = probs.Values.Sum();
            Assert.InRange(total, 0.999, 1.001);
        }
    }

    [Fact]
    public void Run_VoteSharePercentilesOrdered()
    {
        var simulator = CreateSimulator();
        var config = TestHelpers.CreateTestConfig(seed: 42, numSimulations: 500);

        var result = simulator.Run(config);

        foreach (var (ridingId, partyDists) in result.RidingVoteShareDistributions)
        {
            foreach (var (party, dist) in partyDists)
            {
                Assert.True(dist.P5 <= dist.P25,
                    $"Riding {ridingId}, {party}: P5 ({dist.P5}) > P25 ({dist.P25})");
                Assert.True(dist.P25 <= dist.Median,
                    $"Riding {ridingId}, {party}: P25 ({dist.P25}) > Median ({dist.Median})");
                Assert.True(dist.Median <= dist.P75,
                    $"Riding {ridingId}, {party}: Median ({dist.Median}) > P75 ({dist.P75})");
                Assert.True(dist.P75 <= dist.P95,
                    $"Riding {ridingId}, {party}: P75 ({dist.P75}) > P95 ({dist.P95})");
            }
        }
    }

    [Fact]
    public void Run_MajorityPlusMinorityLeqOne()
    {
        var simulator = CreateSimulator();
        var config = TestHelpers.CreateTestConfig(seed: 42, numSimulations: 500);

        var result = simulator.Run(config);

        foreach (var party in PartyColourProvider.MainParties)
        {
            double majority = result.MajorityProbabilities.GetValueOrDefault(party, 0);
            double minority = result.MinorityProbabilities.GetValueOrDefault(party, 0);
            Assert.True(majority + minority <= 1.001,
                $"{party}: majority ({majority}) + minority ({minority}) > 1.0");
        }
    }

    [Fact]
    public void Run_WithGaussianNoise_Runs()
    {
        var simulator = CreateSimulator();
        var config = TestHelpers.CreateTestConfig(seed: 42, numSimulations: 100) with
        {
            DegreesOfFreedom = null // Gaussian fallback
        };

        var result = simulator.Run(config);

        Assert.Equal(100, result.TotalSimulations);
        Assert.Equal(_ridings.Count, result.RidingWinProbabilities.Count);
    }

    [Fact]
    public void Run_IndependentNoise_DiffersFromCorrelated()
    {
        var simulator = CreateSimulator();
        var configCorrelated = TestHelpers.CreateTestConfig(seed: 42, numSimulations: 500) with
        {
            UseCorrelatedNoise = true
        };
        var configIndependent = TestHelpers.CreateTestConfig(seed: 42, numSimulations: 500) with
        {
            UseCorrelatedNoise = false
        };

        var resultCorrelated = simulator.Run(configCorrelated);
        var resultIndependent = simulator.Run(configIndependent);

        // Results should differ when correlation is toggled
        bool anyDifferent = false;
        foreach (var party in PartyColourProvider.MainParties)
        {
            if (Math.Abs(resultCorrelated.SeatDistributions[party].Mean -
                         resultIndependent.SeatDistributions[party].Mean) > 0.01)
            {
                anyDifferent = true;
                break;
            }
        }
        Assert.True(anyDifferent,
            "Correlated and independent noise should produce different seat distributions");
    }

    [Fact]
    public void Run_ReportsProgress()
    {
        var simulator = CreateSimulator();
        var config = TestHelpers.CreateTestConfig(seed: 42, numSimulations: 100);

        var progressValues = new List<int>();
        var progress = new Progress<int>(v => progressValues.Add(v));

        simulator.Run(config, progress);

        // Progress should have been reported at least once
        // (may not fire immediately due to threading, but final value should be reported)
        Assert.True(progressValues.Count >= 1, "Progress should be reported at least once");
    }

    [Fact]
    public void Run_AllRidingsHaveVoteShareDistributions()
    {
        var simulator = CreateSimulator();
        var config = TestHelpers.CreateTestConfig(seed: 42, numSimulations: 100);

        var result = simulator.Run(config);

        foreach (var riding in _ridings)
        {
            Assert.Contains(riding.Id, result.RidingVoteShareDistributions.Keys);
            var partyDists = result.RidingVoteShareDistributions[riding.Id];
            foreach (var party in PartyColourProvider.MainParties)
            {
                Assert.Contains(party, partyDists.Keys);
            }
        }
    }
}
