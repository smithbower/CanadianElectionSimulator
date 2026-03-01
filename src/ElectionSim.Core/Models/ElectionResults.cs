namespace ElectionSim.Core.Models;

/// <summary>
/// A single candidate's result in a riding: their party, raw vote count, and vote share (0.0-1.0).
/// </summary>
public record CandidateResult(Party Party, int Votes, double VoteShare);

/// <summary>
/// Riding-level election result containing all candidates' results and total votes cast.
/// Historical results (pre-2025) are mapped to current riding IDs via name/number matching.
/// </summary>
public record RidingResult(int RidingId, int Year, List<CandidateResult> Candidates, int TotalVotes);
