using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using CsvHelper;
using CsvHelper.Configuration;
using ElectionSim.Core.Models;

namespace ElectionSim.DataTools.Commands;

/// <summary>
/// Transforms raw Elections Canada CSVs into JSON consumed by the simulator and web app.
/// Parses candidate results, maps historical riding IDs to current boundaries, and generates
/// ridings.json, results-{year}.json, and polling.json. See DATA.md for details.
/// </summary>
public static class ProcessCommand
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    // Known party suffixes in Elections Canada CSV "Candidate" field
    private static readonly (string Suffix, Party Party)[] PartySuffixes =
    [
        ("Liberal/Libéral", Party.LPC),
        ("Conservative/Conservateur", Party.CPC),
        ("NDP-New Democratic Party/NPD-Nouveau Parti démocratique", Party.NDP),
        ("Bloc Québécois/Bloc Québécois", Party.BQ),
        ("Bloc Québécois", Party.BQ),
        ("Green Party/Parti Vert", Party.GPC),
        ("People's Party - PPC/Parti populaire - PPC", Party.PPC),
        ("People's Party/Parti populaire", Party.PPC),
    ];

    public static async Task RunAsync(string rawDir, string processedDir, string wwwrootDataDir)
    {
        Console.WriteLine("Processing Elections Canada data...");

        // Process 2025 first to get the master riding list
        var csv2025 = Path.Combine(rawDir, "2025_results_elections_canada.csv");

        if (!File.Exists(csv2025))
        {
            Console.WriteLine("  ERROR: 2025 CSV not found. Run 'download' first or use 'generate-sample' for dev data.");
            return;
        }

        // Parse all available years
        var (ridings2025, results2025, winners2025) = ProcessElectionCsv(csv2025, 2025);
        Console.WriteLine($"  2025: {ridings2025.Count} ridings, {results2025.Count} results");

        // Collect MP data: start with 2025
        var allMps = new List<RidingMp>();
        foreach (var (ridingId, (name, party)) in winners2025)
        {
            if (name != null)
                allMps.Add(new RidingMp(ridingId, 2025, name, party));
        }

        // Build name-based lookup for riding mapping
        var nameToRidingId = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in ridings2025.Values)
            nameToRidingId.TryAdd(NormalizeName(r.Name), r.Id);

        // Historical years to process (most recent first)
        int[] historicalYears = [2021, 2019, 2015, 2011, 2008];
        var mappedResultsByYear = new Dictionary<int, List<RidingResult>>();

        foreach (var year in historicalYears)
        {
            var csvPath = Path.Combine(rawDir, $"{year}_results_elections_canada.csv");
            if (!File.Exists(csvPath))
            {
                Console.WriteLine($"  {year}: CSV not found, skipping.");
                continue;
            }

            var (ridingsForYear, resultsForYear, winnersForYear) = ProcessElectionCsv(csvPath, year);
            Console.WriteLine($"  {year}: {ridingsForYear.Count} ridings, {resultsForYear.Count} results");
            var (mapped, singleMappedIds) = MapResultsToCurrentRidings(resultsForYear, ridingsForYear, nameToRidingId, ridings2025, year);
            Console.WriteLine($"  {year} mapped: {mapped.Count} results to 2025 riding IDs");
            mappedResultsByYear[year] = mapped;

            // Emit MP entries for ridings with a name match (exact or prefix).
            // Do NOT fall back to numeric ID matching — after a redistribution the same ID
            // can refer to a completely different riding (e.g. 59016 was Langley--Aldergrove
            // in 2021 but Kelowna in 2025).
            foreach (var (oldRidingId, (name, party)) in winnersForYear)
            {
                if (name == null) continue;
                int? currentId = null;
                if (ridingsForYear.TryGetValue(oldRidingId, out var oldRiding))
                {
                    var normalized = NormalizeName(oldRiding.Name);
                    // 1) Exact name match
                    if (nameToRidingId.TryGetValue(normalized, out int matchedId))
                    {
                        currentId = matchedId;
                    }
                    else
                    {
                        // 2) Prefix match: "kelowna lake country" starts with "kelowna",
                        //    or "kelowna" starts with "kelowna lake country".
                        //    Pick the longest matching name to avoid short-prefix false positives.
                        string? bestMatch = null;
                        foreach (var currentName in nameToRidingId.Keys)
                        {
                            if (normalized.StartsWith(currentName, StringComparison.OrdinalIgnoreCase)
                                || currentName.StartsWith(normalized, StringComparison.OrdinalIgnoreCase))
                            {
                                if (bestMatch == null || currentName.Length > bestMatch.Length)
                                    bestMatch = currentName;
                            }
                        }
                        if (bestMatch != null)
                            currentId = nameToRidingId[bestMatch];
                    }
                }

                if (currentId != null && singleMappedIds.Contains(currentId.Value))
                    allMps.Add(new RidingMp(currentId.Value, year, name, party));
            }
        }

        // Generate polling.json from 2025 regional averages
        var polling = GeneratePollingFromResults(results2025, ridings2025);
        Console.WriteLine($"  Generated polling data for {polling.Count} regions");

        // Write all output files
        var ridingsList = ridings2025.Values.OrderBy(r => r.Id).ToList();

        foreach (var dir in new[] { processedDir, wwwrootDataDir })
        {
            Directory.CreateDirectory(dir);

            await File.WriteAllTextAsync(
                Path.Combine(dir, "ridings.json"),
                JsonSerializer.Serialize(ridingsList, JsonOptions));

            await File.WriteAllTextAsync(
                Path.Combine(dir, "results-2025.json"),
                JsonSerializer.Serialize(results2025, JsonOptions));

            foreach (var (year, mapped) in mappedResultsByYear)
            {
                await File.WriteAllTextAsync(
                    Path.Combine(dir, $"results-{year}.json"),
                    JsonSerializer.Serialize(mapped, JsonOptions));
            }

            await File.WriteAllTextAsync(
                Path.Combine(dir, "polling.json"),
                JsonSerializer.Serialize(polling, JsonOptions));

            await File.WriteAllTextAsync(
                Path.Combine(dir, "riding-mps.json"),
                JsonSerializer.Serialize(allMps.OrderBy(m => m.RidingId).ThenByDescending(m => m.Year), JsonOptions));
        }

        Console.WriteLine($"  Generated riding-mps.json with {allMps.Count} MP entries");

        // Copy post-election-events.json if it exists in wwwroot/data/ (hand-curated)
        var eventsSource = Path.Combine(wwwrootDataDir, "post-election-events.json");
        if (File.Exists(eventsSource))
        {
            var eventsDest = Path.Combine(processedDir, "post-election-events.json");
            File.Copy(eventsSource, eventsDest, overwrite: true);
            Console.WriteLine("  Copied post-election-events.json to processed/");
        }

        Console.WriteLine("Processing complete. Files written to processed/ and wwwroot/data/.");
    }

    private static (Dictionary<int, Riding> Ridings, List<RidingResult> Results, Dictionary<int, (string? Name, Party Party)> Winners) ProcessElectionCsv(string csvPath, int year)
    {
        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true,
            MissingFieldFound = null,
            HeaderValidated = null,
        };

        using var reader = new StreamReader(csvPath, DetectEncoding(csvPath));
        using var csv = new CsvReader(reader, config);

        var ridings = new Dictionary<int, Riding>();
        var candidatesByRiding = new Dictionary<int, List<(CandidateResult Result, string? Name)>>();

        csv.Read();
        csv.ReadHeader();

        while (csv.Read())
        {
            // Province: "Newfoundland and Labrador/Terre-Neuve-et-Labrador" → English part
            var provinceRaw = csv.GetField(0) ?? "";
            var province = provinceRaw.Split('/')[0].Trim();

            // Riding name: may be bilingual with /
            var ridingNameRaw = csv.GetField(1) ?? "";
            var ridingNameParts = SplitBilingualName(ridingNameRaw);
            var ridingName = ridingNameParts.English;
            var ridingNameFr = ridingNameParts.French;

            // Riding number
            var ridingNumStr = csv.GetField(2) ?? "0";
            if (!int.TryParse(ridingNumStr.Trim(), out int ridingId))
                continue;

            // Candidate field contains party: "Paul Connors Liberal/Libéral"
            var candidateField = csv.GetField(3) ?? "";
            var party = ExtractParty(candidateField);
            var candidateName = ExtractCandidateName(candidateField);

            // Votes
            var votesStr = csv.GetField(6) ?? "0";
            if (!int.TryParse(votesStr.Trim().Replace(",", ""), out int votes))
                votes = 0;

            var region = PartyColorProvider.GetRegionForProvince(province);

            var (lat, lng) = HexLayoutGenerator.RidingCentroids.GetValueOrDefault(ridingId, (0, 0));
            ridings.TryAdd(ridingId, new Riding(ridingId, ridingName, ridingNameFr, province, region, lat, lng));

            if (!candidatesByRiding.TryGetValue(ridingId, out var candidates))
            {
                candidates = new List<(CandidateResult, string?)>();
                candidatesByRiding[ridingId] = candidates;
            }

            // Aggregate votes by party within each riding (some ridings have multiple independents etc.)
            var existing = candidates.FindIndex(c => c.Result.Party == party);
            if (existing >= 0)
            {
                var prev = candidates[existing];
                candidates[existing] = (new CandidateResult(party, prev.Result.Votes + votes, 0), prev.Name ?? candidateName);
            }
            else
            {
                candidates.Add((new CandidateResult(party, votes, 0), candidateName));
            }
        }

        // Calculate vote shares and determine winners
        var results = new List<RidingResult>();
        var winners = new Dictionary<int, (string? Name, Party Party)>();
        foreach (var (ridingId, candidates) in candidatesByRiding)
        {
            int totalVotes = candidates.Sum(c => c.Result.Votes);
            var withShares = candidates
                .Select(c => (Result: new CandidateResult(c.Result.Party, c.Result.Votes, totalVotes > 0 ? (double)c.Result.Votes / totalVotes : 0), c.Name))
                .OrderByDescending(c => c.Result.Votes)
                .ToList();
            results.Add(new RidingResult(ridingId, year, withShares.Select(c => c.Result).ToList(), totalVotes));

            var winner = withShares.First();
            winners[ridingId] = (winner.Name, winner.Result.Party);
        }

        return (ridings, results.OrderBy(r => r.RidingId).ToList(), winners);
    }

    private static string? ExtractCandidateName(string candidateField)
    {
        // Remove incumbent marker "**" and trim
        var field = candidateField.Replace("**", "").Trim();

        // Try known suffixes to find where the name ends
        foreach (var (suffix, _) in PartySuffixes)
        {
            if (field.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                var name = field[..^suffix.Length].Trim();
                return string.IsNullOrWhiteSpace(name) ? null : name;
            }
        }

        // Fallback: try keyword-based extraction for less common formats
        var lower = field.ToLowerInvariant();
        string[] keywords = ["liberal/libéral", "conservative/conservateur", "ndp", "new democratic",
            "bloc québécois", "green party", "parti vert", "people's party", "parti populaire"];
        foreach (var kw in keywords)
        {
            var idx = lower.IndexOf(kw, StringComparison.Ordinal);
            if (idx > 0)
            {
                var name = field[..idx].Trim();
                return string.IsNullOrWhiteSpace(name) ? null : name;
            }
        }

        // Party.Other — can't reliably extract name
        return null;
    }

    private static Party ExtractParty(string candidateField)
    {
        // Remove incumbent marker "**" and trim
        var field = candidateField.Replace("**", "").Trim();

        // Try known suffixes (longest first to avoid partial matches)
        foreach (var (suffix, party) in PartySuffixes)
        {
            if (field.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                return party;
        }

        // Fallback: check for keywords anywhere in the field
        var lower = field.ToLowerInvariant();
        if (lower.Contains("liberal/libéral")) return Party.LPC;
        if (lower.Contains("conservative/conservateur")) return Party.CPC;
        if (lower.Contains("ndp") || lower.Contains("new democratic")) return Party.NDP;
        if (lower.Contains("bloc québécois")) return Party.BQ;
        if (lower.Contains("green party") || lower.Contains("parti vert")) return Party.GPC;
        if (lower.Contains("people's party") || lower.Contains("parti populaire")) return Party.PPC;

        return Party.Other;
    }

    private static (string English, string French) SplitBilingualName(string name)
    {
        // Many riding names are bilingual: "Avalon" (no slash) or "Gaspésie--Les Îles-de-la-Madeleine"
        // Some have bilingual format: "Hull--Aylmer/Hull--Aylmer"
        // Heuristic: split on "/" only if both halves look like riding names (not too short)
        var slashIdx = name.IndexOf('/');
        if (slashIdx > 0 && slashIdx < name.Length - 1)
        {
            var left = name[..slashIdx].Trim();
            var right = name[(slashIdx + 1)..].Trim();
            // Only treat as bilingual if both parts are reasonable length
            if (left.Length >= 3 && right.Length >= 3)
                return (left, right);
        }
        return (name.Trim(), name.Trim());
    }

    private static string NormalizeName(string name)
    {
        // Normalize for matching: lowercase, remove diacritics-insensitive, collapse whitespace/dashes
        return Regex.Replace(name.Trim().ToLowerInvariant(), @"[\s\-\u2013\u2014]+", " ");
    }

    private static Encoding DetectEncoding(string filePath)
    {
        // Older Elections Canada CSVs (2008, 2011) are Windows-1252/Latin-1 encoded.
        // Newer ones (2015+) use UTF-8, often with BOM.
        // Detect by checking for a UTF-8 BOM at the start of the file.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var bom = new byte[3];
        using var fs = File.OpenRead(filePath);
        fs.ReadExactly(bom, 0, 3);
        if (bom[0] == 0xEF && bom[1] == 0xBB && bom[2] == 0xBF)
            return Encoding.UTF8;

        // No BOM — check if the file is valid UTF-8 by scanning a sample
        fs.Position = 0;
        var sample = new byte[Math.Min(4096, fs.Length)];
        fs.ReadExactly(sample, 0, sample.Length);
        if (IsValidUtf8(sample))
            return Encoding.UTF8;

        return Encoding.GetEncoding(1252); // Windows-1252
    }

    private static bool IsValidUtf8(byte[] data)
    {
        for (int i = 0; i < data.Length; i++)
        {
            byte b = data[i];
            if (b < 0x80) continue;
            int expected;
            if ((b & 0xE0) == 0xC0) expected = 1;
            else if ((b & 0xF0) == 0xE0) expected = 2;
            else if ((b & 0xF8) == 0xF0) expected = 3;
            else return false; // Invalid leading byte
            if (i + expected >= data.Length) return true; // Truncated sample, assume valid
            for (int j = 1; j <= expected; j++)
            {
                if ((data[i + j] & 0xC0) != 0x80) return false;
            }
            i += expected;
        }
        return true;
    }

    private static (List<RidingResult> Results, HashSet<int> SingleMappedIds) MapResultsToCurrentRidings(
        List<RidingResult> oldResults,
        Dictionary<int, Riding> oldRidings,
        Dictionary<string, int> nameToCurrentId,
        Dictionary<int, Riding> currentRidings,
        int year)
    {
        // Map old results to current (2025) riding IDs
        // Strategy: 1) exact name match, 2) prefix name match, 3) skip
        // Do NOT fall back to numeric ID — after redistribution the same ID can be
        // a completely different riding.
        var mappedByRiding = new Dictionary<int, List<(RidingResult Result, int Weight)>>();
        int nameMatches = 0, prefixMatches = 0, skipped = 0;

        foreach (var result in oldResults)
        {
            int? targetId = null;

            if (oldRidings.TryGetValue(result.RidingId, out var oldRiding))
            {
                var normalized = NormalizeName(oldRiding.Name);
                // 1) Exact name match
                if (nameToCurrentId.TryGetValue(normalized, out int matchedId))
                {
                    targetId = matchedId;
                    nameMatches++;
                }
                else
                {
                    // 2) Prefix match (e.g. "joliette" ↔ "joliette manawan")
                    string? bestMatch = null;
                    foreach (var currentName in nameToCurrentId.Keys)
                    {
                        if (normalized.StartsWith(currentName, StringComparison.OrdinalIgnoreCase)
                            || currentName.StartsWith(normalized, StringComparison.OrdinalIgnoreCase))
                        {
                            if (bestMatch == null || currentName.Length > bestMatch.Length)
                                bestMatch = currentName;
                        }
                    }
                    if (bestMatch != null)
                    {
                        targetId = nameToCurrentId[bestMatch];
                        prefixMatches++;
                    }
                }
            }

            if (targetId == null)
            {
                skipped++;
                continue;
            }

            if (!mappedByRiding.TryGetValue(targetId.Value, out var list))
            {
                list = new List<(RidingResult, int)>();
                mappedByRiding[targetId.Value] = list;
            }
            list.Add((result, result.TotalVotes));
        }

        Console.WriteLine($"    Mapping: {nameMatches} name matches, {prefixMatches} prefix matches, {skipped} skipped");

        // Track which current riding IDs have a 1:1 mapping (riding existed in its current form)
        var singleMappedIds = new HashSet<int>();

        // Build final results: if multiple old ridings map to one new riding, average vote shares weighted by total votes
        var finalResults = new List<RidingResult>();
        foreach (var (ridingId, mappings) in mappedByRiding)
        {
            if (mappings.Count == 1)
            {
                var r = mappings[0].Result;
                finalResults.Add(new RidingResult(ridingId, year, r.Candidates, r.TotalVotes));
                singleMappedIds.Add(ridingId);
            }
            else
            {
                // Weighted average of vote shares
                int totalWeight = mappings.Sum(m => m.Weight);
                var partyShares = new Dictionary<Party, double>();
                var partyVotes = new Dictionary<Party, int>();

                foreach (var (result, weight) in mappings)
                {
                    foreach (var c in result.Candidates)
                    {
                        partyShares.TryGetValue(c.Party, out double existingShare);
                        partyShares[c.Party] = existingShare + c.VoteShare * weight;

                        partyVotes.TryGetValue(c.Party, out int existingVotes);
                        partyVotes[c.Party] = existingVotes + c.Votes;
                    }
                }

                var candidates = partyShares
                    .Select(kv => new CandidateResult(
                        kv.Key,
                        partyVotes.GetValueOrDefault(kv.Key, 0),
                        totalWeight > 0 ? kv.Value / totalWeight : 0))
                    .OrderByDescending(c => c.VoteShare)
                    .ToList();

                finalResults.Add(new RidingResult(ridingId, year, candidates, totalWeight));
            }
        }

        return (finalResults.OrderBy(r => r.RidingId).ToList(), singleMappedIds);
    }

    private static List<RegionalPoll> GeneratePollingFromResults(
        List<RidingResult> results,
        Dictionary<int, Riding> ridings)
    {
        // Compute regional average vote shares from election results
        var regionVotes = new Dictionary<Region, Dictionary<Party, int>>();
        var regionTotals = new Dictionary<Region, int>();

        foreach (var result in results)
        {
            if (!ridings.TryGetValue(result.RidingId, out var riding))
                continue;

            var region = riding.Region;
            if (!regionVotes.TryGetValue(region, out var partyVotes))
            {
                partyVotes = new Dictionary<Party, int>();
                regionVotes[region] = partyVotes;
                regionTotals[region] = 0;
            }

            foreach (var c in result.Candidates)
            {
                partyVotes.TryGetValue(c.Party, out int existing);
                partyVotes[c.Party] = existing + c.Votes;
            }
            regionTotals[region] += result.TotalVotes;
        }

        var polls = new List<RegionalPoll>();
        foreach (var (region, partyVotes) in regionVotes.OrderBy(kv => kv.Key))
        {
            int total = regionTotals[region];
            var shares = new Dictionary<Party, double>();
            foreach (var (party, votes) in partyVotes)
            {
                shares[party] = total > 0 ? Math.Round((double)votes / total, 4) : 0;
            }
            polls.Add(new RegionalPoll(region, shares));
        }

        return polls;
    }
}
