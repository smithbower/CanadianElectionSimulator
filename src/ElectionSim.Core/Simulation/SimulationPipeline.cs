using ElectionSim.Core.Models;

namespace ElectionSim.Core.Simulation;

/// <summary>
/// Encapsulates the vote share projection pipeline: swing ratios → additive deltas →
/// blended projection → optional demographic prior → optional by-election blending.
/// </summary>
public static class SimulationPipeline
{
    public static double[,] ProjectVoteShares(
        IReadOnlyList<Riding> ridings,
        IReadOnlyList<RidingResult> baseline,
        IReadOnlyList<RegionalPoll> polls,
        SimulationConfig config,
        IReadOnlyList<RidingDemographics>? demographics = null,
        IReadOnlyList<IReadOnlyList<RidingResult>>? trainingElections = null,
        IReadOnlyList<PostElectionEvent>? postElectionEvents = null)
    {
        // If by-election results exist, create a blended baseline for those ridings
        var effectiveBaseline = baseline;
        if (postElectionEvents != null && config.ByElectionBlendWeight > 0)
        {
            effectiveBaseline = BlendByElectionBaselines(baseline, postElectionEvents, config.ByElectionBlendWeight);
        }

        var swingRatios = SwingCalculator.ComputeSwingRatios(polls, effectiveBaseline, ridings);
        var additiveDeltas = SwingCalculator.ComputeAdditiveDeltas(polls, effectiveBaseline, ridings);
        var projected = SwingCalculator.ProjectRidingVoteSharesBlended(
            effectiveBaseline, ridings, swingRatios, additiveDeltas, config.SwingBlendAlpha, polls);

        if (config.UseDemographicPrior && demographics != null)
        {
            var demographicPrior = DemographicPrior.ComputePrior(
                ridings, demographics, trainingElections ?? []);
            projected = SwingCalculator.BlendWithDemographicPrior(
                projected, demographicPrior, ridings, effectiveBaseline,
                config.DemographicBlendWeight);
        }

        return projected;
    }

    /// <summary>
    /// For ridings with by-election results, creates a blended baseline:
    /// effective = (1 - w) * generalElection + w * byElection
    /// </summary>
    private static List<RidingResult> BlendByElectionBaselines(
        IReadOnlyList<RidingResult> baseline,
        IReadOnlyList<PostElectionEvent> events,
        double blendWeight)
    {
        // Index by-election results by riding ID
        var byElectionResults = new Dictionary<int, ByElectionResult>();
        foreach (var evt in events.Where(e => e.Type == PostElectionEventType.ByElection && e.ByElectionResult != null))
        {
            byElectionResults[evt.RidingId] = evt.ByElectionResult!;
        }

        if (byElectionResults.Count == 0)
            return baseline as List<RidingResult> ?? [.. baseline];

        var blended = new List<RidingResult>(baseline.Count);
        foreach (var result in baseline)
        {
            if (!byElectionResults.TryGetValue(result.RidingId, out var byElection))
            {
                blended.Add(result);
                continue;
            }

            // Build party→share lookup from by-election
            var byElectionShares = new Dictionary<Party, double>();
            foreach (var c in byElection.Candidates)
                byElectionShares[c.Party] = c.VoteShare;

            // Blend: (1 - w) * general + w * byElection
            var allParties = result.Candidates.Select(c => c.Party)
                .Union(byElectionShares.Keys)
                .Distinct();

            var blendedCandidates = new List<CandidateResult>();
            foreach (var party in allParties)
            {
                var generalShare = result.Candidates.FirstOrDefault(c => c.Party == party)?.VoteShare ?? 0;
                var byElectionShare = byElectionShares.GetValueOrDefault(party);
                var effectiveShare = (1 - blendWeight) * generalShare + blendWeight * byElectionShare;
                if (effectiveShare > 0)
                {
                    blendedCandidates.Add(new CandidateResult(party, 0, effectiveShare));
                }
            }

            // Renormalize to sum to 1.0
            var total = blendedCandidates.Sum(c => c.VoteShare);
            if (total > 0)
            {
                blendedCandidates = blendedCandidates
                    .Select(c => new CandidateResult(c.Party, 0, c.VoteShare / total))
                    .OrderByDescending(c => c.VoteShare)
                    .ToList();
            }

            blended.Add(new RidingResult(result.RidingId, result.Year, blendedCandidates, result.TotalVotes));
        }

        return blended;
    }
}
