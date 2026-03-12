using ElectionSim.Core.Models;
using ElectionSim.Web.Services;
using Microsoft.AspNetCore.Components;

namespace ElectionSim.Web.Components;

/// <summary>
/// Sortable table of ridings where the projected winner differs from the current holder
/// (accounting for floor crossings, by-elections, etc.). Shows previous and projected party,
/// win probability, and vote shares.
/// </summary>
public partial class FlippedRidingsTable
{
    [Parameter] public SimulationSummary? Summary { get; set; }
    [Parameter] public IReadOnlyList<Riding>? Ridings { get; set; }
    [Parameter] public EventCallback<Riding> OnRidingSelected { get; set; }

    [Inject] private DataService DataService { get; set; } = default!;
    [Inject] private SimulationState SimulationState { get; set; } = default!;

    private record VoteShareDisplay(double Median, double HalfIqr);

    private record FlippedRidingEntry(
        Riding Riding,
        Party PreviousWinner,
        Party ProjectedWinner,
        double ProjectedWinProb,
        Dictionary<Party, VoteShareDisplay> VoteShares);

    internal const string ColProvince = "province";
    internal const string ColRiding = "riding";
    internal const string ColFrom = "from";
    internal const string ColTo = "to";
    internal const string ColWinProb = "winprob";

    private string sortColumn = ColWinProb;
    private bool sortAscending = true;

    private List<FlippedRidingEntry> GetFlippedRidings()
    {
        var results = new List<FlippedRidingEntry>();
        var baselineResults = DataService.GetResultsForYear(SimulationState.BaselineYear);
        if (Summary == null || Ridings == null || baselineResults == null)
            return results;

        foreach (var riding in Ridings)
        {
            if (!Summary.RidingWinProbabilities.TryGetValue(riding.Id, out var probs))
                continue;

            var projected = probs.OrderByDescending(p => p.Value).First();

            var baselineResult = baselineResults.FirstOrDefault(r => r.RidingId == riding.Id);
            if (baselineResult == null)
                continue;

            var electionWinner = baselineResult.Candidates.OrderByDescending(c => c.VoteShare).First().Party;
            var ridingStatus = SimulationState.CurrentParliamentaryState?.GetValueOrDefault(riding.Id);
            var previousWinner = ridingStatus?.CurrentHolder ?? electionWinner;

            if (projected.Key == previousWinner)
                continue;

            var voteShares = new Dictionary<Party, VoteShareDisplay>();
            if (Summary.RidingVoteShareDistributions.TryGetValue(riding.Id, out var dists))
            {
                foreach (var (party, dist) in dists)
                {
                    var halfIqr = (dist.P75 - dist.P25) / 2.0;
                    voteShares[party] = new VoteShareDisplay(dist.Median, halfIqr);
                }
            }

            results.Add(new FlippedRidingEntry(riding, previousWinner, projected.Key, projected.Value, voteShares));
        }

        IEnumerable<FlippedRidingEntry> sorted = sortColumn switch
        {
            ColProvince => sortAscending
                ? results.OrderBy(e => e.Riding.Province.ToString())
                : results.OrderByDescending(e => e.Riding.Province.ToString()),
            ColRiding => sortAscending
                ? results.OrderBy(e => e.Riding.Name)
                : results.OrderByDescending(e => e.Riding.Name),
            ColFrom => sortAscending
                ? results.OrderBy(e => e.PreviousWinner.ToString())
                : results.OrderByDescending(e => e.PreviousWinner.ToString()),
            ColTo => sortAscending
                ? results.OrderBy(e => e.ProjectedWinner.ToString())
                : results.OrderByDescending(e => e.ProjectedWinner.ToString()),
            ColWinProb => sortAscending
                ? results.OrderBy(e => e.ProjectedWinProb)
                : results.OrderByDescending(e => e.ProjectedWinProb),
            _ when Enum.TryParse(sortColumn, out Party party) => sortAscending
                ? results.OrderBy(e => e.VoteShares.TryGetValue(party, out var vs) ? vs.Median : -1)
                : results.OrderByDescending(e => e.VoteShares.TryGetValue(party, out var vs) ? vs.Median : -1),
            _ => results.OrderBy(e => e.ProjectedWinProb)
        };

        return sorted.ToList();
    }

    internal string HeaderClass(string column, string extra = "")
    {
        var css = "sortable";
        if (!string.IsNullOrEmpty(extra))
            css = extra + " " + css;
        if (sortColumn == column)
            css += " sorted";
        return css;
    }

    internal void SortBy(string column)
    {
        if (sortColumn == column)
        {
            sortAscending = !sortAscending;
        }
        else
        {
            sortColumn = column;
            sortAscending = column is ColProvince or ColRiding or ColFrom or ColTo;
        }
    }

    internal string SortIndicator(string column)
    {
        if (sortColumn != column) return "";
        return sortAscending ? " ▲" : " ▼";
    }
}
