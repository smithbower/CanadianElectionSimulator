namespace ElectionSim.Core.Models;

/// <summary>
/// Maps parties to their display colors, full names, short names, and provides
/// province-to-region lookups. Central source of truth for party presentation.
/// </summary>
public static class PartyColorProvider
{
    private readonly record struct PartyInfo(string Color, string Name, string ShortName);

    private static readonly PartyInfo DefaultInfo = new("#888888", "Other", "Oth");

    private static readonly Dictionary<Party, PartyInfo> PartyInfoMap = new()
    {
        [Party.LPC] = new("#D71920", "Liberal", "LPC"),
        [Party.CPC] = new("#1A4782", "Conservative", "CPC"),
        [Party.NDP] = new("#F58220", "NDP", "NDP"),
        [Party.BQ] = new("#33B2CC", "Bloc Québécois", "BQ"),
        [Party.GPC] = new("#3D9B35", "Green", "GPC"),
        [Party.PPC] = new("#662D91", "PPC", "PPC"),
    };

    public static string GetColor(Party party) =>
        PartyInfoMap.TryGetValue(party, out var info) ? info.Color : DefaultInfo.Color;

    public static string GetName(Party party) =>
        PartyInfoMap.TryGetValue(party, out var info) ? info.Name : DefaultInfo.Name;

    public static string GetShortName(Party party) =>
        PartyInfoMap.TryGetValue(party, out var info) ? info.ShortName : DefaultInfo.ShortName;

    public static readonly IReadOnlyList<Party> MainParties =
        [Party.LPC, Party.CPC, Party.NDP, Party.BQ, Party.GPC, Party.PPC];

    public static string GetProvinceRegionName(Region region) => region switch
    {
        Region.Atlantic => "Atlantic Canada",
        Region.Quebec => "Quebec",
        Region.Ontario => "Ontario",
        Region.Prairies => "Prairies",
        Region.Alberta => "Alberta",
        Region.BritishColumbia => "British Columbia",
        Region.North => "Northern Canada",
        _ => "Unknown"
    };

    public static Region GetRegionForProvince(string province) => province switch
    {
        "Newfoundland and Labrador" or "NL" => Region.Atlantic,
        "Prince Edward Island" or "PE" => Region.Atlantic,
        "Nova Scotia" or "NS" => Region.Atlantic,
        "New Brunswick" or "NB" => Region.Atlantic,
        "Quebec" or "QC" => Region.Quebec,
        "Ontario" or "ON" => Region.Ontario,
        "Manitoba" or "MB" => Region.Prairies,
        "Saskatchewan" or "SK" => Region.Prairies,
        "Alberta" or "AB" => Region.Alberta,
        "British Columbia" or "BC" => Region.BritishColumbia,
        "Yukon" or "YT" => Region.North,
        "Northwest Territories" or "NT" => Region.North,
        "Nunavut" or "NU" => Region.North,
        _ => Region.Ontario
    };
}
