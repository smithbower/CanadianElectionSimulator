using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CsvHelper;
using CsvHelper.Configuration;
using ElectionSim.Core.Models;

namespace ElectionSim.DataTools.Commands;

/// <summary>
/// Downloads and processes 2021 Census Profile data for 343 federal electoral districts.
/// Extracts 9 demographic variables, normalizes values, and outputs demographics.json.
/// </summary>
public static class DemographicsCommand
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    // StatsCan 2021 Census Profile for FEDs (2023 Representation Order, 343 ridings)
    private const string CensusDownloadUrl =
        "https://www12.statcan.gc.ca/census-recensement/2021/dp-pd/prof/details/download-telecharger/comp/GetFile.cfm?Lang=E&FILETYPE=CSV&GEONO=029";

    // CHARACTERISTIC_ID values from the 2021 Census Profile (98-401-X2021029).
    // Using stable IDs rather than name matching to avoid substring collisions
    // (e.g., "Non-immigrants" contains "Immigrants").
    private static readonly Dictionary<int, string> CharacteristicIds = new()
    {
        [113]  = "MedianIncome",       // Median total income in 2020 among recipients ($)
        [40]   = "MedianAge",          // Median age of the population
        [8]    = "TotalPopulation",    // Total - Age groups of the population - 100% data
        [41]   = "DwellingTotal",      // Total - Occupied private dwellings by structural type
        [42]   = "SingleDetached",     // Single-detached house
        [2014] = "UnivTotal",          // Total - Highest certificate... aged 25 to 64 years
        [2024] = "UnivBachelorsPlus",  // Bachelor's degree or higher (25-64)
        [1683] = "VisMinTotal",        // Total - Visible minority for pop in private households
        [1684] = "VisMinMinority",     // Total visible minority population
        [1527] = "ImmigrantTotal",     // Total - Immigrant status and period of immigration
        [1529] = "Immigrants",         // Immigrants (not non-immigrants)
        [388]  = "FolTotal",           // Total - First official language spoken
        [390]  = "FolFrench",          // French (first official language)
        [1402] = "IndigenousTotal",    // Total - Indigenous identity for pop in private households
        [1403] = "IndigenousIdentity", // Indigenous identity
        [1414] = "TenureTotal",        // Total - Private households by tenure
        [1415] = "TenureOwner",        // Owner
    };

    public static async Task RunAsync(string rawDir, string processedDir, string wwwrootDataDir)
    {
        Console.WriteLine("=== Demographics Pipeline ===");
        Console.WriteLine();

        // Step 1: Download census CSV if not present
        var csvPath = Path.Combine(rawDir, "census-2021-fed-2023ro.csv");
        if (!File.Exists(csvPath))
        {
            csvPath = await DownloadCensusData(rawDir);
            if (csvPath == null) return;
        }
        else
        {
            Console.WriteLine($"  Census CSV already exists: {Path.GetFileName(csvPath)}");
        }

        // Step 2: Load riding IDs from processed ridings.json
        var ridingsPath = Path.Combine(processedDir, "ridings.json");
        if (!File.Exists(ridingsPath))
        {
            Console.WriteLine("  ERROR: ridings.json not found. Run 'process' first.");
            return;
        }
        var ridingsJson = await File.ReadAllTextAsync(ridingsPath);
        var ridings = JsonSerializer.Deserialize<List<Riding>>(ridingsJson, JsonOptions);
        if (ridings == null || ridings.Count == 0)
        {
            Console.WriteLine("  ERROR: Could not parse ridings.json");
            return;
        }
        Console.WriteLine($"  Loaded {ridings.Count} ridings from ridings.json");

        // Step 3: Parse census CSV and extract demographics
        Console.WriteLine("  Parsing census CSV...");
        var rawData = ParseCensusCsv(csvPath, ridings);
        Console.WriteLine($"  Extracted data for {rawData.Count} ridings");

        if (rawData.Count == 0)
        {
            Console.WriteLine("  ERROR: No demographic data extracted. Check CSV format.");
            return;
        }

        // Step 4: Normalize and build RidingDemographics records
        var demographics = NormalizeData(rawData, ridings);
        Console.WriteLine($"  Normalized demographics for {demographics.Count} ridings");

        // Step 5: Write output
        var json = JsonSerializer.Serialize(demographics, JsonOptions);

        var processedPath = Path.Combine(processedDir, "demographics.json");
        await File.WriteAllTextAsync(processedPath, json);
        Console.WriteLine($"  Wrote {processedPath}");

        var wwwrootPath = Path.Combine(wwwrootDataDir, "demographics.json");
        await File.WriteAllTextAsync(wwwrootPath, json);
        Console.WriteLine($"  Wrote {wwwrootPath}");

        // Print sample
        Console.WriteLine();
        Console.WriteLine("  Sample demographics (first 5 ridings):");
        foreach (var d in demographics.Take(5))
        {
            var riding = ridings.First(r => r.Id == d.RidingId);
            Console.WriteLine($"    {riding.Name}: income={d.MedianIncome:F2} edu={d.PctUniversityEducated:F2} " +
                              $"vismin={d.PctVisibleMinority:F2} immig={d.PctImmigrant:F2} " +
                              $"franco={d.PctFrancophone:F2} singledet={d.PctSingleDetached:F2} " +
                              $"age={d.MedianAge:F2} indig={d.PctIndigenous:F2} owner={d.PctHomeowner:F2}");
        }

        Console.WriteLine();
        Console.WriteLine("Demographics pipeline complete.");
    }

    private static async Task<string?> DownloadCensusData(string rawDir)
    {
        Console.WriteLine("  Downloading 2021 Census Profile (FED 2023 RO)...");
        Console.WriteLine($"  URL: {CensusDownloadUrl}");

        using var http = new HttpClient();
        http.DefaultRequestHeaders.UserAgent.ParseAdd("ElectionSim DataTools/1.0");
        http.Timeout = TimeSpan.FromMinutes(5);

        try
        {
            var response = await http.GetAsync(CensusDownloadUrl);
            response.EnsureSuccessStatusCode();

            var contentType = response.Content.Headers.ContentType?.MediaType ?? "";
            var bytes = await response.Content.ReadAsByteArrayAsync();
            Console.WriteLine($"  Downloaded {bytes.Length:N0} bytes (content-type: {contentType})");

            // StatsCan may return a ZIP file or direct CSV
            if (contentType.Contains("zip") || bytes.Length > 4 && bytes[0] == 'P' && bytes[1] == 'K')
            {
                var zipPath = Path.Combine(rawDir, "census-2021-fed-2023ro.zip");
                await File.WriteAllBytesAsync(zipPath, bytes);

                // Extract CSV from ZIP
                using var zip = ZipFile.OpenRead(zipPath);
                var csvEntry = zip.Entries.FirstOrDefault(e =>
                    e.Name.EndsWith(".csv", StringComparison.OrdinalIgnoreCase));

                if (csvEntry == null)
                {
                    Console.WriteLine("  ERROR: No CSV found in ZIP archive.");
                    return null;
                }

                var csvPath = Path.Combine(rawDir, "census-2021-fed-2023ro.csv");
                csvEntry.ExtractToFile(csvPath, overwrite: true);
                Console.WriteLine($"  Extracted {csvEntry.Name} -> {Path.GetFileName(csvPath)}");
                return csvPath;
            }
            else
            {
                var csvPath = Path.Combine(rawDir, "census-2021-fed-2023ro.csv");
                await File.WriteAllBytesAsync(csvPath, bytes);
                Console.WriteLine($"  Saved as {Path.GetFileName(csvPath)}");
                return csvPath;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Failed to download: {ex.Message}");
            Console.WriteLine($"  You can manually download from:");
            Console.WriteLine($"    https://www12.statcan.gc.ca/census-recensement/2021/dp-pd/prof/details/download-telecharger.cfm");
            Console.WriteLine($"  Select: CSV, Federal electoral districts (2023 Representation Order)");
            Console.WriteLine($"  Place the CSV at: {Path.Combine(rawDir, "census-2021-fed-2023ro.csv")}");
            return null;
        }
    }

    private record RawRidingData
    {
        public int RidingId { get; init; }
        public string GeoName { get; init; } = "";
        public Dictionary<string, double> Values { get; init; } = new();
    }

    private static List<RawRidingData> ParseCensusCsv(string csvPath, List<Riding> ridings)
    {
        // The Census Profile CSV format has one row per (geography, characteristic).
        // Key columns: DGUID, GEO_NAME, CHARACTERISTIC_NAME, C1_COUNT_TOTAL
        // FED DGUIDs for 2023 RO start with "2023A0004"

        var ridingIdSet = new HashSet<int>(ridings.Select(r => r.Id));
        var data = new Dictionary<int, RawRidingData>();

        // Try different encodings - StatsCan CSVs sometimes use UTF-8 BOM
        Encoding encoding;
        var preamble = File.ReadAllBytes(csvPath).Take(3).ToArray();
        if (preamble.Length >= 3 && preamble[0] == 0xEF && preamble[1] == 0xBB && preamble[2] == 0xBF)
            encoding = new UTF8Encoding(true);
        else
            encoding = Encoding.UTF8;

        using var reader = new StreamReader(csvPath, encoding);
        using var csv = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true,
            MissingFieldFound = null,
            BadDataFound = null,
        });

        csv.Read();
        csv.ReadHeader();

        // Detect column names (StatsCan uses various naming conventions)
        var headers = csv.HeaderRecord ?? [];
        var dguidCol = FindColumn(headers, "DGUID", "dguid");
        var geoNameCol = FindColumn(headers, "GEO_NAME", "geo_name", "Geographic name");
        var charIdCol = FindColumn(headers, "CHARACTERISTIC_ID", "characteristic_id");
        var countCol = FindColumn(headers, "C1_COUNT_TOTAL", "c1_count_total", "Total");
        var altGeoCol = FindColumn(headers, "ALT_GEO_CODE", "alt_geo_code");

        if (charIdCol == null || countCol == null)
        {
            Console.WriteLine($"  WARNING: Could not find required columns. Available: {string.Join(", ", headers)}");
            return [];
        }

        int rowCount = 0;
        int matchedRows = 0;

        while (csv.Read())
        {
            rowCount++;

            // Identify the riding from DGUID or ALT_GEO_CODE
            int ridingId = 0;

            if (dguidCol != null)
            {
                var dguid = csv.GetField(dguidCol) ?? "";
                // DGUID format: 2023A000410001 — last 5 digits are the FED code
                if (dguid.StartsWith("2023A0004") && dguid.Length >= 14)
                {
                    if (int.TryParse(dguid[9..], out var fedCode))
                        ridingId = fedCode;
                }
            }

            if (ridingId == 0 && altGeoCol != null)
            {
                var altGeo = csv.GetField(altGeoCol) ?? "";
                int.TryParse(altGeo, out ridingId);
            }

            if (ridingId == 0 || !ridingIdSet.Contains(ridingId))
                continue;

            // Match by CHARACTERISTIC_ID (stable, avoids substring collisions)
            var charIdStr = csv.GetField(charIdCol) ?? "";
            if (!int.TryParse(charIdStr, out var charId))
                continue;

            if (!CharacteristicIds.TryGetValue(charId, out var key))
                continue;

            var countStr = (csv.GetField(countCol) ?? "").Replace(",", "").Trim();
            if (!double.TryParse(countStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var value))
                continue;

            if (!data.ContainsKey(ridingId))
            {
                var geoName = geoNameCol != null ? (csv.GetField(geoNameCol) ?? "") : "";
                data[ridingId] = new RawRidingData { RidingId = ridingId, GeoName = geoName };
            }

            data[ridingId].Values.TryAdd(key, value);
            matchedRows++;
        }

        Console.WriteLine($"  Parsed {rowCount:N0} rows, matched {matchedRows:N0} characteristic values across {data.Count} ridings");
        return data.Values.ToList();
    }

    private static string? FindColumn(string[] headers, params string[] candidates)
    {
        foreach (var candidate in candidates)
        {
            var match = headers.FirstOrDefault(h =>
                h.Equals(candidate, StringComparison.OrdinalIgnoreCase));
            if (match != null) return match;
        }
        return null;
    }

    private static List<RidingDemographics> NormalizeData(List<RawRidingData> rawData, List<Riding> ridings)
    {
        // Extract raw values, compute proportions
        var records = new List<(int RidingId, double Income, double Edu, double VisMn, double Immig,
            double Franco, double SingleDet, double Age, double Indig, double Owner)>();

        foreach (var raw in rawData)
        {
            var v = raw.Values;

            double income = v.GetValueOrDefault("MedianIncome", 0);
            double age = v.GetValueOrDefault("MedianAge", 0);

            // Proportions
            double eduTotal = v.GetValueOrDefault("UnivTotal", 0);
            double eduBach = v.GetValueOrDefault("UnivBachelorsPlus", 0);
            double edu = eduTotal > 0 ? eduBach / eduTotal : 0;

            double visMinTotal = v.GetValueOrDefault("VisMinTotal", 0);
            double visMinCount = v.GetValueOrDefault("VisMinMinority", 0);
            double visMn = visMinTotal > 0 ? visMinCount / visMinTotal : 0;

            double immigTotal = v.GetValueOrDefault("ImmigrantTotal", 0);
            double immigCount = v.GetValueOrDefault("Immigrants", 0);
            double immig = immigTotal > 0 ? immigCount / immigTotal : 0;

            double folTotal = v.GetValueOrDefault("FolTotal", 0);
            double folFrench = v.GetValueOrDefault("FolFrench", 0);
            double franco = folTotal > 0 ? folFrench / folTotal : 0;

            // Single-detached % as urbanization proxy (high = rural, low = urban)
            double dwellingTotal = v.GetValueOrDefault("DwellingTotal", 0);
            double singleDet = v.GetValueOrDefault("SingleDetached", 0);
            double singleDetPct = dwellingTotal > 0 ? singleDet / dwellingTotal : 0;

            double indigTotal = v.GetValueOrDefault("IndigenousTotal", 0);
            double indigCount = v.GetValueOrDefault("IndigenousIdentity", 0);
            double indig = indigTotal > 0 ? indigCount / indigTotal : 0;

            double tenureTotal = v.GetValueOrDefault("TenureTotal", 0);
            double tenureOwner = v.GetValueOrDefault("TenureOwner", 0);
            double owner = tenureTotal > 0 ? tenureOwner / tenureTotal : 0;

            records.Add((raw.RidingId, income, edu, visMn, immig, franco, singleDetPct, age, indig, owner));
        }

        if (records.Count == 0) return [];

        // Compute min/max for normalization of non-proportion fields
        double minIncome = records.Min(r => r.Income);
        double maxIncome = records.Max(r => r.Income);
        double minAge = records.Min(r => r.Age);
        double maxAge = records.Max(r => r.Age);

        var demographics = new List<RidingDemographics>();
        foreach (var r in records)
        {
            demographics.Add(new RidingDemographics(
                RidingId: r.RidingId,
                MedianIncome: MinMaxNorm(r.Income, minIncome, maxIncome),
                PctUniversityEducated: r.Edu,
                PctVisibleMinority: r.VisMn,
                PctImmigrant: r.Immig,
                PctFrancophone: r.Franco,
                PctSingleDetached: r.SingleDet,
                MedianAge: MinMaxNorm(r.Age, minAge, maxAge),
                PctIndigenous: r.Indig,
                PctHomeowner: r.Owner
            ));
        }

        // Fill in any ridings that are missing from census data with median values
        var existingIds = new HashSet<int>(demographics.Select(d => d.RidingId));
        var medianIncome = Median(demographics.Select(d => d.MedianIncome));
        var medianEdu = Median(demographics.Select(d => d.PctUniversityEducated));
        var medianVisMn = Median(demographics.Select(d => d.PctVisibleMinority));
        var medianImmig = Median(demographics.Select(d => d.PctImmigrant));
        var medianFranco = Median(demographics.Select(d => d.PctFrancophone));
        var medianSingleDet = Median(demographics.Select(d => d.PctSingleDetached));
        var medianAge = Median(demographics.Select(d => d.MedianAge));
        var medianIndig = Median(demographics.Select(d => d.PctIndigenous));
        var medianOwner = Median(demographics.Select(d => d.PctHomeowner));

        foreach (var riding in ridings)
        {
            if (!existingIds.Contains(riding.Id))
            {
                demographics.Add(new RidingDemographics(
                    riding.Id, medianIncome, medianEdu, medianVisMn, medianImmig,
                    medianFranco, medianSingleDet, medianAge, medianIndig, medianOwner));
            }
        }

        return demographics.OrderBy(d => d.RidingId).ToList();
    }

    private static double MinMaxNorm(double value, double min, double max)
    {
        if (max <= min) return 0.5;
        return (value - min) / (max - min);
    }

    private static double Median(IEnumerable<double> values)
    {
        var sorted = values.OrderBy(v => v).ToList();
        if (sorted.Count == 0) return 0;
        int mid = sorted.Count / 2;
        return sorted.Count % 2 == 0 ? (sorted[mid - 1] + sorted[mid]) / 2.0 : sorted[mid];
    }
}
