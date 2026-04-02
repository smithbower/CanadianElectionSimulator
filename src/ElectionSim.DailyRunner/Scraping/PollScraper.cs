using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using ElectionSim.Core.Models;

namespace ElectionSim.DailyRunner.Scraping;

/// <summary>
/// A single poll result scraped from 338Canada, with region, pollster metadata, and per-party vote shares.
/// </summary>
public record ScrapedPoll(
    Region Region,
    string Firm,
    DateOnly StartDate,
    DateOnly EndDate,
    int SampleSize,
    string FirmGrade,
    Dictionary<Party, double> VoteShares
);

/// <summary>
/// Scrapes regional and national polling data from 338Canada by extracting the embedded
/// demopoll_TABLE_DATA JavaScript object from https://338canada.com/polls.htm.
/// </summary>
public partial class PollScraper(ILogger<PollScraper> logger)
{
    private const string PollsUrl = "https://338canada.com/polls.htm";

    // Fixed cell order in demopoll_TABLE_DATA: LPC, CPC, NDP, GPC, BQ
    private static readonly Party[] CellPartyOrder = [Party.LPC, Party.CPC, Party.NDP, Party.GPC, Party.BQ];

    // Map 338Canada demo keys to our Region enum
    private static readonly Dictionary<string, Region> DemoKeyToRegion = new(StringComparer.OrdinalIgnoreCase)
    {
        ["National"] = Region.North,
        ["ATL"] = Region.Atlantic,
        ["QC"] = Region.Quebec,
        ["ON"] = Region.Ontario,
        ["PR"] = Region.Prairies,
        ["AB"] = Region.Alberta,
        ["BC"] = Region.BritishColumbia,
    };

    // Fallback mapping using the title/long-form keys
    private static readonly Dictionary<string, Region> TitleToRegion = new(StringComparer.OrdinalIgnoreCase)
    {
        ["National"] = Region.North,
        ["ATL only"] = Region.Atlantic,
        ["Quebec only"] = Region.Quebec,
        ["Ontario only"] = Region.Ontario,
        ["MB/SK only"] = Region.Prairies,
        ["Alberta only"] = Region.Alberta,
        ["B.C. only"] = Region.BritishColumbia,
    };

    [GeneratedRegex(@">([A-F][+\-−]?)<")]
    private static partial Regex GradeBadgeRegex();

    [GeneratedRegex(@"\((\d+)/(\d+)\)")]
    private static partial Regex RollingRegex();

    public async Task<List<ScrapedPoll>> ScrapeAllRegionsAsync(IBrowserContext context, DateOnly? cutoffDate = null)
    {
        var allData = await ScrapePageDataAsync(context);
        if (allData == null) return [];

        var allPolls = new List<ScrapedPoll>();
        foreach (var (demoKey, rows) in allData)
        {
            var region = ResolveRegion(demoKey);
            if (region == null)
            {
                logger.LogWarning("Unknown demo key '{DemoKey}', skipping", demoKey);
                continue;
            }

            // ScrapeAllRegionsAsync returns regional polls only (not national)
            if (region == Region.North) continue;

            var polls = ParseRows(rows, region.Value);
            logger.LogInformation("Found {Count} polls for {Region}", polls.Count, region);
            allPolls.AddRange(polls);
        }

        return ApplyCutoff(allPolls, cutoffDate);
    }

    public async Task<List<ScrapedPoll>> ScrapeNationalAsync(IBrowserContext context, DateOnly? cutoffDate = null)
    {
        var allData = await ScrapePageDataAsync(context);
        if (allData == null) return [];

        foreach (var (demoKey, rows) in allData)
        {
            var region = ResolveRegion(demoKey);
            if (region == Region.North)
            {
                var polls = ParseRows(rows, Region.North);
                logger.LogInformation("Found {Count} national polls", polls.Count);
                return ApplyCutoff(polls, cutoffDate);
            }
        }

        logger.LogWarning("No 'National' demo found in page data");
        return [];
    }

    private async Task<Dictionary<string, List<DemoPollRow>>?> ScrapePageDataAsync(IBrowserContext context)
    {
        var page = await context.NewPageAsync();
        try
        {
            logger.LogInformation("Scraping polls from {Url}", PollsUrl);
            await page.GotoAsync(PollsUrl, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });

            // Extract the embedded demopoll_TABLE_DATA JS object
            var json = await page.EvaluateAsync<string?>(
                "typeof window.demopoll_TABLE_DATA !== 'undefined' ? JSON.stringify(window.demopoll_TABLE_DATA) : null");

            if (json == null)
            {
                logger.LogError("window.demopoll_TABLE_DATA not found on page");
                return null;
            }

            var tableData = JsonSerializer.Deserialize<DemoPollTableData>(json);
            if (tableData?.Demos == null || tableData.Demos.Count == 0)
            {
                logger.LogError("Failed to deserialize demopoll_TABLE_DATA or no demos found");
                return null;
            }

            logger.LogInformation("Extracted {Count} demo regions from page", tableData.Demos.Count);

            var result = new Dictionary<string, List<DemoPollRow>>();
            foreach (var (key, demo) in tableData.Demos)
            {
                result[key] = demo.Rows ?? [];
            }
            return result;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to scrape {Url}", PollsUrl);
            return null;
        }
        finally
        {
            await page.CloseAsync();
        }
    }

    private List<ScrapedPoll> ParseRows(List<DemoPollRow> rows, Region region)
    {
        var polls = new List<ScrapedPoll>();
        foreach (var row in rows)
        {
            try
            {
                var poll = ParseRow(row, region);
                if (poll != null)
                    polls.Add(poll);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to parse poll row for {Firm} on {Date}", row.Firm, row.Date);
            }
        }
        return polls;
    }

    private ScrapedPoll? ParseRow(DemoPollRow row, Region region)
    {
        // Skip election result rows
        if (!string.IsNullOrEmpty(row.GeneralElx)) return null;

        // Parse date
        if (!DateOnly.TryParse(row.Date, out var endDate)) return null;

        // Derive start date: for rolling polls, estimate from the rolling window
        var startDate = DeriveStartDate(endDate, row.IsRolling);

        // Parse sample size (remove commas)
        if (!TryParseSampleSize(row.Sample, out var sampleSize)) return null;

        // Extract grade from HTML badge (e.g., "<span class='rating-badge'...>A</span>" → "A")
        var grade = ExtractGrade(row.RatingBadge);

        // Parse vote shares from cells
        var voteShares = new Dictionary<Party, double>();
        if (row.Cells != null)
        {
            for (int i = 0; i < row.Cells.Count && i < CellPartyOrder.Length; i++)
            {
                var label = row.Cells[i].Label?.Trim();
                if (!string.IsNullOrEmpty(label) && double.TryParse(label, out var share))
                {
                    voteShares[CellPartyOrder[i]] = share / 100.0;
                }
            }
        }

        if (voteShares.Count == 0) return null;

        var firm = row.Firm?.Trim() ?? "Unknown";
        return new ScrapedPoll(region, firm, startDate, endDate, sampleSize, grade, voteShares);
    }

    private static DateOnly DeriveStartDate(DateOnly endDate, string? isRolling)
    {
        if (string.IsNullOrEmpty(isRolling)) return endDate;

        // Parse rolling window from HTML like "<sup>(1/4)</sup>" → denominator 4 (weeks)
        var match = RollingRegex().Match(isRolling);
        if (match.Success && int.TryParse(match.Groups[2].Value, out var weeks) && weeks > 0)
        {
            return endDate.AddDays(-(weeks * 7 - 1));
        }

        return endDate;
    }

    private static string ExtractGrade(string? ratingBadge)
    {
        if (string.IsNullOrEmpty(ratingBadge)) return "C";

        var match = GradeBadgeRegex().Match(ratingBadge);
        if (match.Success)
        {
            // Normalize Unicode minus (−) to ASCII hyphen (-)
            return match.Groups[1].Value.Replace('−', '-');
        }

        return "C";
    }

    private static bool TryParseSampleSize(string? text, out int sampleSize)
    {
        sampleSize = 0;
        if (string.IsNullOrEmpty(text)) return false;
        text = text.Replace(",", "").Replace(" ", "");
        return int.TryParse(text, out sampleSize) && sampleSize > 0;
    }

    private static Region? ResolveRegion(string demoKey)
    {
        if (DemoKeyToRegion.TryGetValue(demoKey, out var region)) return region;
        if (TitleToRegion.TryGetValue(demoKey, out region)) return region;
        return null;
    }

    private List<ScrapedPoll> ApplyCutoff(List<ScrapedPoll> polls, DateOnly? cutoffDate)
    {
        if (cutoffDate == null) return polls;

        var filtered = polls.Where(p => p.EndDate <= cutoffDate.Value).ToList();
        var removed = polls.Count - filtered.Count;
        if (removed > 0)
            logger.LogInformation("Cutoff {Cutoff}: filtered out {Count} polls newer than cutoff date",
                cutoffDate.Value.ToString("yyyy-MM-dd"), removed);
        return filtered;
    }

    // --- JSON deserialization models for demopoll_TABLE_DATA ---

    private sealed class DemoPollTableData
    {
        [JsonPropertyName("demos")]
        public Dictionary<string, DemoRegionData>? Demos { get; set; }
    }

    private sealed class DemoRegionData
    {
        [JsonPropertyName("title")]
        public string? Title { get; set; }

        [JsonPropertyName("rows")]
        public List<DemoPollRow>? Rows { get; set; }
    }

    private sealed class DemoPollRow
    {
        [JsonPropertyName("generalelx")]
        public string? GeneralElx { get; set; }

        [JsonPropertyName("firm")]
        public string? Firm { get; set; }

        [JsonPropertyName("date")]
        public string? Date { get; set; }

        [JsonPropertyName("sample")]
        public string? Sample { get; set; }

        [JsonPropertyName("isRolling")]
        public string? IsRolling { get; set; }

        [JsonPropertyName("ratingbadge")]
        public string? RatingBadge { get; set; }

        [JsonPropertyName("cells")]
        public List<DemoPollCell>? Cells { get; set; }

        [JsonPropertyName("newpoll_link")]
        public string? NewPollLink { get; set; }

        [JsonPropertyName("poll_link")]
        public string? PollLink { get; set; }
    }

    private sealed class DemoPollCell
    {
        [JsonPropertyName("label")]
        public string? Label { get; set; }

        [JsonPropertyName("cellclass")]
        public string? CellClass { get; set; }
    }
}
