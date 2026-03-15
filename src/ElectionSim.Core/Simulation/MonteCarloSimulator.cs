using ElectionSim.Core.Models;

namespace ElectionSim.Core.Simulation;

/// <summary>
/// Core simulation engine. Runs N parallel Monte Carlo simulations with correlated Student-t
/// noise at national, regional, and riding levels, then aggregates seat distributions and
/// riding win probabilities. See SIMULATION.md for methodology.
/// </summary>
public class MonteCarloSimulator
{
    private const int NumBins = 201; // 0.0% to 100.0% in 0.5% steps

    private readonly IReadOnlyList<Riding> _ridings;
    private readonly double[,] _projectedShares; // [ridingIndex, partyIndex]
    private readonly int _numRidings;
    private readonly int _numParties;
    private readonly int[] _ridingRegions; // region index per riding

    public MonteCarloSimulator(IReadOnlyList<Riding> ridings, double[,] projectedShares)
    {
        _ridings = ridings;
        _projectedShares = projectedShares;
        _numRidings = ridings.Count;
        _numParties = PartyColourProvider.MainParties.Count;

        _ridingRegions = new int[_numRidings];
        for (int i = 0; i < _numRidings; i++)
            _ridingRegions[i] = (int)ridings[i].Region;
    }

    public SimulationSummary Run(SimulationConfig config, IProgress<int>? progress = null)
    {
        int numRegions = Enum.GetValues<Region>().Length;
        var allResults = new SimulationRunResult[config.NumSimulations];

        // Pre-compute per-party national sigmas
        var nationalSigmas = BuildNationalSigmas(config);

        // Pre-compute per-region sigma multipliers
        var regionMultipliers = BuildRegionMultipliers(config, numRegions);

        // Pre-compute Student-t scale factor so effective sigma matches the configured value
        double tScale = ComputeStudentTScale(config.DegreesOfFreedom);

        // Pre-compute Cholesky factor for correlated noise
        var cholesky = config.UseCorrelatedNoise ? CorrelationData.CholeskyFactor : null;

        // Track per-riding winners across all sims
        var ridingWins = new int[_numRidings, _numParties];

        // Global histogram — thread-local copies merged after Parallel.For
        int histLen = _numRidings * _numParties * NumBins;
        var histogram = new int[histLen];

        int completedCount = 0;
        int reportInterval = Math.Max(config.NumSimulations / 100, 1);

        Parallel.For(0, config.NumSimulations, new ParallelOptions(),
            () => (
                Rng: config.Seed.HasValue
                    ? new Random(config.Seed.Value + Environment.CurrentManagedThreadId)
                    : new Random(),
                LocalHist: new int[histLen]
            ),
            (sim, state, local) =>
            {
                var rng = local.Rng;
                var localHist = local.LocalHist;
                var seatCounts = new int[_numParties];

                // Draw national errors (correlated or independent)
                Span<double> nationalNoise = stackalloc double[_numParties];
                DrawCorrelatedNoise(rng, config.DegreesOfFreedom, tScale, cholesky, _numParties, nationalNoise);
                var nationalError = new double[_numParties];
                for (int p = 0; p < _numParties; p++)
                    nationalError[p] = nationalNoise[p] * nationalSigmas[p];

                // Draw regional errors (correlated or independent, scaled by per-region multiplier)
                var regionalError = new double[numRegions, _numParties];
                Span<double> regionNoise = stackalloc double[_numParties];
                for (int r = 0; r < numRegions; r++)
                {
                    double sigma_r = config.RegionalSigma * regionMultipliers[r];
                    DrawCorrelatedNoise(rng, config.DegreesOfFreedom, tScale, cholesky, _numParties, regionNoise);
                    for (int p = 0; p < _numParties; p++)
                        regionalError[r, p] = regionNoise[p] * sigma_r;
                }

                // Simulate each riding
                var adjusted = new double[_numParties];
                var winners = new int[_numRidings];
                Span<double> ridingNoise = stackalloc double[_numParties];

                for (int ri = 0; ri < _numRidings; ri++)
                {
                    int regionIdx = _ridingRegions[ri];
                    double sum = 0;
                    double ridingSigma = config.RidingSigma * regionMultipliers[regionIdx];

                    DrawCorrelatedNoise(rng, config.DegreesOfFreedom, tScale, cholesky, _numParties, ridingNoise);
                    for (int p = 0; p < _numParties; p++)
                    {
                        adjusted[p] = _projectedShares[ri, p]
                            + nationalError[p]
                            + regionalError[regionIdx, p]
                            + ridingNoise[p] * ridingSigma;
                        if (adjusted[p] < 0) adjusted[p] = 0;
                        sum += adjusted[p];
                    }

                    // Renormalize
                    if (sum > 0)
                        for (int p = 0; p < _numParties; p++)
                            adjusted[p] /= sum;

                    // Find winner (FPTP)
                    int winner = 0;
                    double maxShare = adjusted[0];
                    for (int p = 1; p < _numParties; p++)
                    {
                        if (adjusted[p] > maxShare)
                        {
                            maxShare = adjusted[p];
                            winner = p;
                        }
                    }

                    seatCounts[winner]++;
                    winners[ri] = winner;

                    // Accumulate vote share histogram (thread-local)
                    for (int p = 0; p < _numParties; p++)
                    {
                        int bin = (int)(adjusted[p] * 200.0);
                        if (bin >= NumBins) bin = NumBins - 1;
                        localHist[(ri * _numParties + p) * NumBins + bin]++;
                    }
                }

                // Record riding wins (thread-safe via Interlocked)
                for (int ri = 0; ri < _numRidings; ri++)
                    Interlocked.Increment(ref ridingWins[ri, winners[ri]]);

                var dict = new Dictionary<Party, int>();
                var parties = PartyColourProvider.MainParties;
                for (int p = 0; p < _numParties; p++)
                    dict[parties[p]] = seatCounts[p];

                allResults[sim] = new SimulationRunResult(dict);

                int count = Interlocked.Increment(ref completedCount);
                if (count % reportInterval == 0 || count == config.NumSimulations)
                    progress?.Report(count);

                return local;
            },
            local =>
            {
                // Merge thread-local histogram into global
                lock (histogram)
                {
                    for (int i = 0; i < histLen; i++)
                        histogram[i] += local.LocalHist[i];
                }
            }
        );

        return Aggregate(allResults, ridingWins, histogram, config.NumSimulations);
    }

    /// <summary>
    /// Draws unit-variance correlated (via Cholesky) or independent noise samples.
    /// Callers scale the output by their own sigma.
    /// </summary>
    private static void DrawCorrelatedNoise(
        Random rng, double? df, double tScale,
        double[,]? cholesky, int numParties,
        Span<double> output)
    {
        if (cholesky != null)
        {
            Span<double> z = stackalloc double[numParties];
            for (int p = 0; p < numParties; p++)
                z[p] = NextNoise(rng, df, tScale);
            for (int p = 0; p < numParties; p++)
            {
                double correlated = 0;
                for (int q = 0; q <= p; q++)
                    correlated += cholesky[p, q] * z[q];
                output[p] = correlated;
            }
        }
        else
        {
            for (int p = 0; p < numParties; p++)
                output[p] = NextNoise(rng, df, tScale);
        }
    }

    private SimulationSummary Aggregate(SimulationRunResult[] results, int[,] ridingWins, int[] histogram, int totalSims)
    {
        var parties = PartyColourProvider.MainParties;
        int majorityThreshold = (_numRidings / 2) + 1; // 172 for 343 ridings

        // Seat distributions per party
        var seatDistributions = new Dictionary<Party, SeatDistribution>();
        var majorityProbs = new Dictionary<Party, double>();

        foreach (var party in parties)
        {
            var seats = results.Select(r => r.SeatCounts.GetValueOrDefault(party, 0)).OrderBy(x => x).ToArray();
            int n = seats.Length;

            seatDistributions[party] = new SeatDistribution(
                Mean: seats.Average(),
                Median: seats[n / 2],
                P5: seats[(int)(n * 0.05)],
                P25: seats[(int)(n * 0.25)],
                P75: seats[(int)(n * 0.75)],
                P95: seats[(int)(n * 0.95)],
                Min: seats[0],
                Max: seats[n - 1]
            );

            majorityProbs[party] = (double)seats.Count(s => s >= majorityThreshold) / n;
        }

        // Minority probabilities (most seats but below majority)
        var minorityProbs = new Dictionary<Party, double>();
        var minorityCounts = new Dictionary<Party, int>();
        foreach (var party in parties)
            minorityCounts[party] = 0;

        foreach (var result in results)
        {
            var leader = result.SeatCounts.MaxBy(kv => kv.Value);
            if (leader.Value > 0 && leader.Value < majorityThreshold)
                minorityCounts[leader.Key]++;
        }

        foreach (var party in parties)
            minorityProbs[party] = (double)minorityCounts[party] / totalSims;

        // Riding win probabilities
        var ridingWinProbs = new Dictionary<int, Dictionary<Party, double>>();
        for (int ri = 0; ri < _numRidings; ri++)
        {
            var probs = new Dictionary<Party, double>();
            for (int p = 0; p < parties.Count; p++)
                probs[parties[p]] = (double)ridingWins[ri, p] / totalSims;
            ridingWinProbs[_ridings[ri].Id] = probs;
        }

        // Vote share distributions from histograms
        var voteShareDists = new Dictionary<int, Dictionary<Party, RidingVoteShareDistribution>>();
        double[] percentileRanks = [0.05, 0.25, 0.50, 0.75, 0.95];

        for (int ri = 0; ri < _numRidings; ri++)
        {
            var partyDists = new Dictionary<Party, RidingVoteShareDistribution>();
            for (int p = 0; p < _numParties; p++)
            {
                int baseIdx = (ri * _numParties + p) * NumBins;
                double[] pctValues = new double[5];
                int cumulative = 0;
                int pctIdx = 0;

                for (int b = 0; b < NumBins && pctIdx < 5; b++)
                {
                    cumulative += histogram[baseIdx + b];
                    while (pctIdx < 5 && (double)cumulative / totalSims >= percentileRanks[pctIdx])
                    {
                        pctValues[pctIdx] = b * 0.5; // Convert bin to percentage
                        pctIdx++;
                    }
                }

                // Fill any remaining percentiles (should not happen normally)
                while (pctIdx < 5)
                {
                    pctValues[pctIdx] = 100.0;
                    pctIdx++;
                }

                partyDists[parties[p]] = new RidingVoteShareDistribution(
                    Median: pctValues[2],
                    P5: pctValues[0],
                    P25: pctValues[1],
                    P75: pctValues[3],
                    P95: pctValues[4]
                );
            }
            voteShareDists[_ridings[ri].Id] = partyDists;
        }

        return new SimulationSummary(totalSims, seatDistributions, ridingWinProbs, majorityProbs, minorityProbs, voteShareDists);
    }

    private double[] BuildNationalSigmas(SimulationConfig config)
    {
        var sigmas = new double[_numParties];
        var parties = PartyColourProvider.MainParties;
        for (int p = 0; p < _numParties; p++)
        {
            if (config.PartyUncertainty != null && config.PartyUncertainty.TryGetValue(parties[p], out double sigma))
                sigmas[p] = sigma;
            else if (SimulationConfig.DefaultPartyUncertainty.TryGetValue(parties[p], out double defaultSigma))
                sigmas[p] = defaultSigma;
            else
                sigmas[p] = config.NationalSigma;
        }
        return sigmas;
    }

    private static double[] BuildRegionMultipliers(SimulationConfig config, int numRegions)
    {
        var multipliers = new double[numRegions];
        var source = config.RegionalSigmaMultipliers ?? SimulationConfig.DefaultRegionalSigmaMultipliers;
        for (int r = 0; r < numRegions; r++)
        {
            var region = (Region)r;
            multipliers[r] = source.TryGetValue(region, out double m) ? m : 1.0;
        }
        return multipliers;
    }

    internal static double NextGaussian(Random rng)
    {
        // Box-Muller transform
        double u1 = 1.0 - rng.NextDouble();
        double u2 = rng.NextDouble();
        return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Sin(2.0 * Math.PI * u2);
    }

    /// <summary>
    /// Returns a noise sample: Student-t if df is finite, Gaussian otherwise.
    /// The tScale factor ensures the effective standard deviation matches the configured sigma.
    /// </summary>
    internal static double NextNoise(Random rng, double? df, double tScale)
    {
        if (df is null || double.IsPositiveInfinity(df.Value) || df.Value <= 2)
            return NextGaussian(rng);

        return NextStudentT(rng, df.Value) * tScale;
    }

    /// <summary>
    /// Sample from Student-t distribution with the given degrees of freedom.
    /// Uses the ratio: t = z / sqrt(v / df), where z ~ N(0,1) and v ~ chi-squared(df).
    /// </summary>
    internal static double NextStudentT(Random rng, double df)
    {
        double z = NextGaussian(rng);

        // Chi-squared with df degrees of freedom = sum of df standard normals squared
        int intDf = (int)df;
        double v = 0;
        for (int i = 0; i < intDf; i++)
        {
            double g = NextGaussian(rng);
            v += g * g;
        }

        return z / Math.Sqrt(v / intDf);
    }

    /// <summary>
    /// Compute scale factor so that NextStudentT * scale has unit variance.
    /// Student-t with df has variance df/(df-2), so scale = sqrt((df-2)/df).
    /// Returns 1.0 for Gaussian (df=null or infinity).
    /// </summary>
    internal static double ComputeStudentTScale(double? df)
    {
        if (df is null || double.IsPositiveInfinity(df.Value) || df.Value <= 2)
            return 1.0;

        return Math.Sqrt((df.Value - 2.0) / df.Value);
    }
}
