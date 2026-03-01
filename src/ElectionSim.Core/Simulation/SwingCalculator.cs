using ElectionSim.Core.Models;

namespace ElectionSim.Core.Simulation;

/// <summary>
/// Computes swing-based riding-level vote share projections from regional polling data
/// and a baseline election. Supports proportional, additive, and blended swing models,
/// plus optional demographic prior blending. See SIMULATION.md for methodology.
/// </summary>
public static class SwingCalculator
{
    /// <summary>
    /// Computes baseline regional vote shares by aggregating riding-level results per region.
    /// </summary>
    private static Dictionary<Region, Dictionary<Party, double>> ComputeBaselineRegionalShares(
        IReadOnlyList<RegionalPoll> currentPolls,
        IReadOnlyList<RidingResult> baselineResults,
        IReadOnlyList<Riding> ridings)
    {
        var ridingLookup = ridings.ToDictionary(r => r.Id);
        var result = new Dictionary<Region, Dictionary<Party, double>>();

        foreach (var poll in currentPolls)
        {
            var region = poll.Region;
            var regionRidings = baselineResults
                .Where(r => ridingLookup.ContainsKey(r.RidingId) && ridingLookup[r.RidingId].Region == region)
                .ToList();

            var baselineRegional = new Dictionary<Party, double>();
            foreach (var party in PartyColorProvider.MainParties)
            {
                double totalVotes = 0;
                double partyVotes = 0;
                foreach (var rr in regionRidings)
                {
                    totalVotes += rr.TotalVotes;
                    var candidate = rr.Candidates.FirstOrDefault(c => c.Party == party);
                    if (candidate != null)
                        partyVotes += candidate.Votes;
                }
                baselineRegional[party] = totalVotes > 0 ? partyVotes / totalVotes : 0;
            }

            result[region] = baselineRegional;
        }

        return result;
    }

    /// <summary>
    /// Computes regional swing ratios: currentPoll / baselineRegionalAvg for each party-region pair.
    /// </summary>
    public static Dictionary<Region, Dictionary<Party, double>> ComputeSwingRatios(
        IReadOnlyList<RegionalPoll> currentPolls,
        IReadOnlyList<RidingResult> baselineResults,
        IReadOnlyList<Riding> ridings)
    {
        var baselineShares = ComputeBaselineRegionalShares(currentPolls, baselineResults, ridings);
        var result = new Dictionary<Region, Dictionary<Party, double>>();

        foreach (var poll in currentPolls)
        {
            var region = poll.Region;
            var baselineRegional = baselineShares[region];
            var swings = new Dictionary<Party, double>();

            foreach (var party in PartyColorProvider.MainParties)
            {
                double baseline = baselineRegional.GetValueOrDefault(party, 0);
                double current = poll.VoteShares.GetValueOrDefault(party, 0);
                swings[party] = current / Math.Max(baseline, 0.01);
            }
            result[region] = swings;
        }

        return result;
    }

    /// <summary>
    /// Computes regional additive deltas: currentPoll - baselineRegionalAvg for each party-region pair.
    /// </summary>
    public static Dictionary<Region, Dictionary<Party, double>> ComputeAdditiveDeltas(
        IReadOnlyList<RegionalPoll> currentPolls,
        IReadOnlyList<RidingResult> baselineResults,
        IReadOnlyList<Riding> ridings)
    {
        var baselineShares = ComputeBaselineRegionalShares(currentPolls, baselineResults, ridings);
        var result = new Dictionary<Region, Dictionary<Party, double>>();

        foreach (var poll in currentPolls)
        {
            var region = poll.Region;
            var baselineRegional = baselineShares[region];
            var deltas = new Dictionary<Party, double>();

            foreach (var party in PartyColorProvider.MainParties)
            {
                double baseline = baselineRegional.GetValueOrDefault(party, 0);
                double current = poll.VoteShares.GetValueOrDefault(party, 0);
                deltas[party] = current - baseline;
            }
            result[region] = deltas;
        }

        return result;
    }

    /// <summary>
    /// Projects riding-level vote shares using a blend of proportional and additive swing.
    /// alpha=1.0 is pure proportional, alpha=0.0 is pure additive.
    /// </summary>
    public static double[,] ProjectRidingVoteSharesBlended(
        IReadOnlyList<RidingResult> baselineResults,
        IReadOnlyList<Riding> ridings,
        Dictionary<Region, Dictionary<Party, double>> swingRatios,
        Dictionary<Region, Dictionary<Party, double>> additiveDeltas,
        double alpha,
        IReadOnlyList<RegionalPoll>? currentPolls = null)
    {
        var parties = PartyColorProvider.MainParties;
        int numRidings = ridings.Count;
        int numParties = parties.Count;
        var projected = new double[numRidings, numParties];

        var ridingLookup = ridings.ToDictionary(r => r.Id);
        var baselineLookup = baselineResults.ToDictionary(r => r.RidingId);
        var pollLookup = currentPolls?.ToDictionary(p => p.Region, p => p.VoteShares);

        for (int ri = 0; ri < numRidings; ri++)
        {
            var riding = ridings[ri];
            if (!baselineLookup.TryGetValue(riding.Id, out var baseline))
            {
                // No baseline data - use current regional polling vote shares
                if (pollLookup != null && pollLookup.TryGetValue(riding.Region, out var regionShares))
                {
                    for (int pi = 0; pi < numParties; pi++)
                        projected[ri, pi] = regionShares.GetValueOrDefault(parties[pi], 0);
                }
                else if (swingRatios.TryGetValue(riding.Region, out var regionSwings))
                {
                    // Fallback to swing ratios if no polls provided (legacy behavior)
                    for (int pi = 0; pi < numParties; pi++)
                        projected[ri, pi] = regionSwings.GetValueOrDefault(parties[pi], 0);
                }
                continue;
            }

            var swings = swingRatios.GetValueOrDefault(riding.Region);
            var deltas = additiveDeltas.GetValueOrDefault(riding.Region);
            double sum = 0;

            for (int pi = 0; pi < numParties; pi++)
            {
                var party = parties[pi];
                var candidate = baseline.Candidates.FirstOrDefault(c => c.Party == party);
                double baseShare = candidate?.VoteShare ?? 0;

                double propProjected = baseShare;
                double addProjected = baseShare;

                if (swings != null && swings.TryGetValue(party, out double ratio))
                    propProjected = Math.Max(baseShare, 0.005) * ratio;

                if (deltas != null && deltas.TryGetValue(party, out double delta))
                    addProjected = baseShare + delta;

                projected[ri, pi] = alpha * propProjected + (1 - alpha) * addProjected;

                if (projected[ri, pi] < 0) projected[ri, pi] = 0;
                sum += projected[ri, pi];
            }

            // Normalize to sum to 1.0
            if (sum > 0)
            {
                for (int pi = 0; pi < numParties; pi++)
                    projected[ri, pi] /= sum;
            }
        }

        return projected;
    }

    /// <summary>
    /// Blends swing-projected vote shares with a demographic prior.
    /// For ridings with baseline data: final = (1-w) * projected + w * demographic.
    /// For ridings with no baseline: final = demographic (full weight).
    /// </summary>
    public static double[,] BlendWithDemographicPrior(
        double[,] projectedShares,
        double[,] demographicPrior,
        IReadOnlyList<Riding> ridings,
        IReadOnlyList<RidingResult> baselineResults,
        double blendWeight)
    {
        int numRidings = ridings.Count;
        int numParties = PartyColorProvider.MainParties.Count;
        var blended = new double[numRidings, numParties];
        var baselineIds = new HashSet<int>(baselineResults.Select(r => r.RidingId));

        for (int ri = 0; ri < numRidings; ri++)
        {
            bool hasBaseline = baselineIds.Contains(ridings[ri].Id);
            double w = hasBaseline ? blendWeight : 1.0;
            double sum = 0;

            for (int pi = 0; pi < numParties; pi++)
            {
                blended[ri, pi] = (1 - w) * projectedShares[ri, pi] + w * demographicPrior[ri, pi];
                if (blended[ri, pi] < 0) blended[ri, pi] = 0;
                sum += blended[ri, pi];
            }

            if (sum > 0)
            {
                for (int pi = 0; pi < numParties; pi++)
                    blended[ri, pi] /= sum;
            }
        }

        return blended;
    }
}
