namespace ElectionSim.Core.Models;

/// <summary>
/// Types of events that can change parliamentary state after a general election.
/// </summary>
public enum PostElectionEventType
{
    /// <summary>An MP switches party allegiance without a by-election.</summary>
    FloorCrossing,
    /// <summary>A by-election is held to fill a vacant seat.</summary>
    ByElection,
    /// <summary>A seat becomes vacant (resignation, death, appointment to Senate, etc.).</summary>
    Vacancy
}

/// <summary>
/// A post-election event that modifies parliamentary state. Events are applied chronologically
/// on top of election results to derive the current state of the House of Commons.
/// <see cref="ElectionYear"/> identifies which parliament this event belongs to (the general
/// election year that preceded it, e.g. 2025 for the 45th Parliament).
/// </summary>
public record PostElectionEvent(
    int RidingId,
    PostElectionEventType Type,
    DateOnly Date,
    int ElectionYear,
    Party? FromParty,
    Party? ToParty,
    string? Description,
    ByElectionResult? ByElectionResult,
    string? MpName = null
);

/// <summary>
/// Full results of a by-election, structured like a general election riding result.
/// </summary>
public record ByElectionResult(
    List<CandidateResult> Candidates,
    int TotalVotes
);
