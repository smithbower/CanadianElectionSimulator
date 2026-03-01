using ElectionSim.Core.Models;
using ElectionSim.Web.Services;
using Microsoft.AspNetCore.Components;

namespace ElectionSim.Web.Components;

/// <summary>
/// Sortable table of competitive ridings (leader win probability below 80%).
/// Classifies races as Toss-up, Lean, Likely, or Safe based on win probability thresholds.
/// </summary>
public partial class CloseRidingsTable
{
    [Parameter] public SimulationSummary? Summary { get; set; }
    [Parameter] public IReadOnlyList<Riding>? Ridings { get; set; }
    [Parameter] public EventCallback<Riding> OnRidingSelected { get; set; }

    private record VoteShareDisplay(double Median, double HalfIqr);

    private record CloseRidingEntry(
        Riding Riding,
        Party Leader,
        double LeaderProb,
        string Classification,
        Dictionary<Party, VoteShareDisplay> VoteShares);

    internal const string ColProvince = "province";
    internal const string ColRiding = "riding";
    internal const string ColProjection = "projection";

private string sortColumn = ColProjection;
    private bool sortAscending = true;

    private static Dictionary<string, string> ClassificationTooltips => new()
    {
        ["Toss-up"] = "Either party has a realistic chance of winning",
        ["Lean"] = "One party has a slight advantage, but the race is still competitive",
        ["Likely"] = "One party is favored, though an upset remains possible",
        ["Safe"] = "One party is heavily favored to win"
    };

    private List<CloseRidingEntry> GetCloseRidings()
    {
        var results = new List<CloseRidingEntry>();
        if (Summary == null || Ridings == null) return results;

        foreach (var riding in Ridings)
        {
            if (!Summary.RidingWinProbabilities.TryGetValue(riding.Id, out var probs))
                continue;

            var leader = probs.OrderByDescending(p => p.Value).First();
            if (leader.Value >= 0.80)
                continue;

var classification = leader.Value switch
            {
                <= 0.54 => "Toss-up",
                <= 0.67 => "Lean",
                <= 0.85 => "Likely",
                _ => "Safe"
            };

            var voteShares = new Dictionary<Party, VoteShareDisplay>();
            if (Summary.RidingVoteShareDistributions.TryGetValue(riding.Id, out var dists))
            {
                foreach (var (party, dist) in dists)
                {
                    var halfIqr = (dist.P75 - dist.P25) / 2.0;
                    voteShares[party] = new VoteShareDisplay(dist.Median, halfIqr);
                }
            }

            results.Add(new CloseRidingEntry(riding, leader.Key, leader.Value, classification, voteShares));
        }

        IEnumerable<CloseRidingEntry> sorted = sortColumn switch
        {
            ColProvince => sortAscending
                ? results.OrderBy(e => e.Riding.Province.ToString())
                : results.OrderByDescending(e => e.Riding.Province.ToString()),
            ColRiding => sortAscending
                ? results.OrderBy(e => e.Riding.Name)
                : results.OrderByDescending(e => e.Riding.Name),
            ColProjection => sortAscending
                ? results.OrderBy(e => e.LeaderProb)
                : results.OrderByDescending(e => e.LeaderProb),
            _ when Enum.TryParse(sortColumn, out Party party) => sortAscending
                ? results.OrderBy(e => e.VoteShares.TryGetValue(party, out var vs) ? vs.Median : -1)
                : results.OrderByDescending(e => e.VoteShares.TryGetValue(party, out var vs) ? vs.Median : -1),
            _ => results.OrderBy(e => e.LeaderProb)
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
            sortAscending = column is ColProvince or ColRiding;
        }
    }

    internal string SortIndicator(string column)
    {
        if (sortColumn != column) return "";
        return sortAscending ? " \u25B2" : " \u25BC";
    }
}
