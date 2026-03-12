using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using ElectionSim.Core.Models;

namespace ElectionSim.Web.Services;

/// <summary>
/// Client-side data loader for the Blazor WebAssembly app. Fetches all static JSON data
/// files (ridings, election results, polling, hex layout, demographics) from wwwroot/data/
/// on startup via parallel HTTP requests. Also loads trend data and historical snapshots
/// from the server API when available. Properties are nullable because some datasets may
/// not exist (e.g., trends require a server, demographics are optional).
/// </summary>
public class DataService(HttpClient http)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public List<Riding>? Ridings { get; private set; }
    public List<RidingResult>? Results2025 { get; private set; }
    public List<RidingResult>? Results2021 { get; private set; }
    public List<RidingResult>? Results2015 { get; private set; }
    public List<RegionalPoll>? Polling { get; private set; }
    public List<HexPosition>? HexLayout { get; private set; }
    public List<RidingDemographics>? Demographics { get; private set; }
    public List<PostElectionEvent>? PostElectionEvents { get; private set; }
    public List<RidingMp>? RidingMps { get; private set; }
    public bool IsLoaded { get; private set; }
    public SimulationTrendData? TrendData { get; private set; }
    public bool TrendsLoading { get; private set; }

    /// <summary>Fetches all static election data files in parallel. Idempotent; subsequent calls are no-ops.</summary>
    public async Task LoadAllAsync()
    {
        // Multiple components may call LoadAllAsync from OnInitializedAsync,
        // but only the first call performs the HTTP requests.
        if (IsLoaded) return;

        var ridingsTask = LoadAsync<List<Riding>>("data/ridings.json");
        var r2025Task = LoadAsync<List<RidingResult>>("data/results-2025.json");
        var r2021Task = LoadAsync<List<RidingResult>>("data/results-2021.json");
        var r2015Task = LoadAsync<List<RidingResult>>("data/results-2015.json");
        var pollingTask = LoadAsync<List<RegionalPoll>>("data/polling.json");
        var hexTask = LoadAsync<List<HexPosition>>("data/hex-layout.json");
        var demoTask = LoadAsync<List<RidingDemographics>>("data/demographics.json");
        var eventsTask = LoadAsync<List<PostElectionEvent>>("data/post-election-events.json");
        var mpsTask = LoadAsync<List<RidingMp>>("data/riding-mps.json");

        await Task.WhenAll(ridingsTask, r2025Task, r2021Task, r2015Task, pollingTask, hexTask, demoTask, eventsTask, mpsTask);

        Ridings = ridingsTask.Result;
        Results2025 = r2025Task.Result;
        Results2021 = r2021Task.Result;
        Results2015 = r2015Task.Result;
        Polling = pollingTask.Result;
        HexLayout = hexTask.Result;
        Demographics = demoTask.Result;
        PostElectionEvents = eventsTask.Result ?? [];
        RidingMps = mpsTask.Result;
        IsLoaded = true;
    }

    /// <summary>
    /// Loads historical simulation trend data from the server API. Silently fails when the
    /// server is unavailable (e.g., standalone WASM mode without ElectionSim.Server).
    /// </summary>
    public async Task LoadTrendDataAsync()
    {
        TrendsLoading = true;
        try
        {
            var response = await http.GetAsync("api/simulation/trends");
            if (response.IsSuccessStatusCode)
            {
                TrendData = await response.Content.ReadFromJsonAsync<SimulationTrendData>(JsonOptions);
            }
        }
        catch
        {
            // Server may not exist in standalone WASM mode — app degrades gracefully
        }
        finally
        {
            TrendsLoading = false;
        }
    }

    public async Task<SimulationSnapshot?> LoadSnapshotByTimestampAsync(DateTime timestamp)
    {
        try
        {
            var response = await http.GetAsync($"api/simulation/snapshot?timestamp={timestamp:O}");
            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadFromJsonAsync<SimulationSnapshot>(JsonOptions);
            }
        }
        catch
        {
            // Server may not exist in standalone WASM mode
        }
        return null;
    }

    /// <summary>Returns the election results for the given year, or null if not loaded.</summary>
    public List<RidingResult>? GetResultsForYear(int year) => year switch
    {
        2015 => Results2015,
        2021 => Results2021,
        2025 => Results2025,
        _ => null
    };

    /// <summary>Returns the winning MP for a riding in a specific election year.</summary>
    public RidingMp? GetMpForRiding(int ridingId, int year) =>
        RidingMps?.FirstOrDefault(m => m.RidingId == ridingId && m.Year == year);

    /// <summary>Returns all historical MPs for a riding, ordered by year descending.</summary>
    public List<RidingMp> GetMpHistory(int ridingId) =>
        RidingMps?.Where(m => m.RidingId == ridingId)
            .OrderByDescending(m => m.Year)
            .ToList() ?? [];

    private async Task<T?> LoadAsync<T>(string path) where T : class
    {
        try
        {
            return await http.GetFromJsonAsync<T>(path, JsonOptions);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to load {path}: {ex.Message}");
            return default;
        }
    }
}
