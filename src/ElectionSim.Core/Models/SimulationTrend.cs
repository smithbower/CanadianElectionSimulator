namespace ElectionSim.Core.Models;

/// <summary>
/// Lightweight trend metrics extracted from a simulation snapshot. Omits per-riding data
/// to keep the trend cache small.
/// </summary>
public record SimulationTrendPoint(
    DateTime Timestamp,
    int BaselineYear,
    Dictionary<Party, double> MeanSeats,
    Dictionary<Party, double> P5Seats,
    Dictionary<Party, double> P25Seats,
    Dictionary<Party, double> P75Seats,
    Dictionary<Party, double> P95Seats,
    Dictionary<Party, double> MajorityProbabilities,
    Dictionary<Party, double> MinorityProbabilities
);

/// <summary>
/// Collection of trend points spanning all historical simulation snapshots, with a generation timestamp.
/// Cached in simulations/trend-cache.json.
/// </summary>
public record SimulationTrendData(
    List<SimulationTrendPoint> Points,
    DateTime GeneratedAt
);
