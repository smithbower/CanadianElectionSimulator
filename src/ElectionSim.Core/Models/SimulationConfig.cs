namespace ElectionSim.Core.Models;

/// <summary>
/// Configuration parameters for the Monte Carlo simulation engine. All sigma values are
/// fractions (e.g., 0.06 = 6%). See SIMULATION.md for parameter derivation and calibration.
/// </summary>
public record SimulationConfig(
    int NumSimulations = 10_000,
    double NationalSigma = 0.06,
    double RegionalSigma = 0.026,
    double RidingSigma = 0.065,
    int? Seed = null,
    Dictionary<Party, double>? PartyUncertainty = null,
    double? DegreesOfFreedom = 5.0,
    double SwingBlendAlpha = 0.0,
    Dictionary<Region, double>? RegionalSigmaMultipliers = null,
    bool UseCorrelatedNoise = true,
    bool UseDemographicPrior = false,
    double DemographicBlendWeight = 0.02,
    double ByElectionBlendWeight = 0.3
)
{
    /// <summary>
    /// Per-party national sigma defaults derived from empirical residuals across 4 election
    /// transitions (2008→2011, 2011→2015, 2015→2021, 2021→2025). Scaled from full residual
    /// std dev to the noise component using factor ≈ 0.24.
    /// </summary>
    public static readonly Dictionary<Party, double> DefaultPartyUncertainty = new()
    {
        [Party.LPC] = 0.12,
        [Party.CPC] = 0.08,
        [Party.NDP] = 0.06,
        [Party.BQ] = 0.04,
        [Party.GPC] = 0.07,
        [Party.PPC] = 0.03,
        [Party.Other] = 0.03,
    };

    /// <summary>
    /// Per-region sigma multipliers derived from empirical residual std dev ratios across
    /// 4 election transitions. Applied to both RegionalSigma and RidingSigma.
    /// North has a floor of 0.50 (raw 0.20 unreliable with only 3 ridings).
    /// </summary>
    public static readonly Dictionary<Region, double> DefaultRegionalSigmaMultipliers = new()
    {
        [Region.Alberta] = 1.41,
        [Region.Quebec] = 1.22,
        [Region.BritishColumbia] = 1.06,
        [Region.Prairies] = 0.85,
        [Region.Ontario] = 0.81,
        [Region.Atlantic] = 0.43,
        [Region.North] = 0.50,
    };

    /// <summary>
    /// Factory for server-side automated runs. Uses tighter sigmas because the server
    /// runs with current polling data (less uncertainty than user-adjusted shares).
    /// </summary>
    public static SimulationConfig ForServer(
        int numSimulations = 10_000,
        double? nationalSigma = null, double? regionalSigma = null,
        double? ridingSigma = null, int? seed = null,
        Dictionary<Party, double>? partyUncertainty = null,
        double? swingBlendAlpha = null) => new(
        NumSimulations: numSimulations,
        NationalSigma: nationalSigma ?? 0.025,
        RegionalSigma: regionalSigma ?? 0.02,
        RidingSigma: ridingSigma ?? 0.015,
        Seed: seed,
        PartyUncertainty: partyUncertainty,
        SwingBlendAlpha: swingBlendAlpha ?? 1.0);
}
