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
/// Scrapes regional and national polling tables from 338Canada using Playwright.
/// Parses pollster name, field dates, sample size, firm grade, and per-party vote shares.
/// </summary>
public class PollScraper(ILogger<PollScraper> logger)
{
    private static readonly Dictionary<string, Region> RegionalPages = new()
    {
        ["polls-atl.htm"] = Region.Atlantic,
        ["polls-qc.htm"] = Region.Quebec,
        ["polls-on.htm"] = Region.Ontario,
        ["polls-pr.htm"] = Region.Prairies,
        ["polls-ab.htm"] = Region.Alberta,
        ["polls-bc.htm"] = Region.BritishColumbia,
    };

    public async Task<List<ScrapedPoll>> ScrapeAllRegionsAsync(IBrowserContext context, DateOnly? cutoffDate = null)
    {
        var allPolls = new List<ScrapedPoll>();

        foreach (var (page, region) in RegionalPages)
        {
            var url = $"https://338canada.com/{page}";
            logger.LogInformation("Scraping {Region} from {Url}", region, url);

            try
            {
                var polls = await ScrapePageAsync(context, url, region);
                logger.LogInformation("Found {Count} polls for {Region}", polls.Count, region);
                allPolls.AddRange(polls);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to scrape {Region} from {Url}", region, url);
            }
        }

        return ApplyCutoff(allPolls, cutoffDate);
    }

    public async Task<List<ScrapedPoll>> ScrapeNationalAsync(IBrowserContext context, DateOnly? cutoffDate = null)
    {
        var url = "https://338canada.com/polls.htm";
        logger.LogInformation("Scraping national polls from {Url}", url);

        try
        {
            var polls = await ScrapePageAsync(context, url, Region.North);
            logger.LogInformation("Found {Count} national polls", polls.Count);
            return ApplyCutoff(polls, cutoffDate);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to scrape national polls");
            return [];
        }
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

    private async Task<List<ScrapedPoll>> ScrapePageAsync(
        IBrowserContext context, string url, Region region)
    {
        var page = await context.NewPageAsync();
        try
        {
            await page.GotoAsync(url, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });

            // Wait for the poll table to render
            var tableSelector = await WaitForTableAsync(page);
            if (tableSelector == null)
            {
                logger.LogWarning("No poll table found on {Url}", url);
                return [];
            }

            var rows = await page.QuerySelectorAllAsync($"{tableSelector} tbody tr");
            if (rows.Count == 0)
                rows = await page.QuerySelectorAllAsync($"{tableSelector} tr");

            var polls = new List<ScrapedPoll>();

            foreach (var row in rows)
            {
                try
                {
                    var poll = await ParseRowAsync(row, region);
                    if (poll != null)
                    {
                        polls.Add(poll);
                    }
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to parse poll row on {Url}", url);
                }
            }

            return polls;
        }
        finally
        {
            await page.CloseAsync();
        }
    }

    private static async Task<string?> WaitForTableAsync(IPage page)
    {
        var selectors = new[] { "#myTable", ".poll-table", "table" };
        foreach (var selector in selectors)
        {
            try
            {
                var element = await page.WaitForSelectorAsync(selector, new PageWaitForSelectorOptions
                {
                    Timeout = 10_000
                });
                if (element != null) return selector;
            }
            catch (TimeoutException)
            {
                // Try next selector
            }
        }
        return null;
    }

    private async Task<ScrapedPoll?> ParseRowAsync(IElementHandle row, Region region)
    {
        var cells = await row.QuerySelectorAllAsync("td");
        if (cells.Count < 3) return null;

        // 338canada table format:
        // Col 0: Multi-line cell with "Firm\nDate, n=SampleSize"
        // Col 1: Grade (e.g., "A−", "B+")
        // Col 2+: Party vote shares

        var firstCellText = (await cells[0].InnerTextAsync()).Trim();
        var lines = firstCellText.Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length < 2) return null;

        var firm = lines[0].Trim();
        var detailLine = lines[1].Trim();

        // Split detail line into date part and sample size part on ", n="
        string dateText;
        int sampleSize = 0;
        var nIndex = detailLine.IndexOf(", n=", StringComparison.OrdinalIgnoreCase);
        if (nIndex >= 0)
        {
            dateText = detailLine[..nIndex].Trim();
            var sampleText = detailLine[(nIndex + 4)..].Trim();
            // Remove trailing parenthetical like "(1/2)"
            var parenIdx = sampleText.IndexOf('(');
            if (parenIdx >= 0) sampleText = sampleText[..parenIdx].Trim();
            if (!TryParseSampleSize(sampleText, out sampleSize)) return null;
        }
        else
        {
            dateText = detailLine;
        }

        var (startDate, endDate) = ParseDates(dateText);
        if (startDate == default || endDate == default) return null;

        var grade = (await cells[1].InnerTextAsync()).Trim();
        if (string.IsNullOrEmpty(grade)) grade = "C";

        // Parse header to determine party column order
        var table = await row.EvaluateHandleAsync("el => el.closest('table')");
        var tableEl = (table as IElementHandle)!;
        var partyColumns = await MapPartyColumnsFromTableAsync(tableEl);

        var voteShares = new Dictionary<Party, double>();
        foreach (var (colIndex, party) in partyColumns)
        {
            if (colIndex < cells.Count)
            {
                var text = (await cells[colIndex].InnerTextAsync()).Trim().TrimEnd('%');
                if (double.TryParse(text, out double share))
                {
                    voteShares[party] = share / 100.0;
                }
            }
        }

        if (voteShares.Count == 0) return null;

        return new ScrapedPoll(region, firm, startDate, endDate, sampleSize, grade, voteShares);
    }

    private static (DateOnly Start, DateOnly End) ParseDates(string text)
    {
        // Common formats:
        // "Jan 15-20, 2026"
        // "Jan 15 - Feb 2, 2026"
        // "2026-01-15 to 2026-01-20"
        // "January 15-20, 2026"

        try
        {
            text = text.Replace("–", "-").Replace("—", "-");

            // Try single date first (handles ISO "2026-02-22" and "January 15, 2026")
            if (DateOnly.TryParse(text, out var single))
                return (single, single);

            // Handle "to" separator (e.g., "2026-01-15 to 2026-01-20")
            var toParts = text.Split(" to ", 2, StringSplitOptions.TrimEntries);
            if (toParts.Length == 2 &&
                DateOnly.TryParse(toParts[0], out var toStart) &&
                DateOnly.TryParse(toParts[1], out var toEnd))
            {
                return (toStart, toEnd);
            }

            // Handle "Month Day-Day, Year" or "Month Day - Month Day, Year"
            var parts = text.Split(['-'], 2);
            if (parts.Length == 2)
            {
                var rightPart = parts[1].Trim();

                // Try to parse the end part first to get the year
                if (DateOnly.TryParse(rightPart, out var endDate))
                {
                    // Parse start - it may lack the year (e.g., "Jan 15 - Feb 2, 2026")
                    var startText = parts[0].Trim();
                    if (DateOnly.TryParse(startText + ", " + endDate.Year, out var startDate))
                        return (startDate, endDate);
                    if (DateOnly.TryParse(startText, out startDate))
                        return (startDate, endDate);
                }
                else
                {
                    // Handle "Jan 15" + "20, 2026" → reconstruct "Jan 20, 2026"
                    var startText = parts[0].Trim();
                    var startParts = startText.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (startParts.Length >= 2)
                    {
                        var month = startParts[0];
                        if (DateOnly.TryParse($"{month} {rightPart}", out endDate))
                        {
                            if (DateOnly.TryParse($"{startText}, {endDate.Year}", out var startDate))
                                return (startDate, endDate);
                        }
                    }
                }

                // Try parsing both as full dates
                if (DateOnly.TryParse(parts[0].Trim(), out var start) &&
                    DateOnly.TryParse(parts[1].Trim(), out var end))
                {
                    return (start, end);
                }
            }
        }
        catch
        {
            // Fall through
        }

        return default;
    }

    private static bool TryParseSampleSize(string text, out int sampleSize)
    {
        // Remove commas, spaces, "n=" prefix
        text = text.Replace(",", "").Replace(" ", "").Replace("n=", "").Replace("N=", "");
        return int.TryParse(text, out sampleSize) && sampleSize > 0;
    }

    private async Task<Dictionary<int, Party>> MapPartyColumnsFromTableAsync(IElementHandle tableEl)
    {
        // 338Canada uses a header row (tr.header or first tr) with party logos as <img src="LPC.svg">
        var headerRow = await tableEl.QuerySelectorAsync("tr.header") ??
                        await tableEl.QuerySelectorAsync("thead tr") ??
                        await tableEl.QuerySelectorAsync("tr:first-child");

        if (headerRow == null) return new Dictionary<int, Party>();

        var headerCells = await headerRow.QuerySelectorAllAsync("th");
        if (headerCells.Count == 0)
            headerCells = await headerRow.QuerySelectorAllAsync("td");

        var map = new Dictionary<int, Party>();
        for (int i = 0; i < headerCells.Count; i++)
        {
            // First try: look for an <img> whose src contains a party name
            var img = await headerCells[i].QuerySelectorAsync("img");
            string? identifier = null;
            if (img != null)
            {
                var src = await img.GetAttributeAsync("src");
                if (src != null)
                {
                    // Extract filename without extension: "LPC.svg" → "LPC"
                    identifier = Path.GetFileNameWithoutExtension(src).ToUpperInvariant();
                }
            }

            // Fallback: use inner text
            identifier ??= (await headerCells[i].InnerTextAsync()).Trim().ToUpperInvariant();

            var party = identifier switch
            {
                "CPC" or "CON" or "CONSERVATIVE" or "PCC" => Party.CPC,
                "LPC" or "LIB" or "LIBERAL" or "PLC" => Party.LPC,
                "NDP" or "NPD" => Party.NDP,
                "BQ" or "BLOC" => Party.BQ,
                "GPC" or "GRN" or "GREEN" or "PVC" => Party.GPC,
                "PPC" => Party.PPC,
                _ => (Party?)null
            };
            if (party.HasValue)
                map[i] = party.Value;
        }

        return map;
    }
}
