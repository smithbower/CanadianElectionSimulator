using ElectionSim.Core.Models;

namespace ElectionSim.Core.Simulation;

/// <summary>
/// Encapsulates the vote share projection pipeline: swing ratios → additive deltas →
/// blended projection → optional demographic prior.
/// </summary>
public static class SimulationPipeline
{
    public static double[,] ProjectVoteShares(
        IReadOnlyList<Riding> ridings,
        IReadOnlyList<RidingResult> baseline,
        IReadOnlyList<RegionalPoll> polls,
        SimulationConfig config,
        IReadOnlyList<RidingDemographics>? demographics = null,
        IReadOnlyList<IReadOnlyList<RidingResult>>? trainingElections = null)
    {
        var swingRatios = SwingCalculator.ComputeSwingRatios(polls, baseline, ridings);
        var additiveDeltas = SwingCalculator.ComputeAdditiveDeltas(polls, baseline, ridings);
        var projected = SwingCalculator.ProjectRidingVoteSharesBlended(
            baseline, ridings, swingRatios, additiveDeltas, config.SwingBlendAlpha, polls);

        if (config.UseDemographicPrior && demographics != null)
        {
            var demographicPrior = DemographicPrior.ComputePrior(
                ridings, demographics, trainingElections ?? []);
            projected = SwingCalculator.BlendWithDemographicPrior(
                projected, demographicPrior, ridings, baseline,
                config.DemographicBlendWeight);
        }

        return projected;
    }
}
