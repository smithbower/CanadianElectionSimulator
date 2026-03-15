using System.Text.Json;
using System.Text.Json.Serialization;
using ElectionSim.Core.Models;
using ElectionSim.Core.Simulation;

namespace ElectionSim.DataTools.Commands;

/// <summary>
/// Runs hindcast validation tests against historical elections. Computes riding accuracy,
/// Brier score, log loss, CI coverage, and calibration analysis. Outputs results to
/// data/validation-results.json. See SIMULATION.md for methodology.
/// </summary>
public static class ValidateCommand
{
    private static readonly JsonSerializerOptions JsonReadOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private static readonly JsonSerializerOptions JsonWriteOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public static async Task RunAsync(string dataDir)
    {
        var processedDir = Path.Combine(dataDir, "processed");

        Console.WriteLine("=== Election Simulator Validation ===");
        Console.WriteLine();

        // 1. Load data
        var ridings = await LoadJson<List<Riding>>(Path.Combine(processedDir, "ridings.json"));
        var results2025 = await LoadJson<List<RidingResult>>(Path.Combine(processedDir, "results-2025.json"));
        var results2021 = await LoadJson<List<RidingResult>>(Path.Combine(processedDir, "results-2021.json"));
        var results2019 = await LoadJson<List<RidingResult>>(Path.Combine(processedDir, "results-2019.json"));
        var results2015 = await LoadJson<List<RidingResult>>(Path.Combine(processedDir, "results-2015.json"));
        var results2011 = await LoadJson<List<RidingResult>>(Path.Combine(processedDir, "results-2011.json"));
        var results2008 = await LoadJson<List<RidingResult>>(Path.Combine(processedDir, "results-2008.json"));
        var demographics = await LoadJson<List<RidingDemographics>>(Path.Combine(processedDir, "demographics.json"));

        if (ridings == null || results2025 == null || results2021 == null || results2015 == null)
        {
            Console.WriteLine("ERROR: Missing core data files (2015/2021/2025). Run 'process' first.");
            return;
        }

        var yearsLoaded = new List<string> { "2015", "2021", "2025" };
        if (results2019 != null) yearsLoaded.Add("2019");
        if (results2011 != null) yearsLoaded.Add("2011");
        if (results2008 != null) yearsLoaded.Add("2008");
        Console.WriteLine($"Loaded: {ridings.Count} ridings, results for {string.Join("/", yearsLoaded.OrderBy(y => y))}");
        Console.WriteLine();

        var parties = PartyColourProvider.MainParties;
        var validationResults = new ValidationResults();
        var defaultAlpha = new SimulationConfig().SwingBlendAlpha;

        // 2. Run hindcasts
        var hindcasts = new List<HindcastResult>();

        var hindcast2025 = RunHindcast("2025 from 2021", ridings, results2021, results2025, parties, defaultAlpha);
        hindcasts.Add(hindcast2025);

        if (results2019 != null)
        {
            var hindcast2021 = RunHindcast("2021 from 2019", ridings, results2019, results2021, parties, defaultAlpha);
            hindcasts.Add(hindcast2021);

            var hindcast2019 = RunHindcast("2019 from 2015", ridings, results2015, results2019, parties, defaultAlpha);
            hindcasts.Add(hindcast2019);
        }
        else
        {
            var hindcast2021 = RunHindcast("2021 from 2015", ridings, results2015, results2021, parties, defaultAlpha);
            hindcasts.Add(hindcast2021);
        }

        if (results2011 != null)
        {
            var hindcast2015 = RunHindcast("2015 from 2011", ridings, results2011, results2015, parties, defaultAlpha);
            hindcasts.Add(hindcast2015);
        }

        if (results2008 != null && results2011 != null)
        {
            var hindcast2011 = RunHindcast("2011 from 2008", ridings, results2008, results2011, parties, defaultAlpha);
            hindcasts.Add(hindcast2011);
        }

        validationResults.Hindcasts.AddRange(hindcasts);

        // 2b. Demographic prior hindcasts (if demographics data is available)
        if (demographics is { Count: > 0 })
        {
            Console.WriteLine();
            Console.WriteLine("=== Demographic Prior Hindcasts ===");
            Console.WriteLine();

            // Build election lookup for leave-one-out training
            var allElections = new Dictionary<string, List<RidingResult>>
            {
                ["2025"] = results2025,
                ["2021"] = results2021,
            };
            if (results2019 != null) allElections["2019"] = results2019;
            if (results2015 != null) allElections["2015"] = results2015;
            if (results2011 != null) allElections["2011"] = results2011;
            if (results2008 != null) allElections["2008"] = results2008;

            var demoHindcasts = new List<HindcastResult>();

            // 2025 from 2021 — train on all elections except 2025
            {
                var training = allElections.Where(kv => kv.Key != "2025")
                    .Select(kv => (IReadOnlyList<RidingResult>)kv.Value).ToList();
                demoHindcasts.Add(RunHindcast("2025 from 2021 (demo)", ridings, results2021, results2025, parties, defaultAlpha, demographics, training));
            }

            if (results2019 != null)
            {
                // 2021 from 2019 — train on all except 2021
                {
                    var training = allElections.Where(kv => kv.Key != "2021")
                        .Select(kv => (IReadOnlyList<RidingResult>)kv.Value).ToList();
                    demoHindcasts.Add(RunHindcast("2021 from 2019 (demo)", ridings, results2019, results2021, parties, defaultAlpha, demographics, training));
                }

                // 2019 from 2015 — train on all except 2019
                {
                    var training = allElections.Where(kv => kv.Key != "2019")
                        .Select(kv => (IReadOnlyList<RidingResult>)kv.Value).ToList();
                    demoHindcasts.Add(RunHindcast("2019 from 2015 (demo)", ridings, results2015!, results2019, parties, defaultAlpha, demographics, training));
                }
            }

            if (results2011 != null)
            {
                // 2015 from 2011 — train on all except 2015
                var training = allElections.Where(kv => kv.Key != "2015")
                    .Select(kv => (IReadOnlyList<RidingResult>)kv.Value).ToList();
                demoHindcasts.Add(RunHindcast("2015 from 2011 (demo)", ridings, results2011, results2015!, parties, defaultAlpha, demographics, training));
            }

            if (results2008 != null && results2011 != null)
            {
                // 2011 from 2008 — train on all except 2011
                var training = allElections.Where(kv => kv.Key != "2011")
                    .Select(kv => (IReadOnlyList<RidingResult>)kv.Value).ToList();
                demoHindcasts.Add(RunHindcast("2011 from 2008 (demo)", ridings, results2008, results2011, parties, defaultAlpha, demographics, training));
            }

            validationResults.Hindcasts.AddRange(demoHindcasts);

            // Print comparison table
            Console.WriteLine();
            Console.WriteLine("=== Demographic Prior Comparison ===");
            Console.WriteLine();
            Console.WriteLine("  Hindcast                  | Accuracy (base) | Accuracy (demo) | Brier (base) | Brier (demo)");
            Console.WriteLine("  --------------------------+-----------------+-----------------+--------------+-------------");
            foreach (var demoH in demoHindcasts)
            {
                var baseName = demoH.Name.Replace(" (demo)", "");
                var baseH = hindcasts.FirstOrDefault(h => h.Name == baseName);
                if (baseH != null)
                {
                    Console.WriteLine($"  {baseName,-27} | {baseH.RidingAccuracy * 100,14:F1}% | {demoH.RidingAccuracy * 100,14:F1}% | {baseH.BrierScore,12:F4} | {demoH.BrierScore,11:F4}");
                }
            }
            Console.WriteLine();
        }

        // 3. Calibration analysis (combined across all hindcasts)
        var calibration = ComputeCalibration(hindcasts, parties);
        validationResults.Calibration = calibration;

        // 4. Empirical sigma analysis
        var allTransitions = BuildTransitions(results2008, results2011, results2015!, results2019, results2021, results2025);
        var sigmaAnalysis = ComputeEmpiricalSigmas(ridings, allTransitions, parties, defaultAlpha);
        validationResults.EmpiricalSigmas = sigmaAnalysis;

        // 4b. Correlation matrix estimation
        ComputeCorrelationAnalysis(ridings, allTransitions, parties, defaultAlpha);

        // 5. Swing model comparison
        var swingComparison = ComputeSwingModelComparison(ridings, allTransitions, parties);
        validationResults.SwingModelComparison = swingComparison;

        // 6. Alpha sweep
        var alphaSweepResult = RunAlphaSweep(ridings, allTransitions, parties);
        validationResults.AlphaSweep = alphaSweepResult;

        // 7. Sigma sweep (using current default alpha)
        var sweepResult = RunSigmaSweep(ridings, results2021, results2025, parties, defaultAlpha);
        validationResults.SigmaSweep = sweepResult;

        // 8. Degrees of freedom sweep
        var dfSweep = RunDfSweep(ridings, allTransitions, parties, defaultAlpha);
        validationResults.DfSweep = dfSweep;

        // 9. Demographic blend weight sweep (if demographics available)
        if (demographics is { Count: > 0 })
        {
            var blendSweepResult = RunBlendWeightSweep(ridings, allTransitions, parties, demographics, defaultAlpha);
            validationResults.BlendWeightSweep = blendSweepResult;
        }

        // 10. Write JSON output
        var outputPath = Path.Combine(dataDir, "validation-results.json");
        await File.WriteAllTextAsync(outputPath, JsonSerializer.Serialize(validationResults, JsonWriteOptions));
        Console.WriteLine();
        Console.WriteLine($"Detailed results written to: {outputPath}");
    }

    // --- Hindcast ---

    private static HindcastResult RunHindcast(
        string name,
        IReadOnlyList<Riding> ridings,
        List<RidingResult> baselineResults,
        List<RidingResult> actualResults,
        IReadOnlyList<Party> parties,
        double alpha = 1.0,
        List<RidingDemographics>? demographics = null,
        IReadOnlyList<IReadOnlyList<RidingResult>>? trainingElections = null,
        double demographicBlendWeight = 0.15)
    {
        Console.WriteLine($"--- Hindcast: {name} ---");

        // Build "polls" from actual results' regional averages
        var polls = ComputeRegionalAverages(actualResults, ridings);

        // Compute swing ratios/deltas and project vote shares using blended swing
        var swingRatios = SwingCalculator.ComputeSwingRatios(polls, baselineResults, ridings);
        var additiveDeltas = SwingCalculator.ComputeAdditiveDeltas(polls, baselineResults, ridings);
        var projectedShares = SwingCalculator.ProjectRidingVoteSharesBlended(baselineResults, ridings, swingRatios, additiveDeltas, alpha, polls);

        // Optionally blend with demographic prior
        if (demographics != null && trainingElections is { Count: > 0 })
        {
            var demographicPrior = DemographicPrior.ComputePrior(ridings, demographics, trainingElections);
            projectedShares = SwingCalculator.BlendWithDemographicPrior(
                projectedShares, demographicPrior, ridings, baselineResults, demographicBlendWeight);
        }

        // Run simulation with fixed seed
        var config = new SimulationConfig(
            NumSimulations: 10_000,
            Seed: 42
        );
        var simulator = new MonteCarloSimulator(ridings, projectedShares);
        var summary = simulator.Run(config);

        // Build lookup for actual results
        var actualLookup = actualResults.ToDictionary(r => r.RidingId);

        // Compute metrics
        var result = new HindcastResult { Name = name };

        // Seat MAE
        var actualSeats = CountActualSeats(actualResults, parties);
        Console.WriteLine();
        Console.WriteLine("  Party   Actual  Predicted(Mean)  MAE");
        Console.WriteLine("  -----   ------  ---------------  ---");
        foreach (var party in parties)
        {
            int actual = actualSeats.GetValueOrDefault(party, 0);
            double predicted = summary.SeatDistributions[party].Mean;
            double mae = Math.Abs(predicted - actual);
            result.SeatMae[party] = mae;
            result.ActualSeats[party] = actual;
            result.PredictedMeanSeats[party] = Math.Round(predicted, 1);
            Console.WriteLine($"  {PartyColourProvider.GetShortName(party),-7} {actual,6}  {predicted,15:F1}  {mae,3:F1}");
        }

        // Riding winner accuracy
        int correctWinners = 0;
        int totalRidings = 0;
        double brierSum = 0;
        double logLossSum = 0;
        int ciHits = 0;
        int ciTotal = 0;

        // Per-region CI coverage tracking
        var regionCiHits = new Dictionary<Region, int>();
        var regionCiTotal = new Dictionary<Region, int>();
        foreach (var region in Enum.GetValues<Region>())
        {
            regionCiHits[region] = 0;
            regionCiTotal[region] = 0;
        }

        // Store per-riding data for calibration
        var ridingPredictions = new List<RidingPrediction>();

        foreach (var riding in ridings)
        {
            if (!actualLookup.TryGetValue(riding.Id, out var actualResult))
                continue;
            if (!summary.RidingWinProbabilities.TryGetValue(riding.Id, out var winProbs))
                continue;

            totalRidings++;

            // Actual winner
            var actualWinner = actualResult.Candidates.MaxBy(c => c.VoteShare)?.Party ?? Party.Other;

            // Predicted winner (highest win probability)
            var predictedWinner = winProbs.MaxBy(kv => kv.Value).Key;
            if (predictedWinner == actualWinner)
                correctWinners++;

            // Brier score (multi-class: sum of (predicted_prob - indicator)^2 for each party)
            double ridingBrier = 0;
            foreach (var party in parties)
            {
                double prob = winProbs.GetValueOrDefault(party, 0);
                double indicator = party == actualWinner ? 1.0 : 0.0;
                ridingBrier += (prob - indicator) * (prob - indicator);
            }
            brierSum += ridingBrier;

            // Log loss: -log(p_winner), floored at 0.001
            double winnerProb = Math.Max(winProbs.GetValueOrDefault(actualWinner, 0), 0.001);
            logLossSum += -Math.Log(winnerProb);

            // CI coverage: check if actual vote share falls within P5-P95
            if (summary.RidingVoteShareDistributions.TryGetValue(riding.Id, out var vsDists))
            {
                foreach (var party in parties)
                {
                    var candidate = actualResult.Candidates.FirstOrDefault(c => c.Party == party);
                    double actualShare = (candidate?.VoteShare ?? 0) * 100; // Convert to percentage
                    if (vsDists.TryGetValue(party, out var dist))
                    {
                        ciTotal++;
                        regionCiTotal[riding.Region]++;
                        if (actualShare >= dist.P5 && actualShare <= dist.P95)
                        {
                            ciHits++;
                            regionCiHits[riding.Region]++;
                        }
                    }
                }
            }

            // Store for calibration
            foreach (var party in parties)
            {
                double prob = winProbs.GetValueOrDefault(party, 0);
                bool won = party == actualWinner;
                ridingPredictions.Add(new RidingPrediction(riding.Id, party, prob, won));
            }
        }

        result.RidingAccuracy = totalRidings > 0 ? (double)correctWinners / totalRidings : 0;
        result.BrierScore = totalRidings > 0 ? brierSum / totalRidings : 0;
        result.LogLoss = totalRidings > 0 ? logLossSum / totalRidings : 0;
        result.CiCoverage = ciTotal > 0 ? (double)ciHits / ciTotal : 0;
        result.RidingPredictions = ridingPredictions;
        result.TotalRidings = totalRidings;
        result.CorrectWinners = correctWinners;
        result.CiHits = ciHits;
        result.CiTotal = ciTotal;

        // Per-region CI coverage
        foreach (var region in Enum.GetValues<Region>())
        {
            int rTotal = regionCiTotal[region];
            result.CiCoverageByRegion[region] = rTotal > 0 ? (double)regionCiHits[region] / rTotal : 0;
        }

        // Store projected shares for sigma analysis
        result.ProjectedShares = projectedShares;
        result.RidingOrder = ridings;

        Console.WriteLine();
        Console.WriteLine($"  Riding accuracy: {correctWinners}/{totalRidings} ({result.RidingAccuracy:P1})");
        Console.WriteLine($"  Brier score:     {result.BrierScore:F4} (lower is better; no-skill ~0.25)");
        Console.WriteLine($"  Log loss:        {result.LogLoss:F4} (lower is better)");
        Console.WriteLine($"  CI coverage:     {ciHits}/{ciTotal} ({result.CiCoverage:P1}) (should be ~90%)");
        Console.WriteLine();
        Console.WriteLine("  CI coverage by region:");
        Console.WriteLine("  Region               Hits/Total   Coverage");
        Console.WriteLine("  ------               ----------   --------");
        foreach (var region in Enum.GetValues<Region>())
        {
            int rHits = regionCiHits[region];
            int rTotal = regionCiTotal[region];
            if (rTotal == 0) continue;
            double rCov = (double)rHits / rTotal;
            string flag = rCov < 0.85 ? " *" : rCov > 0.95 ? " +" : "";
            Console.WriteLine($"  {PartyColourProvider.GetProvinceRegionName(region),-20} {rHits,4}/{rTotal,-4}   {rCov:P1}{flag}");
        }
        Console.WriteLine();

        return result;
    }

    // --- Calibration ---

    private static CalibrationResult ComputeCalibration(
        List<HindcastResult> hindcasts,
        IReadOnlyList<Party> parties)
    {
        Console.WriteLine("--- Calibration Analysis ---");

        // Combine all riding predictions from both hindcasts
        var allPredictions = hindcasts.SelectMany(h => h.RidingPredictions).ToList();

        int numBins = 10;
        var bins = new CalibrationBin[numBins];
        for (int i = 0; i < numBins; i++)
            bins[i] = new CalibrationBin { BinLow = i * 0.1, BinHigh = (i + 1) * 0.1 };

        foreach (var pred in allPredictions)
        {
            int binIdx = Math.Min((int)(pred.PredictedProb * numBins), numBins - 1);
            bins[binIdx].Count++;
            if (pred.ActuallyWon)
                bins[binIdx].Wins++;
        }

        Console.WriteLine("  Predicted     N   Actual%  (Expected ~midpoint)");
        Console.WriteLine("  ---------  ----   -------");
        foreach (var bin in bins)
        {
            double actualRate = bin.Count > 0 ? (double)bin.Wins / bin.Count : 0;
            bin.ActualRate = actualRate;
            double midpoint = (bin.BinLow + bin.BinHigh) / 2;
            string flag = bin.Count >= 10 && Math.Abs(actualRate - midpoint) > 0.15 ? " ***" : "";
            Console.WriteLine($"  {bin.BinLow:F1}-{bin.BinHigh:F1}   {bin.Count,5}   {actualRate,6:P1}{flag}");
        }
        Console.WriteLine();

        return new CalibrationResult { Bins = bins.ToList() };
    }

    // --- Transition builder ---

    private static List<(string Name, List<RidingResult> Baseline, List<RidingResult> Actual)> BuildTransitions(
        List<RidingResult>? results2008,
        List<RidingResult>? results2011,
        List<RidingResult> results2015,
        List<RidingResult>? results2019,
        List<RidingResult> results2021,
        List<RidingResult> results2025)
    {
        var transitions = new List<(string, List<RidingResult>, List<RidingResult>)>();
        if (results2008 != null && results2011 != null)
            transitions.Add(("2008->2011", results2008, results2011));
        if (results2011 != null)
            transitions.Add(("2011->2015", results2011, results2015));
        if (results2019 != null)
        {
            transitions.Add(("2015->2019", results2015, results2019));
            transitions.Add(("2019->2021", results2019, results2021));
        }
        else
        {
            transitions.Add(("2015->2021", results2015, results2021));
        }
        transitions.Add(("2021->2025", results2021, results2025));
        return transitions;
    }

    // --- Empirical Sigma Analysis ---

    private static EmpiricalSigmaResult ComputeEmpiricalSigmas(
        IReadOnlyList<Riding> ridings,
        List<(string Name, List<RidingResult> Baseline, List<RidingResult> Actual)> transitions,
        IReadOnlyList<Party> parties,
        double alpha = 1.0)
    {
        Console.WriteLine("--- Empirical Sigma Analysis ---");
        Console.WriteLine($"  (Comparing blended swing (alpha={alpha:F1}) predictions to actual results across {transitions.Count} transitions, without noise)");
        Console.WriteLine();

        var result = new EmpiricalSigmaResult();

        var allResiduals = new List<double>();
        var residualsByParty = new Dictionary<Party, List<double>>();
        var residualsByRegion = new Dictionary<Region, List<double>>();
        var residualsByPartyRegion = new Dictionary<(Party, Region), List<double>>();

        foreach (var party in parties)
            residualsByParty[party] = new List<double>();
        foreach (var region in Enum.GetValues<Region>())
            residualsByRegion[region] = new List<double>();

        foreach (var (transName, baseline, actual) in transitions)
        {
            // Build "polls" from actual regional averages
            var polls = ComputeRegionalAverages(actual, ridings);
            var swingRatios = SwingCalculator.ComputeSwingRatios(polls, baseline, ridings);
            var additiveDeltas = SwingCalculator.ComputeAdditiveDeltas(polls, baseline, ridings);
            var projected = SwingCalculator.ProjectRidingVoteSharesBlended(baseline, ridings, swingRatios, additiveDeltas, alpha, polls);

            var actualLookup = actual.ToDictionary(r => r.RidingId);

            Console.WriteLine($"  Transition: {transName}");

            for (int ri = 0; ri < ridings.Count; ri++)
            {
                var riding = ridings[ri];
                if (!actualLookup.TryGetValue(riding.Id, out var actualResult))
                    continue;

                for (int pi = 0; pi < parties.Count; pi++)
                {
                    var party = parties[pi];
                    double projectedShare = projected[ri, pi];
                    var candidate = actualResult.Candidates.FirstOrDefault(c => c.Party == party);
                    double actualShare = candidate?.VoteShare ?? 0;

                    double residual = actualShare - projectedShare;
                    allResiduals.Add(residual);
                    residualsByParty[party].Add(residual);
                    residualsByRegion[riding.Region].Add(residual);

                    var key = (party, riding.Region);
                    if (!residualsByPartyRegion.TryGetValue(key, out var prList))
                    {
                        prList = new List<double>();
                        residualsByPartyRegion[key] = prList;
                    }
                    prList.Add(residual);
                }
            }
        }

        // Compute overall std dev
        double overallStd = StdDev(allResiduals);
        result.OverallResidualStdDev = overallStd;
        Console.WriteLine($"  Overall residual std dev: {overallStd:F4} ({overallStd * 100:F2}%)");
        Console.WriteLine();

        // By party
        Console.WriteLine("  By Party:");
        Console.WriteLine("  Party    StdDev     Mean      N");
        Console.WriteLine("  -----    ------     ----      -");
        foreach (var party in parties)
        {
            var resids = residualsByParty[party];
            double std = StdDev(resids);
            double mean = resids.Average();
            result.ByParty[party] = new ResidualStats { StdDev = std, Mean = mean, N = resids.Count };
            Console.WriteLine($"  {PartyColourProvider.GetShortName(party),-7}  {std,7:F4}  {mean,7:F4}  {resids.Count,5}");
        }
        Console.WriteLine();

        // By region
        Console.WriteLine("  By Region:");
        Console.WriteLine("  Region               StdDev     Mean      N");
        Console.WriteLine("  ------               ------     ----      -");
        foreach (var region in Enum.GetValues<Region>())
        {
            var resids = residualsByRegion[region];
            if (resids.Count == 0) continue;
            double std = StdDev(resids);
            double mean = resids.Average();
            result.ByRegion[region] = new ResidualStats { StdDev = std, Mean = mean, N = resids.Count };
            Console.WriteLine($"  {PartyColourProvider.GetProvinceRegionName(region),-20} {std,7:F4}  {mean,7:F4}  {resids.Count,5}");
        }
        Console.WriteLine();

        // Current vs empirical sigmas
        var currentConfig = new SimulationConfig();
        double currentCombined = Math.Sqrt(
            currentConfig.NationalSigma * currentConfig.NationalSigma +
            currentConfig.RegionalSigma * currentConfig.RegionalSigma +
            currentConfig.RidingSigma * currentConfig.RidingSigma);

        Console.WriteLine("  Current model sigmas:");
        Console.WriteLine($"    National:  {currentConfig.NationalSigma:F4} ({currentConfig.NationalSigma * 100:F2}%)");
        Console.WriteLine($"    Regional:  {currentConfig.RegionalSigma:F4} ({currentConfig.RegionalSigma * 100:F2}%)");
        Console.WriteLine($"    Riding:    {currentConfig.RidingSigma:F4} ({currentConfig.RidingSigma * 100:F2}%)");
        Console.WriteLine($"    Combined:  {currentCombined:F4} ({currentCombined * 100:F2}%)");
        Console.WriteLine($"    Empirical: {overallStd:F4} ({overallStd * 100:F2}%)");
        Console.WriteLine($"    Ratio (empirical/current): {overallStd / currentCombined:F2}x");
        Console.WriteLine();

        // Decompose variance: estimate national, regional, riding components
        // National = std dev of mean residual across all ridings (one value per simulation/transition)
        // We approximate by looking at structure
        var (estNational, estRegional, estRiding) = DecomposeVariance(
            residualsByParty, residualsByRegion, residualsByPartyRegion, parties, allResiduals);

        result.RecommendedNationalSigma = estNational;
        result.RecommendedRegionalSigma = estRegional;
        result.RecommendedRidingSigma = estRiding;

        Console.WriteLine("  Recommended sigmas (from variance decomposition):");
        Console.WriteLine($"    National:  {estNational:F4} ({estNational * 100:F2}%)");
        Console.WriteLine($"    Regional:  {estRegional:F4} ({estRegional * 100:F2}%)");
        Console.WriteLine($"    Riding:    {estRiding:F4} ({estRiding * 100:F2}%)");
        Console.WriteLine();

        return result;
    }

    // --- Correlation Matrix Analysis ---

    private static void ComputeCorrelationAnalysis(
        IReadOnlyList<Riding> ridings,
        List<(string Name, List<RidingResult> Baseline, List<RidingResult> Actual)> transitions,
        IReadOnlyList<Party> parties,
        double alpha)
    {
        Console.WriteLine("--- Correlation Matrix Analysis ---");
        Console.WriteLine($"  Computing inter-party correlation from residuals across {transitions.Count} transitions");
        Console.WriteLine();

        int numParties = parties.Count;

        // Collect residual vectors: one per (riding, transition) pair
        var residualVectors = new List<double[]>();

        foreach (var (transName, baseline, actual) in transitions)
        {
            var polls = ComputeRegionalAverages(actual, ridings);
            var swingRatios = SwingCalculator.ComputeSwingRatios(polls, baseline, ridings);
            var additiveDeltas = SwingCalculator.ComputeAdditiveDeltas(polls, baseline, ridings);
            var projected = SwingCalculator.ProjectRidingVoteSharesBlended(baseline, ridings, swingRatios, additiveDeltas, alpha, polls);

            var actualLookup = actual.ToDictionary(r => r.RidingId);

            for (int ri = 0; ri < ridings.Count; ri++)
            {
                if (!actualLookup.TryGetValue(ridings[ri].Id, out var actualResult))
                    continue;

                var residuals = new double[numParties];
                for (int pi = 0; pi < numParties; pi++)
                {
                    var candidate = actualResult.Candidates.FirstOrDefault(c => c.Party == parties[pi]);
                    double actualShare = candidate?.VoteShare ?? 0;
                    residuals[pi] = actualShare - projected[ri, pi];
                }

                // Demean residuals within each riding to remove the common-mode component.
                // This isolates competitive dynamics (when one party gains, others lose)
                // from common factors (Other share, baseline quality) that create
                // spurious positive correlations. The common-mode noise is already
                // handled by sigma scaling and renormalization.
                double residualMean = 0;
                for (int pi = 0; pi < numParties; pi++)
                    residualMean += residuals[pi];
                residualMean /= numParties;
                for (int pi = 0; pi < numParties; pi++)
                    residuals[pi] -= residualMean;

                residualVectors.Add(residuals);
            }
        }

        int n = residualVectors.Count;
        Console.WriteLine($"  Residual vectors: {n}");

        // Compute means
        var means = new double[numParties];
        foreach (var v in residualVectors)
            for (int p = 0; p < numParties; p++)
                means[p] += v[p];
        for (int p = 0; p < numParties; p++)
            means[p] /= n;

        // Compute covariance matrix
        var cov = new double[numParties, numParties];
        foreach (var v in residualVectors)
        {
            for (int i = 0; i < numParties; i++)
                for (int j = 0; j < numParties; j++)
                    cov[i, j] += (v[i] - means[i]) * (v[j] - means[j]);
        }
        for (int i = 0; i < numParties; i++)
            for (int j = 0; j < numParties; j++)
                cov[i, j] /= (n - 1);

        // Compute correlation matrix
        var corr = new double[numParties, numParties];
        for (int i = 0; i < numParties; i++)
            for (int j = 0; j < numParties; j++)
                corr[i, j] = cov[i, j] / (Math.Sqrt(cov[i, i]) * Math.Sqrt(cov[j, j]));

        // Print correlation matrix
        Console.Write("                  ");
        for (int j = 0; j < numParties; j++)
            Console.Write($"  {PartyColourProvider.GetShortName(parties[j]),7}");
        Console.WriteLine();

        for (int i = 0; i < numParties; i++)
        {
            Console.Write($"  {PartyColourProvider.GetShortName(parties[i]),7}      ");
            for (int j = 0; j < numParties; j++)
                Console.Write($"  {corr[i, j],7:F3}");
            Console.WriteLine();
        }
        Console.WriteLine();

        // Add regularization to ensure positive-definiteness.
        // Demeaned residuals for 6 parties create a rank-5 covariance matrix;
        // regularization restores full rank so Cholesky succeeds.
        double epsilon = 0.01;
        for (int i = 0; i < numParties; i++)
            corr[i, i] += epsilon;

        // Cholesky decomposition: L such that regularized_corr = L * L^T
        var L = CholeskyDecompose(corr, numParties);

        // Print Cholesky factor
        Console.WriteLine("  Cholesky Factor (L, lower-triangular):");
        Console.Write("                  ");
        for (int j = 0; j < numParties; j++)
            Console.Write($"  {PartyColourProvider.GetShortName(parties[j]),7}");
        Console.WriteLine();

        for (int i = 0; i < numParties; i++)
        {
            Console.Write($"  {PartyColourProvider.GetShortName(parties[i]),7}      ");
            for (int j = 0; j < numParties; j++)
            {
                if (j <= i)
                    Console.Write($"  {L[i, j],7:F4}");
                else
                    Console.Write($"  {"",7}");
            }
            Console.WriteLine();
        }
        Console.WriteLine();

        // Print as C# code for CorrelationData.cs
        Console.WriteLine("  C# code for CorrelationData.cs:");
        Console.WriteLine("  // Party order: LPC, CPC, NDP, BQ, GPC, PPC");
        Console.WriteLine("  public static readonly double[,] CholeskyFactor = new double[6, 6]");
        Console.WriteLine("  {");
        for (int i = 0; i < numParties; i++)
        {
            Console.Write("      { ");
            for (int j = 0; j < numParties; j++)
            {
                Console.Write($"{L[i, j]:F6}");
                if (j < numParties - 1) Console.Write(", ");
            }
            Console.Write(" }");
            if (i < numParties - 1) Console.Write(",");
            Console.Write($" // {PartyColourProvider.GetShortName(parties[i])}");
            Console.WriteLine();
        }
        Console.WriteLine("  };");
        Console.WriteLine();

        // Verify: L * L^T should equal corr
        Console.WriteLine("  Verification (L * L^T - Corr, should be ~0):");
        double maxErr = 0;
        for (int i = 0; i < numParties; i++)
            for (int j = 0; j < numParties; j++)
            {
                double val = 0;
                for (int k = 0; k < numParties; k++)
                    val += L[i, k] * L[j, k];
                double err = Math.Abs(val - corr[i, j]);
                if (err > maxErr) maxErr = err;
            }
        Console.WriteLine($"  Max absolute error: {maxErr:E3}");
        Console.WriteLine();
    }

    private static double[,] CholeskyDecompose(double[,] matrix, int n)
    {
        var L = new double[n, n];
        for (int i = 0; i < n; i++)
        {
            for (int j = 0; j <= i; j++)
            {
                double sum = 0;
                for (int k = 0; k < j; k++)
                    sum += L[i, k] * L[j, k];

                if (i == j)
                    L[i, j] = Math.Sqrt(Math.Max(matrix[i, i] - sum, 0));
                else
                    L[i, j] = L[j, j] > 0 ? (matrix[i, j] - sum) / L[j, j] : 0;
            }
        }
        return L;
    }

    // --- Swing Model Comparison ---

    private static SwingModelComparisonResult ComputeSwingModelComparison(
        IReadOnlyList<Riding> ridings,
        List<(string Name, List<RidingResult> Baseline, List<RidingResult> Actual)> transitions,
        IReadOnlyList<Party> parties)
    {
        Console.WriteLine("--- Swing Model Comparison ---");
        Console.WriteLine($"  Proportional swing (current) vs Additive swing across {transitions.Count} transitions");
        Console.WriteLine();

        var result = new SwingModelComparisonResult();

        double propSseTotal = 0, addSseTotal = 0;
        int totalN = 0;
        var propSseByParty = new Dictionary<Party, double>();
        var addSseByParty = new Dictionary<Party, double>();
        var countByParty = new Dictionary<Party, int>();

        foreach (var party in parties)
        {
            propSseByParty[party] = 0;
            addSseByParty[party] = 0;
            countByParty[party] = 0;
        }

        foreach (var (transName, baseline, actual) in transitions)
        {
            var polls = ComputeRegionalAverages(actual, ridings);
            var baselineLookup = baseline.ToDictionary(r => r.RidingId);
            var actualLookup = actual.ToDictionary(r => r.RidingId);

            // Proportional swing ratios (current model)
            var swingRatios = SwingCalculator.ComputeSwingRatios(polls, baseline, ridings);

            // Also compute additive deltas for comparison
            var additiveDeltas = SwingCalculator.ComputeAdditiveDeltas(polls, baseline, ridings);

            // Proportional projection (alpha=1.0 is pure proportional)
            var propProjected = SwingCalculator.ProjectRidingVoteSharesBlended(baseline, ridings, swingRatios, additiveDeltas, 1.0, polls);

            for (int ri = 0; ri < ridings.Count; ri++)
            {
                var riding = ridings[ri];
                if (!actualLookup.TryGetValue(riding.Id, out var actualResult))
                    continue;
                if (!baselineLookup.TryGetValue(riding.Id, out var baselineResult))
                    continue;

                for (int pi = 0; pi < parties.Count; pi++)
                {
                    var party = parties[pi];
                    var actualCandidate = actualResult.Candidates.FirstOrDefault(c => c.Party == party);
                    double actualShare = actualCandidate?.VoteShare ?? 0;

                    // Proportional
                    double propShare = propProjected[ri, pi];
                    double propErr = actualShare - propShare;
                    propSseTotal += propErr * propErr;
                    propSseByParty[party] += propErr * propErr;

                    // Additive
                    var baseCandidate = baselineResult.Candidates.FirstOrDefault(c => c.Party == party);
                    double baseShare = baseCandidate?.VoteShare ?? 0;
                    double addDelta = additiveDeltas.GetValueOrDefault(riding.Region)?.GetValueOrDefault(party, 0) ?? 0;
                    double addShare = Math.Max(baseShare + addDelta, 0);
                    double addErr = actualShare - addShare;
                    addSseTotal += addErr * addErr;
                    addSseByParty[party] += addErr * addErr;

                    countByParty[party]++;
                    totalN++;
                }
            }
        }

        double propRmse = Math.Sqrt(propSseTotal / totalN);
        double addRmse = Math.Sqrt(addSseTotal / totalN);

        result.ProportionalRmse = propRmse;
        result.AdditiveRmse = addRmse;

        Console.WriteLine($"  Overall RMSE:");
        Console.WriteLine($"    Proportional: {propRmse:F4} ({propRmse * 100:F2}%)");
        Console.WriteLine($"    Additive:     {addRmse:F4} ({addRmse * 100:F2}%)");
        Console.WriteLine($"    Better:       {(propRmse <= addRmse ? "Proportional" : "Additive")}");
        Console.WriteLine();

        Console.WriteLine("  RMSE by Party:");
        Console.WriteLine("  Party    Proportional  Additive    Better");
        Console.WriteLine("  -----    ------------  --------    ------");
        foreach (var party in parties)
        {
            int n = countByParty[party];
            if (n == 0) continue;
            double pRmse = Math.Sqrt(propSseByParty[party] / n);
            double aRmse = Math.Sqrt(addSseByParty[party] / n);
            string better = pRmse <= aRmse ? "Prop" : "Add";
            result.ByParty[party] = new SwingComparisonByParty
            {
                ProportionalRmse = pRmse,
                AdditiveRmse = aRmse
            };
            Console.WriteLine($"  {PartyColourProvider.GetShortName(party),-7}    {pRmse,10:F4}    {aRmse,8:F4}    {better}");
        }
        Console.WriteLine();

        // Residual distribution check (kurtosis)
        Console.WriteLine("  Residual distribution (proportional swing):");
        var allPropResiduals = new List<double>();
        foreach (var (transName, baseline, actual) in transitions)
        {
            var polls = ComputeRegionalAverages(actual, ridings);
            var swingRatios = SwingCalculator.ComputeSwingRatios(polls, baseline, ridings);
            var additiveDeltas2 = SwingCalculator.ComputeAdditiveDeltas(polls, baseline, ridings);
            var propProjected = SwingCalculator.ProjectRidingVoteSharesBlended(baseline, ridings, swingRatios, additiveDeltas2, 1.0, polls);
            var actualLookup = actual.ToDictionary(r => r.RidingId);

            for (int ri = 0; ri < ridings.Count; ri++)
            {
                if (!actualLookup.TryGetValue(ridings[ri].Id, out var actualResult))
                    continue;
                for (int pi = 0; pi < parties.Count; pi++)
                {
                    var candidate = actualResult.Candidates.FirstOrDefault(c => c.Party == parties[pi]);
                    double actualVal = candidate?.VoteShare ?? 0;
                    double propVal = propProjected[ri, pi];
                    allPropResiduals.Add(actualVal - propVal);
                }
            }
        }

        double kurtosis = ExcessKurtosis(allPropResiduals);
        double skewness = Skewness(allPropResiduals);
        result.ResidualKurtosis = kurtosis;
        result.ResidualSkewness = skewness;
        Console.WriteLine($"    Skewness: {skewness:F3} (0 = symmetric)");
        Console.WriteLine($"    Excess kurtosis: {kurtosis:F3} (0 = Gaussian, >0 = heavy tails)");
        Console.WriteLine();

        return result;
    }

    // --- Helper Methods ---

    private static List<RegionalPoll> ComputeRegionalAverages(
        List<RidingResult> results,
        IReadOnlyList<Riding> ridings)
    {
        var ridingLookup = ridings.ToDictionary(r => r.Id);
        var regionVotes = new Dictionary<Region, Dictionary<Party, int>>();
        var regionTotals = new Dictionary<Region, int>();

        foreach (var result in results)
        {
            if (!ridingLookup.TryGetValue(result.RidingId, out var riding))
                continue;

            var region = riding.Region;
            if (!regionVotes.TryGetValue(region, out var partyVotes))
            {
                partyVotes = new Dictionary<Party, int>();
                regionVotes[region] = partyVotes;
                regionTotals[region] = 0;
            }

            foreach (var c in result.Candidates)
            {
                partyVotes.TryGetValue(c.Party, out int existing);
                partyVotes[c.Party] = existing + c.Votes;
            }
            regionTotals[region] += result.TotalVotes;
        }

        var polls = new List<RegionalPoll>();
        foreach (var (region, partyVotes) in regionVotes.OrderBy(kv => kv.Key))
        {
            int total = regionTotals[region];
            var shares = new Dictionary<Party, double>();
            foreach (var (party, votes) in partyVotes)
                shares[party] = total > 0 ? (double)votes / total : 0;
            polls.Add(new RegionalPoll(region, shares));
        }
        return polls;
    }

    private static Dictionary<Party, int> CountActualSeats(
        List<RidingResult> results,
        IReadOnlyList<Party> parties)
    {
        var seats = new Dictionary<Party, int>();
        foreach (var party in parties)
            seats[party] = 0;

        foreach (var result in results)
        {
            var winner = result.Candidates.MaxBy(c => c.VoteShare);
            if (winner != null)
            {
                seats.TryGetValue(winner.Party, out int current);
                seats[winner.Party] = current + 1;
            }
        }
        return seats;
    }

    private static (double national, double regional, double riding) DecomposeVariance(
        Dictionary<Party, List<double>> residualsByParty,
        Dictionary<Region, List<double>> residualsByRegion,
        Dictionary<(Party, Region), List<double>> residualsByPartyRegion,
        IReadOnlyList<Party> parties,
        List<double> allResiduals)
    {
        // Variance decomposition approach:
        // Total variance = national + regional + riding
        //
        // National component: variance of party-level mean residuals
        // Regional component: variance of region-level mean residuals (after removing party means)
        // Riding component: remaining variance

        double totalVar = Variance(allResiduals);

        // National: variance explained by party-level means
        double nationalVar = 0;
        foreach (var party in parties)
        {
            var resids = residualsByParty[party];
            if (resids.Count > 0)
            {
                double mean = resids.Average();
                nationalVar += mean * mean * resids.Count;
            }
        }
        nationalVar /= allResiduals.Count;

        // Regional: variance explained by party-region means beyond party means
        double regionalVar = 0;
        foreach (var (key, resids) in residualsByPartyRegion)
        {
            if (resids.Count < 2) continue;
            double prMean = resids.Average();
            double partyMean = residualsByParty[key.Item1].Average();
            double diff = prMean - partyMean;
            regionalVar += diff * diff * resids.Count;
        }
        regionalVar /= allResiduals.Count;

        // Riding: remaining
        double ridingVar = Math.Max(totalVar - nationalVar - regionalVar, 0);

        return (Math.Sqrt(nationalVar), Math.Sqrt(regionalVar), Math.Sqrt(ridingVar));
    }

    private static double StdDev(List<double> values)
    {
        if (values.Count < 2) return 0;
        double mean = values.Average();
        double sumSq = values.Sum(v => (v - mean) * (v - mean));
        return Math.Sqrt(sumSq / (values.Count - 1));
    }

    private static double Variance(List<double> values)
    {
        if (values.Count < 2) return 0;
        double mean = values.Average();
        return values.Sum(v => (v - mean) * (v - mean)) / (values.Count - 1);
    }

    private static double ExcessKurtosis(List<double> values)
    {
        if (values.Count < 4) return 0;
        double mean = values.Average();
        double n = values.Count;
        double m2 = values.Sum(v => Math.Pow(v - mean, 2)) / n;
        double m4 = values.Sum(v => Math.Pow(v - mean, 4)) / n;
        if (m2 == 0) return 0;
        return (m4 / (m2 * m2)) - 3.0;
    }

    private static double Skewness(List<double> values)
    {
        if (values.Count < 3) return 0;
        double mean = values.Average();
        double n = values.Count;
        double m2 = values.Sum(v => Math.Pow(v - mean, 2)) / n;
        double m3 = values.Sum(v => Math.Pow(v - mean, 3)) / n;
        if (m2 == 0) return 0;
        return m3 / Math.Pow(m2, 1.5);
    }

    private static async Task<T?> LoadJson<T>(string path) where T : class
    {
        if (!File.Exists(path))
        {
            Console.WriteLine($"  WARNING: File not found: {path}");
            return null;
        }
        var json = await File.ReadAllTextAsync(path);
        return JsonSerializer.Deserialize<T>(json, JsonReadOptions);
    }

    // --- Result models ---

    private record RidingPrediction(int RidingId, Party Party, double PredictedProb, bool ActuallyWon);

    private class ValidationResults
    {
        public List<HindcastResult> Hindcasts { get; set; } = [];
        public CalibrationResult? Calibration { get; set; }
        public EmpiricalSigmaResult? EmpiricalSigmas { get; set; }
        public SwingModelComparisonResult? SwingModelComparison { get; set; }
        public AlphaSweepResult? AlphaSweep { get; set; }
        public SigmaSweepResult? SigmaSweep { get; set; }
        public DfSweepResult? DfSweep { get; set; }
        public BlendWeightSweepResult? BlendWeightSweep { get; set; }
    }

    private class HindcastResult
    {
        public string Name { get; set; } = "";
        public Dictionary<Party, double> SeatMae { get; set; } = new();
        public Dictionary<Party, int> ActualSeats { get; set; } = new();
        public Dictionary<Party, double> PredictedMeanSeats { get; set; } = new();
        public double RidingAccuracy { get; set; }
        public double BrierScore { get; set; }
        public double LogLoss { get; set; }
        public double CiCoverage { get; set; }
        public Dictionary<Region, double> CiCoverageByRegion { get; set; } = new();
        public int TotalRidings { get; set; }
        public int CorrectWinners { get; set; }
        public int CiHits { get; set; }
        public int CiTotal { get; set; }

        [JsonIgnore]
        public List<RidingPrediction> RidingPredictions { get; set; } = [];
        [JsonIgnore]
        public double[,]? ProjectedShares { get; set; }
        [JsonIgnore]
        public IReadOnlyList<Riding>? RidingOrder { get; set; }
    }

    private class CalibrationResult
    {
        public List<CalibrationBin> Bins { get; set; } = [];
    }

    private class CalibrationBin
    {
        public double BinLow { get; set; }
        public double BinHigh { get; set; }
        public int Count { get; set; }
        public int Wins { get; set; }
        public double ActualRate { get; set; }
    }

    private class EmpiricalSigmaResult
    {
        public double OverallResidualStdDev { get; set; }
        public Dictionary<Party, ResidualStats> ByParty { get; set; } = new();
        public Dictionary<Region, ResidualStats> ByRegion { get; set; } = new();
        public double RecommendedNationalSigma { get; set; }
        public double RecommendedRegionalSigma { get; set; }
        public double RecommendedRidingSigma { get; set; }
    }

    private class ResidualStats
    {
        public double StdDev { get; set; }
        public double Mean { get; set; }
        public int N { get; set; }
    }

    private class SwingModelComparisonResult
    {
        public double ProportionalRmse { get; set; }
        public double AdditiveRmse { get; set; }
        public Dictionary<Party, SwingComparisonByParty> ByParty { get; set; } = new();
        public double ResidualKurtosis { get; set; }
        public double ResidualSkewness { get; set; }
    }

    private class SwingComparisonByParty
    {
        public double ProportionalRmse { get; set; }
        public double AdditiveRmse { get; set; }
    }

    // --- Alpha Sweep ---

    private static AlphaSweepResult RunAlphaSweep(
        IReadOnlyList<Riding> ridings,
        List<(string Name, List<RidingResult> Baseline, List<RidingResult> Actual)> transitions,
        IReadOnlyList<Party> parties)
    {
        Console.WriteLine("--- Alpha Sweep ---");
        Console.WriteLine($"  Sweeping SwingBlendAlpha [0.0-1.0] across {transitions.Count} transitions (1000 sims, seed=42)");
        Console.WriteLine();

        var result = new AlphaSweepResult();
        double bestAvgBrier = double.MaxValue;
        AlphaSweepEntry? bestEntry = null;

        // Header
        Console.Write($"  {"Alpha",6}");
        foreach (var (transName, _, _) in transitions)
            Console.Write($"  {transName,12}");
        Console.Write($"  {"AvgBrier",10}  {"Accuracy",10}  {"CI Cov",10}  {"ProjRMSE",10}");
        Console.WriteLine();
        Console.Write($"  {"-----",6}");
        foreach (var _ in transitions)
            Console.Write($"  {"------------",12}");
        Console.Write($"  {"----------",10}  {"----------",10}  {"----------",10}  {"----------",10}");
        Console.WriteLine();

        double[] alphaValues = [0.0, 0.1, 0.2, 0.3, 0.4, 0.5, 0.6, 0.7, 0.8, 0.9, 1.0];

        foreach (var alpha in alphaValues)
        {
            var entry = new AlphaSweepEntry { Alpha = alpha };
            double brierSum = 0;
            int brierCount = 0;
            int totalCorrect = 0;
            int totalRidings = 0;
            int totalCiHits = 0;
            int totalCiTotal = 0;
            double projSseTotal = 0;
            int projN = 0;

            Console.Write($"  {alpha,6:F1}");

            foreach (var (transName, baseline, actual) in transitions)
            {
                var polls = ComputeRegionalAverages(actual, ridings);
                var swingRatios = SwingCalculator.ComputeSwingRatios(polls, baseline, ridings);
                var additiveDeltas = SwingCalculator.ComputeAdditiveDeltas(polls, baseline, ridings);
                var projectedShares = SwingCalculator.ProjectRidingVoteSharesBlended(baseline, ridings, swingRatios, additiveDeltas, alpha, polls);

                var actualLookup = actual.ToDictionary(r => r.RidingId);

                // Compute projection RMSE (no noise)
                for (int ri = 0; ri < ridings.Count; ri++)
                {
                    var riding = ridings[ri];
                    if (!actualLookup.TryGetValue(riding.Id, out var actualResult))
                        continue;
                    for (int pi = 0; pi < parties.Count; pi++)
                    {
                        var candidate = actualResult.Candidates.FirstOrDefault(c => c.Party == parties[pi]);
                        double actualShare = candidate?.VoteShare ?? 0;
                        double projShare = projectedShares[ri, pi];
                        double err = actualShare - projShare;
                        projSseTotal += err * err;
                        projN++;
                    }
                }

                // Run simulation
                var config = new SimulationConfig(NumSimulations: 1_000, Seed: 42);
                var simulator = new MonteCarloSimulator(ridings, projectedShares);
                var summary = simulator.Run(config);

                // Compute metrics
                int correct = 0;
                int total = 0;
                double transBrierSum = 0;
                int ciHits = 0;
                int ciTotal = 0;

                foreach (var riding in ridings)
                {
                    if (!actualLookup.TryGetValue(riding.Id, out var actualResult))
                        continue;
                    if (!summary.RidingWinProbabilities.TryGetValue(riding.Id, out var winProbs))
                        continue;

                    total++;
                    var actualWinner = actualResult.Candidates.MaxBy(c => c.VoteShare)?.Party ?? Party.Other;
                    var predictedWinner = winProbs.MaxBy(kv => kv.Value).Key;
                    if (predictedWinner == actualWinner) correct++;

                    double ridingBrier = 0;
                    foreach (var party in parties)
                    {
                        double prob = winProbs.GetValueOrDefault(party, 0);
                        double indicator = party == actualWinner ? 1.0 : 0.0;
                        ridingBrier += (prob - indicator) * (prob - indicator);
                    }
                    transBrierSum += ridingBrier;

                    if (summary.RidingVoteShareDistributions.TryGetValue(riding.Id, out var vsDists))
                    {
                        foreach (var party in parties)
                        {
                            var candidate = actualResult.Candidates.FirstOrDefault(c => c.Party == party);
                            double actualShare = (candidate?.VoteShare ?? 0) * 100;
                            if (vsDists.TryGetValue(party, out var dist))
                            {
                                ciTotal++;
                                if (actualShare >= dist.P5 && actualShare <= dist.P95)
                                    ciHits++;
                            }
                        }
                    }
                }

                double transBrier = total > 0 ? transBrierSum / total : 0;
                entry.BrierByTransition[transName] = Math.Round(transBrier, 4);
                brierSum += transBrier;
                brierCount++;
                totalCorrect += correct;
                totalRidings += total;
                totalCiHits += ciHits;
                totalCiTotal += ciTotal;

                Console.Write($"  {transBrier,12:F4}");
            }

            entry.AverageBrier = Math.Round(brierSum / Math.Max(brierCount, 1), 4);
            entry.Accuracy = Math.Round(totalRidings > 0 ? (double)totalCorrect / totalRidings : 0, 3);
            entry.CiCoverage = Math.Round(totalCiTotal > 0 ? (double)totalCiHits / totalCiTotal : 0, 3);
            entry.ProjectionRmse = Math.Round(projN > 0 ? Math.Sqrt(projSseTotal / projN) : 0, 4);

            Console.Write($"  {entry.AverageBrier,10:F4}  {entry.Accuracy,10:P1}  {entry.CiCoverage,10:P1}  {entry.ProjectionRmse,10:F4}");
            Console.WriteLine();

            result.Entries.Add(entry);

            if (entry.AverageBrier < bestAvgBrier)
            {
                bestAvgBrier = entry.AverageBrier;
                bestEntry = entry;
            }
        }

        Console.WriteLine();
        if (bestEntry != null)
        {
            result.BestAlpha = bestEntry.Alpha;
            result.BestAverageBrier = bestEntry.AverageBrier;
            Console.WriteLine($"  Best alpha: {bestEntry.Alpha:F1}");
            Console.WriteLine($"    Avg Brier: {bestEntry.AverageBrier:F4}, Accuracy: {bestEntry.Accuracy:P1}, CI coverage: {bestEntry.CiCoverage:P1}, Proj RMSE: {bestEntry.ProjectionRmse:F4}");
        }
        Console.WriteLine();

        return result;
    }

    // --- Sigma Sweep ---

    private static SigmaSweepResult RunSigmaSweep(
        IReadOnlyList<Riding> ridings,
        List<RidingResult> baselineResults,
        List<RidingResult> actualResults,
        IReadOnlyList<Party> parties,
        double alpha = 1.0)
    {
        Console.WriteLine("--- Sigma Sweep ---");
        Console.WriteLine($"  Sweeping national and riding sigmas (regional fixed at 0.026, df=3, 1000 sims, alpha={alpha:F1})");
        Console.WriteLine();

        var polls = ComputeRegionalAverages(actualResults, ridings);
        var swingRatios = SwingCalculator.ComputeSwingRatios(polls, baselineResults, ridings);
        var additiveDeltas = SwingCalculator.ComputeAdditiveDeltas(polls, baselineResults, ridings);
        var projectedShares = SwingCalculator.ProjectRidingVoteSharesBlended(baselineResults, ridings, swingRatios, additiveDeltas, alpha, polls);
        var actualLookup = actualResults.ToDictionary(r => r.RidingId);

        double[] nationalValues = [0.03, 0.04, 0.05, 0.06, 0.07, 0.08];
        double[] ridingValues = [0.02, 0.03, 0.04, 0.05, 0.06, 0.07];
        double regionalSigma = 0.026;

        var result = new SigmaSweepResult();
        double bestBrier = double.MaxValue;
        SigmaSweepEntry? bestEntry = null;

        // Print header
        Console.Write("  Nat\\Rid ");
        foreach (var rv in ridingValues)
            Console.Write($" {rv * 100,5:F0}%  ");
        Console.WriteLine();
        Console.Write("  --------");
        foreach (var _ in ridingValues)
            Console.Write("--------");
        Console.WriteLine();

        foreach (var ns in nationalValues)
        {
            Console.Write($"  {ns * 100,4:F0}%    ");
            foreach (var rs in ridingValues)
            {
                var config = new SimulationConfig(
                    NumSimulations: 1_000,
                    NationalSigma: ns,
                    RegionalSigma: regionalSigma,
                    RidingSigma: rs,
                    Seed: 42,
                    DegreesOfFreedom: 3.0
                );

                var simulator = new MonteCarloSimulator(ridings, projectedShares);
                var summary = simulator.Run(config);

                // Compute Brier score, CI coverage, riding accuracy
                int correctWinners = 0;
                int totalRidings = 0;
                double brierSum = 0;
                int ciHits = 0;
                int ciTotal = 0;

                foreach (var riding in ridings)
                {
                    if (!actualLookup.TryGetValue(riding.Id, out var actualResult))
                        continue;
                    if (!summary.RidingWinProbabilities.TryGetValue(riding.Id, out var winProbs))
                        continue;

                    totalRidings++;
                    var actualWinner = actualResult.Candidates.MaxBy(c => c.VoteShare)?.Party ?? Party.Other;
                    var predictedWinner = winProbs.MaxBy(kv => kv.Value).Key;
                    if (predictedWinner == actualWinner) correctWinners++;

                    double ridingBrier = 0;
                    foreach (var party in parties)
                    {
                        double prob = winProbs.GetValueOrDefault(party, 0);
                        double indicator = party == actualWinner ? 1.0 : 0.0;
                        ridingBrier += (prob - indicator) * (prob - indicator);
                    }
                    brierSum += ridingBrier;

                    if (summary.RidingVoteShareDistributions.TryGetValue(riding.Id, out var vsDists))
                    {
                        foreach (var party in parties)
                        {
                            var candidate = actualResult.Candidates.FirstOrDefault(c => c.Party == party);
                            double actualShare = (candidate?.VoteShare ?? 0) * 100;
                            if (vsDists.TryGetValue(party, out var dist))
                            {
                                ciTotal++;
                                if (actualShare >= dist.P5 && actualShare <= dist.P95)
                                    ciHits++;
                            }
                        }
                    }
                }

                double brier = totalRidings > 0 ? brierSum / totalRidings : 0;
                double ciCoverage = ciTotal > 0 ? (double)ciHits / ciTotal : 0;
                double accuracy = totalRidings > 0 ? (double)correctWinners / totalRidings : 0;

                var entry = new SigmaSweepEntry
                {
                    NationalSigma = ns,
                    RegionalSigma = regionalSigma,
                    RidingSigma = rs,
                    BrierScore = Math.Round(brier, 4),
                    CiCoverage = Math.Round(ciCoverage, 3),
                    RidingAccuracy = Math.Round(accuracy, 3)
                };
                result.Entries.Add(entry);

                if (brier < bestBrier)
                {
                    bestBrier = brier;
                    bestEntry = entry;
                }

                Console.Write($" {brier,6:F3} ");
            }
            Console.WriteLine();
        }

        Console.WriteLine();
        if (bestEntry != null)
        {
            result.BestNationalSigma = bestEntry.NationalSigma;
            result.BestRegionalSigma = bestEntry.RegionalSigma;
            result.BestRidingSigma = bestEntry.RidingSigma;
            result.BestBrierScore = bestEntry.BrierScore;

            Console.WriteLine($"  Best combination:");
            Console.WriteLine($"    National: {bestEntry.NationalSigma * 100:F0}%, Regional: {bestEntry.RegionalSigma * 100:F1}%, Riding: {bestEntry.RidingSigma * 100:F0}%");
            Console.WriteLine($"    Brier: {bestEntry.BrierScore:F4}, CI coverage: {bestEntry.CiCoverage:P1}, Accuracy: {bestEntry.RidingAccuracy:P1}");
        }
        Console.WriteLine();

        return result;
    }

    private class SigmaSweepResult
    {
        public List<SigmaSweepEntry> Entries { get; set; } = [];
        public double BestNationalSigma { get; set; }
        public double BestRegionalSigma { get; set; }
        public double BestRidingSigma { get; set; }
        public double BestBrierScore { get; set; }
    }

    private class SigmaSweepEntry
    {
        public double NationalSigma { get; set; }
        public double RegionalSigma { get; set; }
        public double RidingSigma { get; set; }
        public double BrierScore { get; set; }
        public double CiCoverage { get; set; }
        public double RidingAccuracy { get; set; }
    }

    private class AlphaSweepResult
    {
        public List<AlphaSweepEntry> Entries { get; set; } = [];
        public double BestAlpha { get; set; }
        public double BestAverageBrier { get; set; }
    }

    private class AlphaSweepEntry
    {
        public double Alpha { get; set; }
        public Dictionary<string, double> BrierByTransition { get; set; } = new();
        public double AverageBrier { get; set; }
        public double Accuracy { get; set; }
        public double CiCoverage { get; set; }
        public double ProjectionRmse { get; set; }
    }

    // --- Degrees of Freedom Sweep ---

    private static DfSweepResult RunDfSweep(
        IReadOnlyList<Riding> ridings,
        List<(string Name, List<RidingResult> Baseline, List<RidingResult> Actual)> transitions,
        IReadOnlyList<Party> parties,
        double alpha)
    {
        Console.WriteLine("--- Degrees of Freedom Sweep ---");
        Console.WriteLine($"  Sweeping df values across {transitions.Count} transitions (1000 sims, seed=42)");
        Console.WriteLine();

        var result = new DfSweepResult();
        double bestAvgBrier = double.MaxValue;
        DfSweepEntry? bestEntry = null;

        // df values to test: 3, 4, 5 (current), 7, 10, null (Gaussian)
        (double? df, string label)[] dfValues =
        [
            (3.0, "df=3"),
            (4.0, "df=4"),
            (5.0, "df=5 (current)"),
            (7.0, "df=7"),
            (10.0, "df=10"),
            (null, "Gaussian"),
        ];

        // Header
        Console.Write($"  {"df",18}");
        foreach (var (transName, _, _) in transitions)
            Console.Write($"  {transName,12}");
        Console.Write($"  {"AvgBrier",10}  {"Accuracy",10}  {"CI Cov",10}  {"LogLoss",10}  {"SimKurt",10}");
        Console.WriteLine();
        Console.Write($"  {"--",18}");
        foreach (var _ in transitions)
            Console.Write($"  {"------------",12}");
        Console.Write($"  {"----------",10}  {"----------",10}  {"----------",10}  {"----------",10}  {"----------",10}");
        Console.WriteLine();

        foreach (var (df, label) in dfValues)
        {
            var entry = new DfSweepEntry { Df = df, Label = label };
            double brierSum = 0;
            int brierCount = 0;
            int totalCorrect = 0;
            int totalRidings = 0;
            int totalCiHits = 0;
            int totalCiTotal = 0;
            double logLossSum = 0;
            int logLossCount = 0;
            var simulatedResiduals = new List<double>();

            Console.Write($"  {label,18}");

            foreach (var (transName, baseline, actual) in transitions)
            {
                var polls = ComputeRegionalAverages(actual, ridings);
                var swingRatios = SwingCalculator.ComputeSwingRatios(polls, baseline, ridings);
                var additiveDeltas = SwingCalculator.ComputeAdditiveDeltas(polls, baseline, ridings);
                var projectedShares = SwingCalculator.ProjectRidingVoteSharesBlended(baseline, ridings, swingRatios, additiveDeltas, alpha, polls);
                var actualLookup = actual.ToDictionary(r => r.RidingId);

                var config = new SimulationConfig(
                    NumSimulations: 1_000,
                    Seed: 42,
                    DegreesOfFreedom: df
                );

                var simulator = new MonteCarloSimulator(ridings, projectedShares);
                var summary = simulator.Run(config);

                int correct = 0;
                int total = 0;
                double transBrierSum = 0;
                int ciHits = 0;
                int ciTotal = 0;

                foreach (var riding in ridings)
                {
                    if (!actualLookup.TryGetValue(riding.Id, out var actualResult))
                        continue;
                    if (!summary.RidingWinProbabilities.TryGetValue(riding.Id, out var winProbs))
                        continue;

                    total++;
                    var actualWinner = actualResult.Candidates.MaxBy(c => c.VoteShare)?.Party ?? Party.Other;
                    var predictedWinner = winProbs.MaxBy(kv => kv.Value).Key;
                    if (predictedWinner == actualWinner) correct++;

                    double ridingBrier = 0;
                    foreach (var party in parties)
                    {
                        double prob = winProbs.GetValueOrDefault(party, 0);
                        double indicator = party == actualWinner ? 1.0 : 0.0;
                        ridingBrier += (prob - indicator) * (prob - indicator);
                    }
                    transBrierSum += ridingBrier;

                    double winnerProb = Math.Max(winProbs.GetValueOrDefault(actualWinner, 0), 0.001);
                    logLossSum += -Math.Log(winnerProb);
                    logLossCount++;

                    if (summary.RidingVoteShareDistributions.TryGetValue(riding.Id, out var vsDists))
                    {
                        foreach (var party in parties)
                        {
                            var candidate = actualResult.Candidates.FirstOrDefault(c => c.Party == party);
                            double actualShare = (candidate?.VoteShare ?? 0) * 100;
                            if (vsDists.TryGetValue(party, out var dist))
                            {
                                ciTotal++;
                                if (actualShare >= dist.P5 && actualShare <= dist.P95)
                                    ciHits++;

                                // Collect simulated residuals (median vs actual)
                                simulatedResiduals.Add(actualShare - dist.Median);
                            }
                        }
                    }
                }

                double transBrier = total > 0 ? transBrierSum / total : 0;
                entry.BrierByTransition[transName] = Math.Round(transBrier, 4);
                brierSum += transBrier;
                brierCount++;
                totalCorrect += correct;
                totalRidings += total;
                totalCiHits += ciHits;
                totalCiTotal += ciTotal;

                Console.Write($"  {transBrier,12:F4}");
            }

            entry.AverageBrier = Math.Round(brierSum / Math.Max(brierCount, 1), 4);
            entry.Accuracy = Math.Round(totalRidings > 0 ? (double)totalCorrect / totalRidings : 0, 3);
            entry.CiCoverage = Math.Round(totalCiTotal > 0 ? (double)totalCiHits / totalCiTotal : 0, 3);
            entry.LogLoss = Math.Round(logLossCount > 0 ? logLossSum / logLossCount : 0, 4);
            entry.SimulatedKurtosis = Math.Round(ExcessKurtosis(simulatedResiduals), 1);

            Console.Write($"  {entry.AverageBrier,10:F4}  {entry.Accuracy,10:P1}  {entry.CiCoverage,10:P1}  {entry.LogLoss,10:F4}  {entry.SimulatedKurtosis,10:F1}");
            Console.WriteLine();

            result.Entries.Add(entry);

            if (entry.AverageBrier < bestAvgBrier)
            {
                bestAvgBrier = entry.AverageBrier;
                bestEntry = entry;
            }
        }

        Console.WriteLine();
        if (bestEntry != null)
        {
            result.BestDf = bestEntry.Df;
            result.BestLabel = bestEntry.Label;
            result.BestAverageBrier = bestEntry.AverageBrier;
            Console.WriteLine($"  Best df: {bestEntry.Label}");
            Console.WriteLine($"    Avg Brier: {bestEntry.AverageBrier:F4}, Accuracy: {bestEntry.Accuracy:P1}, CI coverage: {bestEntry.CiCoverage:P1}, Log loss: {bestEntry.LogLoss:F4}, Kurtosis: {bestEntry.SimulatedKurtosis:F1}");
        }
        Console.WriteLine();

        return result;
    }

    private class DfSweepResult
    {
        public List<DfSweepEntry> Entries { get; set; } = [];
        public double? BestDf { get; set; }
        public string BestLabel { get; set; } = "";
        public double BestAverageBrier { get; set; }
    }

    private class DfSweepEntry
    {
        public double? Df { get; set; }
        public string Label { get; set; } = "";
        public Dictionary<string, double> BrierByTransition { get; set; } = new();
        public double AverageBrier { get; set; }
        public double Accuracy { get; set; }
        public double CiCoverage { get; set; }
        public double LogLoss { get; set; }
        public double SimulatedKurtosis { get; set; }
    }

    // --- Blend Weight Sweep ---

    private static BlendWeightSweepResult RunBlendWeightSweep(
        IReadOnlyList<Riding> ridings,
        List<(string Name, List<RidingResult> Baseline, List<RidingResult> Actual)> transitions,
        IReadOnlyList<Party> parties,
        List<RidingDemographics> demographics,
        double alpha)
    {
        Console.WriteLine("--- Demographic Blend Weight Sweep ---");
        Console.WriteLine($"  Sweeping DemographicBlendWeight [0.00-0.50] across {transitions.Count} transitions (1000 sims, seed=42)");
        Console.WriteLine();

        // Build election lookup for leave-one-out training
        // Map transition names to their actual results for exclusion
        var allElectionResults = new Dictionary<string, List<RidingResult>>();
        foreach (var (name, baseline, actual) in transitions)
        {
            // name is like "2021->2025", extract the target year
            var targetYear = name.Split("->").Last();
            allElectionResults.TryAdd(targetYear, actual);
            var baseYear = name.Split("->").First();
            allElectionResults.TryAdd(baseYear, baseline);
        }

        var result = new BlendWeightSweepResult();
        double bestAvgBrier = double.MaxValue;
        BlendWeightSweepEntry? bestEntry = null;

        // Header
        Console.Write($"  {"Weight",7}");
        foreach (var (transName, _, _) in transitions)
            Console.Write($"  {transName,12}");
        Console.Write($"  {"AvgBrier",10}  {"Accuracy",10}  {"CI Cov",10}  {"ProjRMSE",10}");
        Console.WriteLine();
        Console.Write($"  {"------",7}");
        foreach (var _ in transitions)
            Console.Write($"  {"------------",12}");
        Console.Write($"  {"----------",10}  {"----------",10}  {"----------",10}  {"----------",10}");
        Console.WriteLine();

        double[] weights = [0.00, 0.02, 0.05, 0.08, 0.10, 0.12, 0.15, 0.18, 0.20, 0.25, 0.30, 0.35, 0.40, 0.50];

        foreach (var weight in weights)
        {
            var entry = new BlendWeightSweepEntry { Weight = weight };
            double brierSum = 0;
            int brierCount = 0;
            int totalCorrect = 0;
            int totalRidings = 0;
            int totalCiHits = 0;
            int totalCiTotal = 0;
            double projSseTotal = 0;
            int projN = 0;

            Console.Write($"  {weight,7:F2}");

            foreach (var (transName, baseline, actual) in transitions)
            {
                var polls = ComputeRegionalAverages(actual, ridings);
                var swingRatios = SwingCalculator.ComputeSwingRatios(polls, baseline, ridings);
                var additiveDeltas = SwingCalculator.ComputeAdditiveDeltas(polls, baseline, ridings);
                var projectedShares = SwingCalculator.ProjectRidingVoteSharesBlended(baseline, ridings, swingRatios, additiveDeltas, alpha, polls);

                // Apply demographic prior with leave-one-out training
                if (weight > 0)
                {
                    var targetYear = transName.Split("->").Last();
                    var trainingElections = allElectionResults
                        .Where(kv => kv.Key != targetYear)
                        .Select(kv => (IReadOnlyList<RidingResult>)kv.Value)
                        .ToList();

                    var demographicPrior = DemographicPrior.ComputePrior(ridings, demographics, trainingElections);
                    projectedShares = SwingCalculator.BlendWithDemographicPrior(
                        projectedShares, demographicPrior, ridings, baseline, weight);
                }

                var actualLookup = actual.ToDictionary(r => r.RidingId);

                // Compute projection RMSE (no noise)
                for (int ri = 0; ri < ridings.Count; ri++)
                {
                    var riding = ridings[ri];
                    if (!actualLookup.TryGetValue(riding.Id, out var actualResult))
                        continue;
                    for (int pi = 0; pi < parties.Count; pi++)
                    {
                        var candidate = actualResult.Candidates.FirstOrDefault(c => c.Party == parties[pi]);
                        double actualShare = candidate?.VoteShare ?? 0;
                        double projShare = projectedShares[ri, pi];
                        double err = actualShare - projShare;
                        projSseTotal += err * err;
                        projN++;
                    }
                }

                // Run simulation
                var config = new SimulationConfig(NumSimulations: 1_000, Seed: 42);
                var simulator = new MonteCarloSimulator(ridings, projectedShares);
                var summary = simulator.Run(config);

                // Compute metrics
                int correct = 0;
                int total = 0;
                double transBrierSum = 0;
                int ciHits = 0;
                int ciTotal = 0;

                foreach (var riding in ridings)
                {
                    if (!actualLookup.TryGetValue(riding.Id, out var actualResult))
                        continue;
                    if (!summary.RidingWinProbabilities.TryGetValue(riding.Id, out var winProbs))
                        continue;

                    total++;
                    var actualWinner = actualResult.Candidates.MaxBy(c => c.VoteShare)?.Party ?? Party.Other;
                    var predictedWinner = winProbs.MaxBy(kv => kv.Value).Key;
                    if (predictedWinner == actualWinner) correct++;

                    double ridingBrier = 0;
                    foreach (var party in parties)
                    {
                        double prob = winProbs.GetValueOrDefault(party, 0);
                        double indicator = party == actualWinner ? 1.0 : 0.0;
                        ridingBrier += (prob - indicator) * (prob - indicator);
                    }
                    transBrierSum += ridingBrier;

                    if (summary.RidingVoteShareDistributions.TryGetValue(riding.Id, out var vsDists))
                    {
                        foreach (var party in parties)
                        {
                            var candidate = actualResult.Candidates.FirstOrDefault(c => c.Party == party);
                            double actualShare = (candidate?.VoteShare ?? 0) * 100;
                            if (vsDists.TryGetValue(party, out var dist))
                            {
                                ciTotal++;
                                if (actualShare >= dist.P5 && actualShare <= dist.P95)
                                    ciHits++;
                            }
                        }
                    }
                }

                double transBrier = total > 0 ? transBrierSum / total : 0;
                entry.BrierByTransition[transName] = Math.Round(transBrier, 4);
                brierSum += transBrier;
                brierCount++;
                totalCorrect += correct;
                totalRidings += total;
                totalCiHits += ciHits;
                totalCiTotal += ciTotal;

                Console.Write($"  {transBrier,12:F4}");
            }

            entry.AverageBrier = Math.Round(brierSum / Math.Max(brierCount, 1), 4);
            entry.Accuracy = Math.Round(totalRidings > 0 ? (double)totalCorrect / totalRidings : 0, 3);
            entry.CiCoverage = Math.Round(totalCiTotal > 0 ? (double)totalCiHits / totalCiTotal : 0, 3);
            entry.ProjectionRmse = Math.Round(projN > 0 ? Math.Sqrt(projSseTotal / projN) : 0, 4);

            Console.Write($"  {entry.AverageBrier,10:F4}  {entry.Accuracy,10:P1}  {entry.CiCoverage,10:P1}  {entry.ProjectionRmse,10:F4}");
            Console.WriteLine();

            result.Entries.Add(entry);

            if (entry.AverageBrier < bestAvgBrier)
            {
                bestAvgBrier = entry.AverageBrier;
                bestEntry = entry;
            }
        }

        Console.WriteLine();
        if (bestEntry != null)
        {
            result.BestWeight = bestEntry.Weight;
            result.BestAverageBrier = bestEntry.AverageBrier;
            Console.WriteLine($"  Best weight: {bestEntry.Weight:F2}");
            Console.WriteLine($"    Avg Brier: {bestEntry.AverageBrier:F4}, Accuracy: {bestEntry.Accuracy:P1}, CI coverage: {bestEntry.CiCoverage:P1}, Proj RMSE: {bestEntry.ProjectionRmse:F4}");
        }
        Console.WriteLine();

        return result;
    }

    private class BlendWeightSweepResult
    {
        public List<BlendWeightSweepEntry> Entries { get; set; } = [];
        public double BestWeight { get; set; }
        public double BestAverageBrier { get; set; }
    }

    private class BlendWeightSweepEntry
    {
        public double Weight { get; set; }
        public Dictionary<string, double> BrierByTransition { get; set; } = new();
        public double AverageBrier { get; set; }
        public double Accuracy { get; set; }
        public double CiCoverage { get; set; }
        public double ProjectionRmse { get; set; }
    }
}
