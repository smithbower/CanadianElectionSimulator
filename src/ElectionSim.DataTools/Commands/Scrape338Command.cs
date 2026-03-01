using AngleSharp;
using AngleSharp.Html.Dom;

namespace ElectionSim.DataTools.Commands;

/// <summary>
/// Scrapes 338Canada polling projection page and saves raw HTML to data/raw/.
/// Stub implementation -- saves the page but does not parse structured data.
/// </summary>
public static class Scrape338Command
{
    public static async Task RunAsync(string rawDir, string processedDir)
    {
        Console.WriteLine("Scraping 338Canada data...");

        try
        {
            var config = Configuration.Default.WithDefaultLoader();
            var context = BrowsingContext.New(config);

            var document = await context.OpenAsync("https://338canada.com/federal.htm");
            if (document is not IHtmlDocument htmlDoc)
            {
                Console.WriteLine("  Failed to load 338Canada page.");
                return;
            }

            // Save raw HTML for debugging
            var rawPath = Path.Combine(rawDir, "338canada-federal.html");
            await File.WriteAllTextAsync(rawPath, document.Source.Text);
            Console.WriteLine($"  Saved raw HTML to {rawPath}");

            // 338Canada typically presents projection data in tables
            // Extract what we can - format may change
            Console.WriteLine("  Note: 338Canada scraping requires format-specific parsing.");
            Console.WriteLine("  Use 'generate-sample' command for development data.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Scrape failed: {ex.Message}");
            Console.WriteLine("  This is expected if the site format has changed.");
            Console.WriteLine("  Use 'generate-sample' command for development data.");
        }
    }
}
