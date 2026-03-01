using AngleSharp;
using AngleSharp.Html.Dom;

namespace ElectionSim.DataTools.Commands;

/// <summary>
/// Scrapes CBC Poll Tracker page and saves raw HTML to data/raw/.
/// Stub implementation -- saves the page but does not parse structured data.
/// </summary>
public static class ScrapeCbcCommand
{
    public static async Task RunAsync(string rawDir, string processedDir)
    {
        Console.WriteLine("Scraping CBC Poll Tracker data...");

        try
        {
            var config = Configuration.Default.WithDefaultLoader();
            var context = BrowsingContext.New(config);

            var document = await context.OpenAsync("https://newsinteractives.cbc.ca/elections/poll-tracker/canada/");
            if (document is not IHtmlDocument htmlDoc)
            {
                Console.WriteLine("  Failed to load CBC Poll Tracker page.");
                return;
            }

            // Save raw HTML for debugging
            var rawPath = Path.Combine(rawDir, "cbc-poll-tracker.html");
            await File.WriteAllTextAsync(rawPath, document.Source.Text);
            Console.WriteLine($"  Saved raw HTML to {rawPath}");

            // CBC typically embeds polling data as JSON in script tags
            var scripts = document.QuerySelectorAll("script");
            foreach (var script in scripts)
            {
                var text = script.TextContent;
                if (text.Contains("pollData") || text.Contains("poll_data") || text.Contains("regionData"))
                {
                    var dataPath = Path.Combine(rawDir, "cbc-poll-data.json");
                    await File.WriteAllTextAsync(dataPath, text);
                    Console.WriteLine($"  Found embedded data, saved to {dataPath}");
                    break;
                }
            }

            Console.WriteLine("  Note: CBC scraping requires format-specific parsing.");
            Console.WriteLine("  Use 'generate-sample' command for development data.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Scrape failed: {ex.Message}");
            Console.WriteLine("  Use 'generate-sample' command for development data.");
        }
    }
}
