namespace ElectionSim.Core.Models;

/// <summary>
/// Raw result of a single Monte Carlo simulation: seat counts per party.
/// </summary>
public record SimulationRunResult(Dictionary<Party, int> SeatCounts);

/// <summary>
/// Statistical summary of a party's seat count across all simulations.
/// </summary>
public record SeatDistribution(
    double Mean,
    double Median,
    int P5,
    int P25,
    int P75,
    int P95,
    int Min,
    int Max
);

/// <summary>
/// Per-riding per-party vote share percentiles, computed from 0.5%-bin histograms across all simulations.
/// Values are percentages (0-100), not fractions.
/// </summary>
public record RidingVoteShareDistribution(double Median, double P5, double P25, double P75, double P95);

/// <summary>
/// Aggregated results from all Monte Carlo simulations: seat distributions, riding win probabilities,
/// government formation probabilities, and per-riding vote share distributions.
/// </summary>
public record SimulationSummary(
    int TotalSimulations,
    Dictionary<Party, SeatDistribution> SeatDistributions,
    Dictionary<int, Dictionary<Party, double>> RidingWinProbabilities,
    Dictionary<Party, double> MajorityProbabilities,
    Dictionary<Party, double> MinorityProbabilities,
    Dictionary<int, Dictionary<Party, RidingVoteShareDistribution>> RidingVoteShareDistributions
);
