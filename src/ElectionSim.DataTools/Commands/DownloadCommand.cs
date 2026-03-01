namespace ElectionSim.DataTools.Commands;

/// <summary>
/// Downloads Elections Canada CSV result files to data/raw/. Skips files that already exist.
/// </summary>
public static class DownloadCommand
{
    public static async Task RunAsync(string rawDir)
    {
        Console.WriteLine("Downloading Elections Canada data...");

        using var http = new HttpClient();
        http.DefaultRequestHeaders.UserAgent.ParseAdd("ElectionSim DataTools/1.0");

        // Elections Canada provides official results as CSV
        // 2025 results (45th general election)
        // Note: Update URL when official data is published
        var downloads = new Dictionary<string, string>
        {
            ["results-2025.csv"] = "https://www.elections.ca/res/rep/off/ovr2025app/GE45-data_donnees.zip",
            ["results-2021.csv"] = "https://www.elections.ca/res/rep/off/ovr2021app/53/data_donnees/table_tableau11.csv",
        };

        foreach (var (filename, url) in downloads)
        {
            var path = Path.Combine(rawDir, filename);
            if (File.Exists(path))
            {
                Console.WriteLine($"  {filename} already exists, skipping.");
                continue;
            }

            try
            {
                Console.WriteLine($"  Downloading {filename}...");
                var data = await http.GetByteArrayAsync(url);
                await File.WriteAllBytesAsync(path, data);
                Console.WriteLine($"  Saved {filename} ({data.Length:N0} bytes)");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  Failed to download {filename}: {ex.Message}");
                Console.WriteLine($"  You can manually download from: {url}");
                Console.WriteLine($"  Place the file at: {path}");
            }
        }

        Console.WriteLine("Download step complete.");
    }
}
