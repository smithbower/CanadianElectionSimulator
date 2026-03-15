using ElectionSim.Core.Models;
using ElectionSim.DailyRunner.Scraping;
using Microsoft.Extensions.Logging;

namespace ElectionSim.DailyRunner.Weighting;

/// <summary>
/// Computes weighted polling averages from scraped polls. Weights combine recency decay
/// (5%/day), sample size (inverse MoE squared), and firm grade. No single poll can exceed
/// 50% of total weight. Also estimates per-party uncertainty from inter-poll variance.
/// </summary>
public class PollWeightCalculator(ILogger<PollWeightCalculator> logger)
{
    private static readonly Dictionary<string, double> GradeWeights = new(StringComparer.OrdinalIgnoreCase)
    {
        ["A+"] = 1.0,
        ["A"]  = 0.95,
        ["A-"] = 0.9,
        ["B+"] = 0.85,
        ["B"]  = 0.8,
        ["B-"] = 0.75,
        ["C+"] = 0.7,
        ["C"]  = 0.65,
        ["C-"] = 0.6,
        ["D+"] = 0.55,
        ["D"]  = 0.5,
        ["D-"] = 0.45,
        ["F"]  = 0.3,
    };

    /// <summary>
    /// Computes weighted average vote shares per region from scraped polls.
    /// Returns polling dictionary suitable for the simulation API.
    /// </summary>
    public Dictionary<Region, Dictionary<Party, double>> ComputeWeightedAverages(
        List<ScrapedPoll> polls, out Dictionary<Party, double> partyUncertainty)
    {
        var result = new Dictionary<Region, Dictionary<Party, double>>();
        var allPartyDeviations = new Dictionary<Party, List<(double deviation, double weight)>>();

        var byRegion = polls.GroupBy(p => p.Region);

        foreach (var group in byRegion)
        {
            var region = group.Key;
            var regionPolls = group.ToList();

            logger.LogInformation("Computing weighted average for {Region} from {Count} polls",
                region, regionPolls.Count);

            var weights = ComputeWeights(regionPolls);
            var weightedShares = ComputeWeightedShares(regionPolls, weights);

            // Normalize shares to sum to 1.0
            var total = weightedShares.Values.Sum();
            if (total > 0)
            {
                foreach (var party in weightedShares.Keys.ToList())
                    weightedShares[party] /= total;
            }

            result[region] = weightedShares;

            // Accumulate deviations for uncertainty calculation
            AccumulateDeviations(regionPolls, weights, weightedShares, allPartyDeviations);

            LogWeightedAverages(region, weightedShares);
        }

        partyUncertainty = ComputePartyUncertainty(allPartyDeviations);
        return result;
    }

    private double[] ComputeWeights(List<ScrapedPoll> polls)
    {
        var weights = new double[polls.Count];
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        for (int i = 0; i < polls.Count; i++)
        {
            var poll = polls[i];

            // Age weighting: ~5% decay per day (non-campaign)
            var pollDate = GetEffectivePollDate(poll);
            int daysSince = today.DayNumber - pollDate.DayNumber;
            double ageWeight = Math.Pow(0.95, Math.Max(0, daysSince));

            // Sample size weighting: proportional to sample size (inverse of MoE squared)
            double moe = 1.96 * Math.Sqrt(0.25 / poll.SampleSize);
            double sizeWeight = 1.0 / (moe * moe);

            // Firm track record weighting
            double firmWeight = GradeWeights.GetValueOrDefault(poll.FirmGrade, 0.65);

            weights[i] = ageWeight * sizeWeight * firmWeight;
        }

        // Normalize weights to sum to 1.0
        double totalWeight = weights.Sum();
        if (totalWeight > 0)
        {
            for (int i = 0; i < weights.Length; i++)
                weights[i] /= totalWeight;
        }

        // Cap: no single poll > 50% of total weight
        ApplyWeightCap(weights, maxShare: 0.50);

        return weights;
    }

    private static DateOnly GetEffectivePollDate(ScrapedPoll poll)
    {
        // For non-campaign periods: if field period > 14 days, use start + 14
        int fieldDays = poll.EndDate.DayNumber - poll.StartDate.DayNumber;
        if (fieldDays > 14)
            return poll.StartDate.AddDays(14);
        return poll.EndDate;
    }

    private static void ApplyWeightCap(double[] weights, double maxShare)
    {
        bool changed = true;
        while (changed)
        {
            changed = false;
            double total = weights.Sum();
            if (total <= 0) return;

            for (int i = 0; i < weights.Length; i++)
            {
                double share = weights[i] / total;
                if (share > maxShare)
                {
                    weights[i] = maxShare * total;
                    changed = true;
                }
            }

            // Re-normalize
            total = weights.Sum();
            if (total > 0)
            {
                for (int i = 0; i < weights.Length; i++)
                    weights[i] /= total;
            }
        }
    }

    private static Dictionary<Party, double> ComputeWeightedShares(
        List<ScrapedPoll> polls, double[] weights)
    {
        var result = new Dictionary<Party, double>();

        for (int i = 0; i < polls.Count; i++)
        {
            foreach (var (party, share) in polls[i].VoteShares)
            {
                if (!result.ContainsKey(party))
                    result[party] = 0;
                result[party] += share * weights[i];
            }
        }

        return result;
    }

    private static void AccumulateDeviations(
        List<ScrapedPoll> polls,
        double[] weights,
        Dictionary<Party, double> weightedMeans,
        Dictionary<Party, List<(double deviation, double weight)>> allDeviations)
    {
        for (int i = 0; i < polls.Count; i++)
        {
            foreach (var (party, share) in polls[i].VoteShares)
            {
                if (!weightedMeans.TryGetValue(party, out double mean)) continue;
                double deviation = share - mean;

                if (!allDeviations.ContainsKey(party))
                    allDeviations[party] = [];
                allDeviations[party].Add((deviation, weights[i]));
            }
        }
    }

    private Dictionary<Party, double> ComputePartyUncertainty(
        Dictionary<Party, List<(double deviation, double weight)>> allDeviations)
    {
        var result = new Dictionary<Party, double>();

        foreach (var (party, deviations) in allDeviations)
        {
            if (deviations.Count < 2)
            {
                // Default uncertainty when we have too few polls
                result[party] = 0.025;
                continue;
            }

            double totalWeight = deviations.Sum(d => d.weight);
            if (totalWeight <= 0)
            {
                result[party] = 0.025;
                continue;
            }

            double weightedVariance = deviations.Sum(d => d.weight * d.deviation * d.deviation) / totalWeight;
            double sigma = Math.Sqrt(weightedVariance);

            // Clamp to reasonable range
            result[party] = Math.Clamp(sigma, 0.005, 0.10);

            logger.LogInformation("Party {Party} uncertainty: {Sigma:P1}", party, sigma);
        }

        // Ensure all main parties have an entry
        foreach (var party in PartyColourProvider.MainParties)
        {
            if (!result.ContainsKey(party))
                result[party] = 0.025;
        }

        return result;
    }

    private void LogWeightedAverages(Region region, Dictionary<Party, double> shares)
    {
        var parts = shares.OrderByDescending(kv => kv.Value)
            .Select(kv => $"{kv.Key}: {kv.Value:P1}");
        logger.LogInformation("{Region} weighted averages: {Shares}",
            region, string.Join(", ", parts));
    }
}
