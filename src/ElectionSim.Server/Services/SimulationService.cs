using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using ElectionSim.Core.Models;
using ElectionSim.Core.Simulation;

namespace ElectionSim.Server.Services;

/// <summary>
/// Server-side simulation runner. Loads election data from the Web project's wwwroot/data/,
/// runs Monte Carlo simulations, persists snapshots to simulations/{year}/{datetime}.json,
/// and maintains a trend cache for historical tracking.
/// </summary>
public class SimulationService
{
    private readonly string _simulationsDir;
    private readonly string _dataDir;
    private readonly JsonSerializerOptions _jsonOptions;

    /// Serializes reads/writes to trend-cache.json. Without this, concurrent simulation
    /// runs could corrupt the cache by interleaving reads and writes.
    private readonly SemaphoreSlim _trendCacheLock = new(1, 1);

    private List<Riding>? _ridings;
    private List<RidingResult>? _results2025;
    private List<RidingResult>? _results2021;
    private List<RidingResult>? _results2015;
    private List<RegionalPoll>? _polling;
    private List<PostElectionEvent>? _postElectionEvents;

    public SimulationService(IWebHostEnvironment env)
    {
        _simulationsDir = Path.Combine(env.ContentRootPath, "simulations");
        // WebRootPath points to the Server project's wwwroot. The fallback navigates to
        // the Web project's wwwroot, which is where the actual JSON data files live.
        var webRoot = env.WebRootPath
            ?? Path.Combine(env.ContentRootPath, "..", "ElectionSim.Web", "wwwroot");
        _dataDir = Path.Combine(webRoot, "data");
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
            WriteIndented = true
        };
    }

    public async Task LoadDataAsync()
    {
        _ridings = await LoadJsonAsync<List<Riding>>("ridings.json");
        _results2025 = await LoadJsonAsync<List<RidingResult>>("results-2025.json");
        _results2021 = await LoadJsonAsync<List<RidingResult>>("results-2021.json");
        _results2015 = await LoadJsonAsync<List<RidingResult>>("results-2015.json");
        _polling = await LoadJsonAsync<List<RegionalPoll>>("polling.json");
        _postElectionEvents = await LoadJsonAsync<List<PostElectionEvent>>("post-election-events.json") ?? [];
    }

    private async Task<T?> LoadJsonAsync<T>(string filename) where T : class
    {
        var path = Path.Combine(_dataDir, filename);
        if (!File.Exists(path)) return null;
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<T>(stream, _jsonOptions);
    }

    /// <summary>
    /// Runs a full Monte Carlo simulation with the given parameters, persists the
    /// snapshot to disk, and incrementally updates the trend cache.
    /// </summary>
    public async Task<SimulationSnapshot> RunSimulationAsync(SimulationRequest request)
    {
        if (_ridings == null) await LoadDataAsync();
        if (_ridings == null) throw new InvalidOperationException("Failed to load riding data.");

        var baselineYear = request.BaselineYear ?? 2025;
        var baseline = baselineYear switch
        {
            2015 => _results2015,
            2021 => _results2021,
            _ => _results2025
        };
        if (baseline == null) throw new InvalidOperationException($"No baseline data for year {baselineYear}.");

        // Use provided polling or fall back to loaded polling data
        var polling = request.Polling ?? _polling?.ToDictionary(p => p.Region, p => p.VoteShares)
            ?? throw new InvalidOperationException("No polling data available.");

        var partyUncertainty = request.PartyUncertainty
            ?? PartyColorProvider.MainParties.ToDictionary(p => p, _ => 0.025);

        var config = SimulationConfig.ForServer(
            numSimulations: request.NumSimulations ?? 10_000,
            nationalSigma: request.NationalSigma,
            regionalSigma: request.RegionalSigma,
            ridingSigma: request.RidingSigma,
            seed: request.Seed,
            partyUncertainty: new Dictionary<Party, double>(partyUncertainty),
            swingBlendAlpha: request.SwingBlendAlpha
        );

        var polls = polling.Select(kv => new RegionalPoll(kv.Key, kv.Value)).ToList();
        var eventsForBaseline = ParliamentaryState.GetEventsForElection(
            _postElectionEvents ?? [], baselineYear);
        var projected = SimulationPipeline.ProjectVoteShares(
            _ridings, baseline, polls, config,
            postElectionEvents: eventsForBaseline);

        var simulator = new MonteCarloSimulator(_ridings, projected);
        var results = simulator.Run(config);

        var version = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString();

        var snapshot = new SimulationSnapshot(
            Timestamp: DateTime.UtcNow,
            BaselineYear: baselineYear,
            Config: config,
            Polling: polling,
            PartyUncertainty: partyUncertainty,
            Results: results,
            Version: version
        );

        await SaveSnapshotAsync(snapshot);
        await UpdateTrendCacheAsync(snapshot);
        return snapshot;
    }

    public async Task<SimulationSnapshot?> GetSnapshotByTimestampAsync(DateTime timestamp)
    {
        var yearDir = Path.Combine(_simulationsDir, timestamp.Year.ToString());
        if (!Directory.Exists(yearDir)) return null;

        var filename = timestamp.ToString("yyyy-MM-dd_HH-mm-ss") + ".json";
        var path = Path.Combine(yearDir, filename);
        if (!File.Exists(path)) return null;

        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<SimulationSnapshot>(stream, _jsonOptions);
    }

    /// <summary>
    /// Returns the most recent simulation snapshot from the current year's directory, or null if none exist.
    /// </summary>
    public async Task<SimulationSnapshot?> GetLatestSnapshotAsync()
    {
        var yearDir = Path.Combine(_simulationsDir, DateTime.UtcNow.Year.ToString());
        if (!Directory.Exists(yearDir)) return null;

        var latest = Directory.GetFiles(yearDir, "*.json")
            .OrderDescending()
            .FirstOrDefault();

        if (latest == null) return null;

        await using var stream = File.OpenRead(latest);
        return await JsonSerializer.DeserializeAsync<SimulationSnapshot>(stream, _jsonOptions);
    }

    private async Task SaveSnapshotAsync(SimulationSnapshot snapshot)
    {
        var yearDir = Path.Combine(_simulationsDir, snapshot.Timestamp.Year.ToString());
        Directory.CreateDirectory(yearDir);

        var filename = snapshot.Timestamp.ToString("yyyy-MM-dd_HH-mm-ss") + ".json";
        var path = Path.Combine(yearDir, filename);

        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, snapshot, _jsonOptions);
    }

    /// <summary>
    /// Returns aggregated trend data from all historical snapshots. Uses a cache file
    /// (trend-cache.json) to avoid deserializing potentially hundreds of full snapshots
    /// on every request. The cache is rebuilt from scratch if missing.
    /// </summary>
    public async Task<SimulationTrendData?> GetTrendDataAsync()
    {
        await _trendCacheLock.WaitAsync();
        try
        {
            var cachePath = Path.Combine(_simulationsDir, "trend-cache.json");
            if (File.Exists(cachePath))
            {
                await using var stream = File.OpenRead(cachePath);
                return await JsonSerializer.DeserializeAsync<SimulationTrendData>(stream, _jsonOptions);
            }

            return await BuildTrendCacheAsync();
        }
        finally
        {
            _trendCacheLock.Release();
        }
    }

    private async Task<SimulationTrendData?> BuildTrendCacheAsync()
    {
        if (!Directory.Exists(_simulationsDir)) return null;

        var points = new List<SimulationTrendPoint>();

        foreach (var yearDir in Directory.GetDirectories(_simulationsDir))
        {
            foreach (var file in Directory.GetFiles(yearDir, "*.json"))
            {
                try
                {
                    await using var stream = File.OpenRead(file);
                    var snapshot = await JsonSerializer.DeserializeAsync<SimulationSnapshot>(stream, _jsonOptions);
                    if (snapshot != null)
                        points.Add(ExtractTrendPoint(snapshot));
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Warning: Skipping malformed snapshot '{file}': {ex.Message}");
                }
            }
        }

        if (points.Count == 0) return null;

        points.Sort((a, b) => a.Timestamp.CompareTo(b.Timestamp));

        var trendData = new SimulationTrendData(points, DateTime.UtcNow);
        await SaveTrendCacheAsync(trendData);
        return trendData;
    }

    /// Appends the new snapshot's trend point to the existing cache (O(1)) rather than
    /// rebuilding from all snapshots (O(n)). Falls back to full rebuild if cache is missing.
    private async Task UpdateTrendCacheAsync(SimulationSnapshot snapshot)
    {
        await _trendCacheLock.WaitAsync();
        try
        {
            var cachePath = Path.Combine(_simulationsDir, "trend-cache.json");
            SimulationTrendData? existing = null;

            if (File.Exists(cachePath))
            {
                await using var stream = File.OpenRead(cachePath);
                existing = await JsonSerializer.DeserializeAsync<SimulationTrendData>(stream, _jsonOptions);
            }

            if (existing == null)
            {
                existing = await BuildTrendCacheAsync();
                return; // BuildTrendCacheAsync already saves
            }

            existing.Points.Add(ExtractTrendPoint(snapshot));
            existing.Points.Sort((a, b) => a.Timestamp.CompareTo(b.Timestamp));

            var updated = new SimulationTrendData(existing.Points, DateTime.UtcNow);
            await SaveTrendCacheAsync(updated);
        }
        finally
        {
            _trendCacheLock.Release();
        }
    }

    private async Task SaveTrendCacheAsync(SimulationTrendData trendData)
    {
        Directory.CreateDirectory(_simulationsDir);
        var cachePath = Path.Combine(_simulationsDir, "trend-cache.json");
        await using var stream = File.Create(cachePath);
        await JsonSerializer.SerializeAsync(stream, trendData, _jsonOptions);
    }

    /// Extracts lightweight trend metrics (seat distribution stats and government-formation
    /// probabilities) from a full snapshot. Per-riding data is intentionally discarded to
    /// keep the trend cache small.
    private static SimulationTrendPoint ExtractTrendPoint(SimulationSnapshot snapshot)
    {
        var dists = snapshot.Results.SeatDistributions;
        var meanSeats = dists.ToDictionary(kv => kv.Key, kv => kv.Value.Mean);
        var p5Seats = dists.ToDictionary(kv => kv.Key, kv => (double)kv.Value.P5);
        var p25Seats = dists.ToDictionary(kv => kv.Key, kv => (double)kv.Value.P25);
        var p75Seats = dists.ToDictionary(kv => kv.Key, kv => (double)kv.Value.P75);
        var p95Seats = dists.ToDictionary(kv => kv.Key, kv => (double)kv.Value.P95);

        return new SimulationTrendPoint(
            Timestamp: snapshot.Timestamp,
            BaselineYear: snapshot.BaselineYear,
            MeanSeats: meanSeats,
            P5Seats: p5Seats,
            P25Seats: p25Seats,
            P75Seats: p75Seats,
            P95Seats: p95Seats,
            MajorityProbabilities: new Dictionary<Party, double>(snapshot.Results.MajorityProbabilities),
            MinorityProbabilities: new Dictionary<Party, double>(snapshot.Results.MinorityProbabilities)
        );
    }
}

/// <summary>
/// Parameters for a server-side simulation run, received via POST /api/simulation/run.
/// All fields are optional; defaults are applied by <see cref="SimulationConfig.ForServer"/>.
/// </summary>
public record SimulationRequest(
    int? BaselineYear = null,
    int? NumSimulations = null,
    double? NationalSigma = null,
    double? RegionalSigma = null,
    double? RidingSigma = null,
    int? Seed = null,
    Dictionary<Region, Dictionary<Party, double>>? Polling = null,
    Dictionary<Party, double>? PartyUncertainty = null,
    double? SwingBlendAlpha = null
);
