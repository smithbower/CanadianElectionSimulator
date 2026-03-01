using System.Text.Json;
using System.Text.Json.Serialization;
using ElectionSim.Core.Models;
using ElectionSim.Core.Simulation;

namespace ElectionSim.DataTools.Commands;

/// <summary>
/// Compares simulation results with and without demographic prior,
/// using current polling data and shifted polling scenarios.
/// </summary>
public static class CompareDemoCommand
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public static async Task RunAsync(string dataDir)
    {
        var processedDir = Path.Combine(dataDir, "processed");

        Console.WriteLine("=== Demographic Prior Impact Analysis ===");
        Console.WriteLine();

        // Load data
        var ridings = await LoadJson<List<Riding>>(Path.Combine(processedDir, "ridings.json"));
        var results2025 = await LoadJson<List<RidingResult>>(Path.Combine(processedDir, "results-2025.json"));
        var results2021 = await LoadJson<List<RidingResult>>(Path.Combine(processedDir, "results-2021.json"));
        var polling = await LoadJson<List<RegionalPoll>>(Path.Combine(processedDir, "polling.json"));
        var demographics = await LoadJson<List<RidingDemographics>>(Path.Combine(processedDir, "demographics.json"));

        if (ridings == null || results2025 == null || results2021 == null || polling == null)
        {
            Console.WriteLine("ERROR: Missing data files. Run 'process' first.");
            return;
        }

        if (demographics == null)
        {
            Console.WriteLine("ERROR: Missing demographics.json. Run 'demographics' first.");
            return;
        }

        var parties = PartyColorProvider.MainParties;
        var config = new SimulationConfig(
            NumSimulations: 10_000,
            Seed: 42
        );

        // Scenario 1: Current polling (matches 2025 baseline)
        Console.WriteLine("--- Scenario 1: Current Polling (2025 baseline) ---");
        Console.WriteLine("  Polling matches the 2025 election results.");
        Console.WriteLine();
        RunComparison("Current", ridings, results2025, polling, demographics, results2025, results2021, config, parties);

        // Scenario 2: CPC +5, LPC -5 nationally
        Console.WriteLine();
        Console.WriteLine("--- Scenario 2: CPC +5%, LPC -5% (uniform shift) ---");
        Console.WriteLine("  Simulates a ~5-point swing from LPC to CPC across all regions.");
        Console.WriteLine();
        var shiftedPolling = ShiftPolling(polling, Party.CPC, +0.05, Party.LPC, -0.05);
        RunComparison("CPC+5", ridings, results2025, shiftedPolling, demographics, results2025, results2021, config, parties);

        // Scenario 3: NDP +10, LPC -5, CPC -5
        Console.WriteLine();
        Console.WriteLine("--- Scenario 3: NDP +10%, LPC -5%, CPC -5% ---");
        Console.WriteLine("  Simulates an NDP surge drawing from both major parties.");
        Console.WriteLine();
        var ndpSurge = ShiftPolling(polling, Party.NDP, +0.10, Party.LPC, -0.05);
        ndpSurge = ShiftPolling(ndpSurge, Party.CPC, -0.05);
        RunComparison("NDP+10", ridings, results2025, ndpSurge, demographics, results2025, results2021, config, parties);

        // Scenario 4: BQ +10, LPC -10 in Quebec
        Console.WriteLine();
        Console.WriteLine("--- Scenario 4: BQ +10%, LPC -10% (Quebec only) ---");
        Console.WriteLine("  Simulates a BQ resurgence in Quebec.");
        Console.WriteLine();
        var bqSurge = ShiftPollingRegional(polling, Region.Quebec, Party.BQ, +0.10, Party.LPC, -0.10);
        RunComparison("BQ+10QC", ridings, results2025, bqSurge, demographics, results2025, results2021, config, parties);
    }

    private static void RunComparison(
        string label,
        List<Riding> ridings,
        List<RidingResult> baseline,
        List<RegionalPoll> polling,
        List<RidingDemographics> demographics,
        List<RidingResult> results2025,
        List<RidingResult> results2021,
        SimulationConfig config,
        IReadOnlyList<Party> parties)
    {
        // Project vote shares (base)
        var swingRatios = SwingCalculator.ComputeSwingRatios(polling, baseline, ridings);
        var additiveDeltas = SwingCalculator.ComputeAdditiveDeltas(polling, baseline, ridings);
        var projectedBase = SwingCalculator.ProjectRidingVoteSharesBlended(
            baseline, ridings, swingRatios, additiveDeltas, config.SwingBlendAlpha, polling);

        // Project vote shares (with demographics)
        var projectedDemo = (double[,])projectedBase.Clone();
        var trainingElections = new List<IReadOnlyList<RidingResult>> { results2025, results2021 };
        var demographicPrior = DemographicPrior.ComputePrior(ridings, demographics, trainingElections);
        projectedDemo = SwingCalculator.BlendWithDemographicPrior(
            projectedDemo, demographicPrior, ridings, baseline, config.DemographicBlendWeight);

        // Analyze projected vote share differences before simulation
        int numRidings = ridings.Count;
        int numParties = parties.Count;

        double maxDiff = 0;
        double sumAbsDiff = 0;
        int totalEntries = 0;
        int flippedWinners = 0;

        for (int ri = 0; ri < numRidings; ri++)
        {
            int baseWinner = 0, demoWinner = 0;
            double baseMax = 0, demoMax = 0;

            for (int pi = 0; pi < numParties; pi++)
            {
                double diff = Math.Abs(projectedDemo[ri, pi] - projectedBase[ri, pi]);
                sumAbsDiff += diff;
                totalEntries++;
                if (diff > maxDiff) maxDiff = diff;

                if (projectedBase[ri, pi] > baseMax) { baseMax = projectedBase[ri, pi]; baseWinner = pi; }
                if (projectedDemo[ri, pi] > demoMax) { demoMax = projectedDemo[ri, pi]; demoWinner = pi; }
            }

            if (baseWinner != demoWinner) flippedWinners++;
        }

        Console.WriteLine("  Pre-simulation projected vote share differences:");
        Console.WriteLine($"    Mean absolute diff per party-riding: {(sumAbsDiff / totalEntries * 100):F3}%");
        Console.WriteLine($"    Max absolute diff:                   {(maxDiff * 100):F2}%");
        Console.WriteLine($"    Projected winner flips:              {flippedWinners}/{numRidings}");
        Console.WriteLine();

        // Run simulations
        var simBase = new MonteCarloSimulator(ridings, projectedBase);
        var simDemo = new MonteCarloSimulator(ridings, projectedDemo);

        var summaryBase = simBase.Run(config);
        var summaryDemo = simDemo.Run(config);

        // Compare seat distributions
        Console.WriteLine("  Seat projections (mean seats):");
        Console.WriteLine("  {0,-8} {1,8} {2,8} {3,8}", "Party", "Base", "Demo", "Diff");
        Console.WriteLine("  {0,-8} {1,8} {2,8} {3,8}", "-----", "----", "----", "----");

        foreach (var party in parties)
        {
            var baseMean = summaryBase.SeatDistributions[party].Mean;
            var demoMean = summaryDemo.SeatDistributions[party].Mean;
            var diff = demoMean - baseMean;
            var sign = diff >= 0 ? "+" : "";
            Console.WriteLine("  {0,-8} {1,8:F1} {2,8:F1} {3,8}", party, baseMean, demoMean, sign + diff.ToString("F1"));
        }

        // Compare seat ranges
        Console.WriteLine();
        Console.WriteLine("  Seat ranges (P5-P95):");
        Console.WriteLine("  {0,-8} {1,16} {2,16}", "Party", "Base P5-P95", "Demo P5-P95");
        Console.WriteLine("  {0,-8} {1,16} {2,16}", "-----", "-----------", "-----------");

        foreach (var party in parties)
        {
            var b = summaryBase.SeatDistributions[party];
            var d = summaryDemo.SeatDistributions[party];
            Console.WriteLine("  {0,-8} {1,16} {2,16}", party, b.P5 + "-" + b.P95, d.P5 + "-" + d.P95);
        }

        // Majority/minority probabilities
        Console.WriteLine();
        Console.WriteLine("  Government probabilities:");
        Console.WriteLine("  {0,-8} {1,10} {2,10} {3,10} {4,10}", "Party", "Base Maj%", "Demo Maj%", "Base Min%", "Demo Min%");
        Console.WriteLine("  {0,-8} {1,10} {2,10} {3,10} {4,10}", "-----", "--------", "--------", "--------", "--------");

        foreach (var party in parties)
        {
            var baseMaj = summaryBase.MajorityProbabilities.GetValueOrDefault(party);
            var demoMaj = summaryDemo.MajorityProbabilities.GetValueOrDefault(party);
            var baseMin = summaryBase.MinorityProbabilities.GetValueOrDefault(party);
            var demoMin = summaryDemo.MinorityProbabilities.GetValueOrDefault(party);
            if (baseMaj < 0.001 && demoMaj < 0.001 && baseMin < 0.001 && demoMin < 0.001) continue;
            Console.WriteLine("  {0,-8} {1,9:F1}% {2,9:F1}% {3,9:F1}% {4,9:F1}%", party, baseMaj * 100, demoMaj * 100, baseMin * 100, demoMin * 100);
        }

        // Count ridings where win probability changes significantly
        int significantRidingChanges = 0;
        int ridingWinnerFlips = 0;
        double maxProbDiff = 0;

        for (int ri = 0; ri < numRidings; ri++)
        {
            var ridingId = ridings[ri].Id;
            var baseProbs = summaryBase.RidingWinProbabilities[ridingId];
            var demoProbs = summaryDemo.RidingWinProbabilities[ridingId];

            var baseLeader = baseProbs.MaxBy(p => p.Value).Key;
            var demoLeader = demoProbs.MaxBy(p => p.Value).Key;

            if (baseLeader != demoLeader) ridingWinnerFlips++;

            foreach (var party in parties)
            {
                var diff = Math.Abs(
                    demoProbs.GetValueOrDefault(party) - baseProbs.GetValueOrDefault(party));
                if (diff > maxProbDiff) maxProbDiff = diff;
                if (diff > 0.02) significantRidingChanges++;
            }
        }

        Console.WriteLine();
        Console.WriteLine("  Riding-level impact:");
        Console.WriteLine($"    Predicted winner flips (by win prob): {ridingWinnerFlips}/{numRidings}");
        Console.WriteLine($"    Party-riding pairs with >2pp change:  {significantRidingChanges}");
        Console.WriteLine($"    Max win probability shift:            {maxProbDiff * 100:F1}pp");

        // Show top 10 ridings with biggest impact
        var ridingImpacts = new List<(int RidingId, string Name, Party BaseWinner, Party DemoWinner, double MaxDiff)>();
        for (int ri = 0; ri < numRidings; ri++)
        {
            var ridingId = ridings[ri].Id;
            var baseProbs = summaryBase.RidingWinProbabilities[ridingId];
            var demoProbs = summaryDemo.RidingWinProbabilities[ridingId];

            var baseLeader = baseProbs.MaxBy(p => p.Value).Key;
            var demoLeader = demoProbs.MaxBy(p => p.Value).Key;

            double maxD = 0;
            foreach (var party in parties)
            {
                var d = Math.Abs(demoProbs.GetValueOrDefault(party) - baseProbs.GetValueOrDefault(party));
                if (d > maxD) maxD = d;
            }

            ridingImpacts.Add((ridingId, ridings[ri].Name, baseLeader, demoLeader, maxD));
        }

        var top10 = ridingImpacts.OrderByDescending(r => r.MaxDiff).Take(10).ToList();
        Console.WriteLine();
        Console.WriteLine("  Top 10 most-affected ridings:");
        Console.WriteLine("  {0,-35} {1,-5} {2,-5} {3,8}", "Riding", "Base", "Demo", "MaxShift");
        Console.WriteLine("  {0,-35} {1,-5} {2,-5} {3,8}", "------", "----", "----", "--------");

        foreach (var r in top10)
        {
            var flip = r.BaseWinner != r.DemoWinner ? " *" : "";
            Console.WriteLine("  {0,-35} {1,-5} {2,-5} {3,7:F1}pp{4}", r.Name, r.BaseWinner, r.DemoWinner, r.MaxDiff * 100, flip);
        }
    }

    private static List<RegionalPoll> ShiftPolling(
        List<RegionalPoll> polls, Party party, double shift, Party? party2 = null, double shift2 = 0)
    {
        return polls.Select(p =>
        {
            var newShares = new Dictionary<Party, double>(p.VoteShares);
            if (newShares.ContainsKey(party))
                newShares[party] = Math.Max(0, newShares[party] + shift);
            if (party2 != null && newShares.ContainsKey(party2.Value))
                newShares[party2.Value] = Math.Max(0, newShares[party2.Value] + shift2);
            return new RegionalPoll(p.Region, newShares);
        }).ToList();
    }

    private static List<RegionalPoll> ShiftPollingRegional(
        List<RegionalPoll> polls, Region region, Party party, double shift, Party party2, double shift2)
    {
        return polls.Select(p =>
        {
            if (p.Region != region) return p;
            var newShares = new Dictionary<Party, double>(p.VoteShares);
            if (newShares.ContainsKey(party))
                newShares[party] = Math.Max(0, newShares[party] + shift);
            if (newShares.ContainsKey(party2))
                newShares[party2] = Math.Max(0, newShares[party2] + shift2);
            return new RegionalPoll(p.Region, newShares);
        }).ToList();
    }

    private static async Task<T?> LoadJson<T>(string path) where T : class
    {
        if (!File.Exists(path)) return null;
        var json = await File.ReadAllTextAsync(path);
        return JsonSerializer.Deserialize<T>(json, JsonOptions);
    }
}
