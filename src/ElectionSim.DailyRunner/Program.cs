using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Text.Json.Serialization;
using ElectionSim.Core.Models;
using ElectionSim.DailyRunner.ApiClient;
using ElectionSim.DailyRunner.Scraping;
using ElectionSim.DailyRunner.Weighting;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

// Parse command-line args
string apiUrl = GetArg(args, "--api-url") ?? "http://localhost:5000";
string? apiKey = GetArg(args, "--api-key");
string? fromFile = GetArg(args, "--from-file");
string? cutoffDateArg = GetArg(args, "--cutoff-date");
bool dryRun = args.Contains("--dry-run");
bool force = args.Contains("--force");
bool noCache = args.Contains("--no-cache");
bool verbose = args.Contains("--verbose");

DateOnly? cutoffDate = null;
if (cutoffDateArg != null)
{
    if (DateOnly.TryParseExact(cutoffDateArg, "yyyy-MM-dd", out var parsed))
        cutoffDate = parsed;
    else
    {
        Console.Error.WriteLine("Error: --cutoff-date must be in YYYY-MM-DD format");
        return 1;
    }
}

if (apiKey == null && !dryRun)
{
    Console.Error.WriteLine("Error: --api-key is required (or use --dry-run)");
    return 1;
}

// Set up logging
using var loggerFactory = LoggerFactory.Create(builder =>
{
    builder.AddConsole();
    builder.SetMinimumLevel(verbose ? LogLevel.Debug : LogLevel.Information);
});
var logger = loggerFactory.CreateLogger("DailyRunner");

logger.LogInformation("Election Simulator Daily Runner starting");
logger.LogInformation("API URL: {ApiUrl}, Dry run: {DryRun}, Force: {Force}, No cache: {NoCache}, Cutoff: {Cutoff}",
    apiUrl, dryRun, force, noCache, cutoffDate?.ToString("yyyy-MM-dd") ?? "none");

var pollJsonOptions = new JsonSerializerOptions
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    WriteIndented = true,
    Converters =
    {
        new JsonStringEnumConverter(),
        new DateOnlyJsonConverter()
    }
};

List<ScrapedPoll> regionalPolls;
List<ScrapedPoll> nationalPolls;
bool scraped = false;

if (fromFile != null)
{
    // Load polls from file instead of scraping
    logger.LogInformation("Loading polls from file: {Path}", fromFile);

    if (!File.Exists(fromFile))
    {
        logger.LogError("File not found: {Path}", fromFile);
        return 1;
    }

    var json = await File.ReadAllTextAsync(fromFile);
    var allPolls = JsonSerializer.Deserialize<List<ScrapedPoll>>(json, pollJsonOptions);

    if (allPolls == null || allPolls.Count == 0)
    {
        logger.LogError("No polls found in file: {Path}", fromFile);
        return 1;
    }

    logger.LogInformation("Loaded {Count} polls from file", allPolls.Count);

    regionalPolls = allPolls.Where(p => p.Region != Region.North).ToList();
    nationalPolls = allPolls.Where(p => p.Region == Region.North).ToList();
}
else
{
    // Install Playwright browsers if needed
    logger.LogInformation("Ensuring Playwright browsers are installed...");
    var exitCode = Microsoft.Playwright.Program.Main(["install", "chromium"]);
    if (exitCode != 0)
    {
        logger.LogError("Failed to install Playwright browsers (exit code {Code})", exitCode);
        return 1;
    }

    // Launch browser and scrape
    var scraper = new PollScraper(loggerFactory.CreateLogger<PollScraper>());

    using var playwright = await Playwright.CreateAsync();
    await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
    {
        Headless = true
    });
    var context = await browser.NewContextAsync(new BrowserNewContextOptions
    {
        UserAgent = "Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36"
    });

    // Scrape regional polls
    regionalPolls = await scraper.ScrapeAllRegionsAsync(context, cutoffDate);
    nationalPolls = await scraper.ScrapeNationalAsync(context, cutoffDate);
    scraped = true;

    if (regionalPolls.Count == 0)
    {
        logger.LogError("No regional polls scraped. Exiting.");
        return 1;
    }

    // Check for new data
    var allPolls = regionalPolls.Concat(nationalPolls).ToList();
    var currentPollIds = ComputePollIds(allPolls);

    if (!force && !dryRun && !noCache)
    {
        var lastRun = LoadLastRunState(logger);
        if (lastRun?.KnownPollIds != null)
        {
            var newIds = currentPollIds.Except(lastRun.KnownPollIds).ToList();
            logger.LogDebug("Cache check: {CurrentCount} current poll IDs, {KnownCount} known poll IDs",
                currentPollIds.Count, lastRun.KnownPollIds.Count);

            if (newIds.Count == 0)
            {
                logger.LogInformation("No new polling data since last run ({LastRun}). Exiting.",
                    lastRun.Timestamp);
                return 0;
            }

            logger.LogInformation("Found {Count} new poll(s) since last run ({LastRun})",
                newIds.Count, lastRun.Timestamp);
        }
        else
        {
            logger.LogInformation("No previous cache state found — treating all polls as new");
        }
    }

    // Save scraped polls to ~/polls/{date}.json
    SaveScrapedPolls(allPolls, pollJsonOptions, logger);
}

var weightCalculator = new PollWeightCalculator(loggerFactory.CreateLogger<PollWeightCalculator>());

// Compute weighted averages
var weightedPolling = weightCalculator.ComputeWeightedAverages(
    regionalPolls, out var partyUncertainty);

// For North region: use national polls as fallback
if (!weightedPolling.ContainsKey(Region.North) && nationalPolls.Count > 0)
{
    var nationalWeighted = weightCalculator.ComputeWeightedAverages(
        nationalPolls, out _);
    if (nationalWeighted.TryGetValue(Region.North, out var northShares))
    {
        weightedPolling[Region.North] = northShares;
        logger.LogInformation("Using national polls as North region fallback");
    }
}

// Print summary
logger.LogInformation("=== Weighted Polling Averages ===");
foreach (var (region, shares) in weightedPolling.OrderBy(kv => kv.Key))
{
    var parts = shares.OrderByDescending(kv => kv.Value)
        .Select(kv => $"{kv.Key}={kv.Value:P1}");
    logger.LogInformation("  {Region}: {Shares}", region, string.Join(", ", parts));
}

if (dryRun)
{
    logger.LogInformation("Dry run mode — skipping API call");
    return 0;
}

// Call simulation API
var apiClient = new SimulationApiClient(loggerFactory.CreateLogger<SimulationApiClient>());
var accepted = await apiClient.RunSimulationAsync(apiUrl, apiKey!, weightedPolling, partyUncertainty);

if (!accepted)
{
    logger.LogError("Simulation API call failed");
    return 1;
}

// Save last run state (only when we scraped, not when replaying from file, and not in no-cache mode)
if (scraped && !noCache)
{
    var allPollsForHash = regionalPolls.Concat(nationalPolls).ToList();
    var newPollIds = ComputePollIds(allPollsForHash);
    var previousState = LoadLastRunState(logger);
    if (previousState?.KnownPollIds != null)
        newPollIds.UnionWith(previousState.KnownPollIds);
    SaveLastRunState(new LastRunState(DateTime.UtcNow, newPollIds));
}

logger.LogInformation("Daily run completed successfully");
return 0;

// --- Helper methods ---

static string? GetArg(string[] args, string name)
{
    for (int i = 0; i < args.Length - 1; i++)
    {
        if (args[i] == name) return args[i + 1];
    }
    return null;
}

static string ComputePollId(ScrapedPoll poll)
{
    // Normalize firm name: collapse whitespace, replace Unicode dashes/quotes, lowercase
    var normalizedFirm = Regex.Replace(poll.Firm.Trim(), @"\s+", " ")
        .Replace("\u2013", "-")  // en-dash
        .Replace("\u2014", "-")  // em-dash
        .Replace("\u2018", "'")  // left single quote
        .Replace("\u2019", "'")  // right single quote
        .Replace("\u00A0", " ") // non-breaking space
        .ToLowerInvariant();
    var raw = $"{poll.Region}|{normalizedFirm}|{poll.EndDate:yyyy-MM-dd}|{poll.SampleSize}";
    var hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
    return Convert.ToHexString(hash);
}

static HashSet<string> ComputePollIds(List<ScrapedPoll> polls)
{
    return polls.Select(ComputePollId).ToHashSet();
}

static string GetStateFilePath()
{
    var dir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".electionsim");
    Directory.CreateDirectory(dir);
    return Path.Combine(dir, "last-run.json");
}

static LastRunState? LoadLastRunState(ILogger? log = null)
{
    var path = GetStateFilePath();
    if (!File.Exists(path)) return null;
    try
    {
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<LastRunState>(json);
    }
    catch (Exception ex)
    {
        log?.LogWarning(ex, "Failed to deserialize last-run state from {Path}", path);
        return null;
    }
}

static void SaveLastRunState(LastRunState state)
{
    var path = GetStateFilePath();
    var json = JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true });
    File.WriteAllText(path, json);
}

static void SaveScrapedPolls(List<ScrapedPoll> polls, JsonSerializerOptions options, ILogger logger)
{
    var dir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "polls");
    Directory.CreateDirectory(dir);

    var date = DateTime.UtcNow.ToString("yyyy-MM-dd");
    var path = Path.Combine(dir, $"{date}.json");

    var json = JsonSerializer.Serialize(polls, options);
    File.WriteAllText(path, json);

    logger.LogInformation("Saved {Count} scraped polls to {Path}", polls.Count, path);
}

record LastRunState(DateTime Timestamp, HashSet<string> KnownPollIds);

class DateOnlyJsonConverter : JsonConverter<DateOnly>
{
    private const string Format = "yyyy-MM-dd";

    public override DateOnly Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return DateOnly.ParseExact(reader.GetString()!, Format, CultureInfo.InvariantCulture);
    }

    public override void Write(Utf8JsonWriter writer, DateOnly value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString(Format, CultureInfo.InvariantCulture));
    }
}
