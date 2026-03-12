using ElectionSim.Core.Models;
using ElectionSim.Core.Simulation;

namespace ElectionSim.Web.Services;

/// <summary>
/// Scoped Blazor service that holds the current simulation inputs (polling data, config,
/// per-party uncertainty) and results. Acts as the central state store for the simulation
/// dashboard. Components subscribe to <see cref="OnStateChanged"/> for reactive re-rendering.
/// </summary>
public class SimulationState(DataService dataService)
{
    public Dictionary<Region, Dictionary<Party, double>> CurrentPolling { get; private set; } = new();
    public SimulationConfig Config { get; set; } = new();
    public SimulationSummary? Results { get; private set; }
    public DateTime? SnapshotTimestamp { get; private set; }
    public bool IsRunning { get; private set; }
    public int SimulationsCompleted { get; private set; }
    public int BaselineYear { get; set; } = 2025;
    public string? ErrorMessage { get; private set; }
    public Dictionary<Party, double> PartyUncertainty { get; set; } = new(SimulationConfig.DefaultPartyUncertainty);
    public Dictionary<int, RidingStatus>? CurrentParliamentaryState { get; private set; }

    public event Action? OnStateChanged;

    /// <summary>Resets all regional polling shares to the baseline values loaded by DataService.</summary>
    public void InitializeFromPolling()
    {
        if (dataService.Polling == null) return;

        CurrentPolling.Clear();
        foreach (var poll in dataService.Polling)
        {
            CurrentPolling[poll.Region] = new Dictionary<Party, double>(poll.VoteShares);
        }
        ComputeParliamentaryState();
        NotifyStateChanged();
    }

    /// <summary>Computes current parliamentary state from baseline results and post-election events.</summary>
    public void ComputeParliamentaryState()
    {
        var baseline = dataService.GetResultsForYear(BaselineYear);
        if (baseline == null) return;

        var events = dataService.PostElectionEvents ?? [];
        CurrentParliamentaryState = ParliamentaryState.ComputeCurrentState(baseline, events, BaselineYear);
    }

    public void UpdateNationalShare(Party party, double value)
    {
        double current = GetNationalShare(party);

        // Proportional scaling: multiply all regional shares by (newNational / currentNational).
        // This preserves the relative regional distribution — e.g., if a party polls at 40%
        // in Quebec and 20% in Ontario, doubling their national share doubles both to 80%/40%.
        if (current > 0)
        {
            double scale = value / current;
            foreach (var region in CurrentPolling.Keys)
            {
                if (CurrentPolling[region].ContainsKey(party))
                {
                    CurrentPolling[region][party] = Math.Clamp(CurrentPolling[region][party] * scale, 0, 1);
                }
            }
        }
        // Zero-support fallback: proportional scaling is impossible (division by zero),
        // so set all regions that carry this party to the target value uniformly.
        else if (value > 0)
        {
            foreach (var region in CurrentPolling.Keys)
            {
                if (CurrentPolling[region].ContainsKey(party))
                {
                    CurrentPolling[region][party] = value;
                }
            }
        }

        NotifyStateChanged();
    }

    public void UpdateRegionalShare(Region region, Party party, double value)
    {
        if (CurrentPolling.TryGetValue(region, out var shares))
        {
            shares[party] = value;
            NotifyStateChanged();
        }
    }

    public double GetRegionalTotal(Region region)
    {
        if (CurrentPolling.TryGetValue(region, out var shares))
            return shares.Values.Sum();
        return 0;
    }

    /// <summary>
    /// Computes national vote share as a weighted average of regional shares, weighted by
    /// riding count per region. This ensures Ontario's 122 ridings count proportionally more
    /// than PEI's 4, matching how Elections Canada reports national popular vote.
    /// </summary>
    public double GetNationalShare(Party party)
    {
        if (CurrentPolling.Count == 0) return 0;
        double totalWeight = 0;
        double weightedSum = 0;
        foreach (var (region, shares) in CurrentPolling)
        {
            int ridingCount = dataService.Ridings?.Count(r => r.Region == region) ?? 1;
            totalWeight += ridingCount;
            weightedSum += shares.GetValueOrDefault(party, 0) * ridingCount;
        }
        return totalWeight > 0 ? weightedSum / totalWeight : 0;
    }

    public async Task RunSimulationAsync()
    {
        if (dataService.Ridings == null) return;

        IsRunning = true;
        SimulationsCompleted = 0;
        ErrorMessage = null;
        SnapshotTimestamp = null;
        NotifyStateChanged();

        try
        {
            var ridings = dataService.Ridings;
            var baseline = BaselineYear switch
            {
                2015 => dataService.Results2015,
                2021 => dataService.Results2021,
                _ => dataService.Results2025
            };
            if (baseline == null || ridings == null) return;

            var polls = CurrentPolling.Select(kv => new RegionalPoll(kv.Key, kv.Value)).ToList();
            // Training elections calibrate the demographic regression prior (only used
            // when UseDemographicPrior is enabled in config).
            var trainingElections = new[] { dataService.Results2025, dataService.Results2021 }
                .Where(e => e != null)
                .Cast<IReadOnlyList<RidingResult>>()
                .ToList();
            var eventsForBaseline = ParliamentaryState.GetEventsForElection(
                dataService.PostElectionEvents ?? [], BaselineYear);
            var projected = SimulationPipeline.ProjectVoteShares(
                ridings, baseline, polls, Config,
                dataService.Demographics, trainingElections,
                eventsForBaseline);

            var simConfig = Config with { PartyUncertainty = new Dictionary<Party, double>(PartyUncertainty) };

            var simulator = new MonteCarloSimulator(ridings, projected);
            var progress = new Progress<int>(count =>
            {
                SimulationsCompleted = count;
                NotifyStateChanged();
            });
            Results = await Task.Run(() => simulator.Run(simConfig, progress));
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsRunning = false;
            NotifyStateChanged();
        }
    }

    /// <summary>Restores the full simulation state (polling, config, results) from a server-persisted snapshot.</summary>
    public void LoadFromSnapshot(SimulationSnapshot snapshot)
    {
        CurrentPolling = snapshot.Polling
            .ToDictionary(kv => kv.Key, kv => new Dictionary<Party, double>(kv.Value));
        Config = snapshot.Config;
        BaselineYear = snapshot.BaselineYear;
        PartyUncertainty = new Dictionary<Party, double>(snapshot.PartyUncertainty);
        Results = snapshot.Results;
        SnapshotTimestamp = snapshot.Timestamp;
        NotifyStateChanged();
    }

    private void NotifyStateChanged() => OnStateChanged?.Invoke();
}
