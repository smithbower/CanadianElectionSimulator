using ElectionSim.Core.Models;
using ElectionSim.Core.Simulation;

namespace ElectionSim.Core.Tests;

public class SimulationPipelineTests
{
    private readonly List<Riding> _ridings = TestHelpers.CreateTestRidings();
    private readonly List<RidingResult> _baseline = TestHelpers.CreateTestBaseline();
    private readonly List<RegionalPoll> _polls = TestHelpers.CreateTestPolls();

    [Fact]
    public void ProjectVoteShares_OutputDimensions()
    {
        var config = new SimulationConfig();
        var projected = SimulationPipeline.ProjectVoteShares(_ridings, _baseline, _polls, config);

        Assert.Equal(_ridings.Count, projected.GetLength(0));
        Assert.Equal(PartyColourProvider.MainParties.Count, projected.GetLength(1));
    }

    [Fact]
    public void ProjectVoteShares_WithoutDemographics_SkipsDemoPrior()
    {
        var configWithDemo = new SimulationConfig(UseDemographicPrior: true, DemographicBlendWeight: 0.15);
        var configWithoutDemo = new SimulationConfig(UseDemographicPrior: false);

        // Without demographics data, even if UseDemographicPrior=true, should work (demographics=null)
        var projectedWithDemo = SimulationPipeline.ProjectVoteShares(
            _ridings, _baseline, _polls, configWithDemo, demographics: null);
        var projectedWithoutDemo = SimulationPipeline.ProjectVoteShares(
            _ridings, _baseline, _polls, configWithoutDemo, demographics: null);

        // Both should produce identical results since demographics is null
        for (int ri = 0; ri < _ridings.Count; ri++)
        {
            for (int pi = 0; pi < PartyColourProvider.MainParties.Count; pi++)
            {
                Assert.Equal(projectedWithDemo[ri, pi], projectedWithoutDemo[ri, pi], precision: 10);
            }
        }
    }

    [Fact]
    public void ProjectVoteShares_WithDemographics_DiffersFromWithout()
    {
        var demographics = TestHelpers.CreateTestDemographics();
        var trainingElections = new List<IReadOnlyList<RidingResult>> { _baseline, _baseline };

        var configWith = new SimulationConfig(UseDemographicPrior: true, DemographicBlendWeight: 0.15);
        var configWithout = new SimulationConfig(UseDemographicPrior: false);

        var projectedWith = SimulationPipeline.ProjectVoteShares(
            _ridings, _baseline, _polls, configWith, demographics, trainingElections);
        var projectedWithout = SimulationPipeline.ProjectVoteShares(
            _ridings, _baseline, _polls, configWithout);

        bool anyDifferent = false;
        for (int ri = 0; ri < _ridings.Count; ri++)
        {
            for (int pi = 0; pi < PartyColourProvider.MainParties.Count; pi++)
            {
                if (Math.Abs(projectedWith[ri, pi] - projectedWithout[ri, pi]) > 1e-6)
                {
                    anyDifferent = true;
                    break;
                }
            }
            if (anyDifferent) break;
        }
        Assert.True(anyDifferent, "Demographic prior should change projected vote shares");
    }

    [Fact]
    public void ProjectVoteShares_OutputNormalized()
    {
        var config = new SimulationConfig();
        var projected = SimulationPipeline.ProjectVoteShares(_ridings, _baseline, _polls, config);

        for (int ri = 0; ri < _ridings.Count; ri++)
        {
            double sum = 0;
            for (int pi = 0; pi < PartyColourProvider.MainParties.Count; pi++)
                sum += projected[ri, pi];

            Assert.InRange(sum, 0.99, 1.01);
        }
    }

    [Fact]
    public void ProjectVoteShares_NonNegativeShares()
    {
        var config = new SimulationConfig();
        var projected = SimulationPipeline.ProjectVoteShares(_ridings, _baseline, _polls, config);

        for (int ri = 0; ri < _ridings.Count; ri++)
        {
            for (int pi = 0; pi < PartyColourProvider.MainParties.Count; pi++)
            {
                Assert.True(projected[ri, pi] >= 0,
                    $"Projected share should be non-negative: riding={ri}, party={pi}, value={projected[ri, pi]}");
            }
        }
    }

    [Fact]
    public void ProjectVoteShares_WithByElections_BlendsCorrectly()
    {
        var events = new List<PostElectionEvent>
        {
            new PostElectionEvent(
                RidingId: 1001,
                Type: PostElectionEventType.ByElection,
                Date: new DateOnly(2025, 8, 1),
                ElectionYear: 2025,
                FromParty: null,
                ToParty: Party.CPC,
                Description: "Test by-election",
                ByElectionResult: new ByElectionResult(
                [
                    new CandidateResult(Party.CPC, 20000, 0.55),
                    new CandidateResult(Party.LPC, 12000, 0.33),
                    new CandidateResult(Party.NDP, 3000, 0.08),
                    new CandidateResult(Party.GPC, 500, 0.014),
                    new CandidateResult(Party.PPC, 500, 0.014),
                    new CandidateResult(Party.Other, 500, 0.012),
                ], 36500)
            )
        };

        var configWithByElection = new SimulationConfig(ByElectionBlendWeight: 0.3);
        var configWithoutByElection = new SimulationConfig(ByElectionBlendWeight: 0.0);

        var projectedWith = SimulationPipeline.ProjectVoteShares(
            _ridings, _baseline, _polls, configWithByElection, postElectionEvents: events);
        var projectedWithout = SimulationPipeline.ProjectVoteShares(
            _ridings, _baseline, _polls, configWithoutByElection);

        // Riding 1001 (index 0) should be affected by the by-election blending
        bool riding1001Differs = false;
        for (int pi = 0; pi < PartyColourProvider.MainParties.Count; pi++)
        {
            if (Math.Abs(projectedWith[0, pi] - projectedWithout[0, pi]) > 1e-6)
            {
                riding1001Differs = true;
                break;
            }
        }
        Assert.True(riding1001Differs,
            "By-election blending should change vote shares for the affected riding");

        // Quebec ridings (indices 3, 4) should be unaffected since the by-election is in Ontario
        for (int pi = 0; pi < PartyColourProvider.MainParties.Count; pi++)
        {
            Assert.Equal(projectedWith[3, pi], projectedWithout[3, pi], precision: 6);
        }
    }
}
