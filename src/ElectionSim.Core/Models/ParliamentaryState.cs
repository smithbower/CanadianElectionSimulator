namespace ElectionSim.Core.Models;

/// <summary>
/// The current status of a single riding, derived by replaying post-election events
/// on top of the general election result.
/// </summary>
public record RidingStatus(
    int RidingId,
    Party? CurrentHolder,
    bool IsFloorCrosser,
    bool HasByElection,
    Party ElectionWinner,
    List<PostElectionEvent> Events
);

/// <summary>
/// Computes the current parliamentary state by replaying post-election events
/// (floor crossings, vacancies, by-elections) on top of baseline election results.
/// </summary>
public static class ParliamentaryState
{
    /// <summary>
    /// Returns only the events that belong to the parliament following the given election year.
    /// </summary>
    public static IReadOnlyList<PostElectionEvent> GetEventsForElection(
        IReadOnlyList<PostElectionEvent> allEvents, int electionYear)
    {
        return allEvents.Where(e => e.ElectionYear == electionYear).ToList();
    }

    /// <summary>
    /// Replays events chronologically on top of baseline results to derive current state per riding.
    /// Only events matching <paramref name="electionYear"/> are applied.
    /// </summary>
    public static Dictionary<int, RidingStatus> ComputeCurrentState(
        IReadOnlyList<RidingResult> baselineResults,
        IReadOnlyList<PostElectionEvent> allEvents,
        int electionYear)
    {
        var events = GetEventsForElection(allEvents, electionYear);

        // Start with election winners
        var state = new Dictionary<int, RidingStatus>();
        foreach (var result in baselineResults)
        {
            var winner = result.Candidates.OrderByDescending(c => c.VoteShare).First().Party;
            state[result.RidingId] = new RidingStatus(
                RidingId: result.RidingId,
                CurrentHolder: winner,
                IsFloorCrosser: false,
                HasByElection: false,
                ElectionWinner: winner,
                Events: []
            );
        }

        // Replay events in chronological order
        foreach (var evt in events.OrderBy(e => e.Date))
        {
            if (!state.TryGetValue(evt.RidingId, out var current))
                continue;

            var updatedEvents = new List<PostElectionEvent>(current.Events) { evt };

            state[evt.RidingId] = evt.Type switch
            {
                PostElectionEventType.FloorCrossing => current with
                {
                    CurrentHolder = evt.ToParty,
                    IsFloorCrosser = true,
                    Events = updatedEvents
                },
                PostElectionEventType.Vacancy => current with
                {
                    CurrentHolder = null,
                    Events = updatedEvents
                },
                PostElectionEventType.Annulled => current with
                {
                    CurrentHolder = null,
                    Events = updatedEvents
                },
                PostElectionEventType.ByElection => current with
                {
                    CurrentHolder = evt.ToParty,
                    HasByElection = true,
                    IsFloorCrosser = false, // By-election result supersedes any prior floor crossing
                    Events = updatedEvents
                },
                _ => current with { Events = updatedEvents }
            };
        }

        return state;
    }

    /// <summary>
    /// Resolves the current MP name and party for a riding, accounting for post-election events.
    /// Returns (null, null) if the seat is vacant.
    /// </summary>
    public static (string? MpName, Party? CurrentParty) GetCurrentMp(
        RidingStatus? status, RidingMp? electionWinner)
    {
        if (status == null)
            return (electionWinner?.Name, electionWinner?.Party);

        if (status.Events.Count == 0)
            return (electionWinner?.Name, electionWinner?.Party);

        // Check if seat is currently vacant
        if (status.CurrentHolder == null)
            return (null, null);

        // Check for by-election (most recent by-election winner is current MP)
        var lastByElection = status.Events
            .Where(e => e.Type == PostElectionEventType.ByElection)
            .OrderByDescending(e => e.Date)
            .FirstOrDefault();

        if (lastByElection != null)
            return (lastByElection.MpName, status.CurrentHolder);

        // Floor crossing: same person, different party
        if (status.IsFloorCrosser)
            return (electionWinner?.Name, status.CurrentHolder);

        return (electionWinner?.Name, status.CurrentHolder);
    }
}
