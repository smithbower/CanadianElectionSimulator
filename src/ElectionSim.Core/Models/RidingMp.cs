namespace ElectionSim.Core.Models;

/// <summary>
/// The winning MP for a riding in a specific election year.
/// Used for display purposes only — not part of simulation data.
/// </summary>
public record RidingMp(int RidingId, int Year, string Name, Party Party);
