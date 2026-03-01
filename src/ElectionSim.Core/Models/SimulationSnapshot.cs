namespace ElectionSim.Core.Models;

/// <summary>
/// Complete snapshot of a simulation run: inputs (polling, config, uncertainty) and results.
/// Persisted to simulations/{year}/{datetime}.json by the server.
/// </summary>
public record SimulationSnapshot(
    DateTime Timestamp,
    int BaselineYear,
    SimulationConfig Config,
    Dictionary<Region, Dictionary<Party, double>> Polling,
    Dictionary<Party, double> PartyUncertainty,
    SimulationSummary Results,
    string? Version = null
);
