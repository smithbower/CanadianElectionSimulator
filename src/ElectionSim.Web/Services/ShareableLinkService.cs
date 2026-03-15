using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ElectionSim.Core.Models;

namespace ElectionSim.Web.Services;

/// <summary>
/// Encodes and decodes simulation state into a compact URL query parameter for shareable links.
/// Pipeline: JSON (short keys) → DEFLATE compress → base64url encode → ?s={result}
/// </summary>
public static class ShareableLinkService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>
    /// Generates a shareable URL encoding all simulation parameters.
    /// </summary>
    public static string GenerateShareableUrl(
        SimulationState state,
        string baseUri)
    {
        var payload = BuildPayload(state);
        var json = JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions);
        var compressed = Compress(json);
        var encoded = Base64UrlEncode(compressed);

        var separator = baseUri.Contains('?') ? "&" : "?";
        return $"{baseUri.TrimEnd('/')}{separator}s={encoded}";
    }

    /// <summary>
    /// Attempts to parse a shareable link parameter and apply it to simulation state.
    /// Returns true if the parameter was valid and state was updated.
    /// </summary>
    public static bool TryApply(string encodedParam, SimulationState state)
    {
        try
        {
            var compressed = Base64UrlDecode(encodedParam);
            var json = Decompress(compressed);
            var payload = JsonSerializer.Deserialize<SharePayload>(json, JsonOptions);
            if (payload == null) return false;

            ApplyPayload(payload, state);
            return true;
        }
        catch (Exception ex) when (ex is FormatException or InvalidDataException or JsonException or InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>
    /// Extracts the "s" query parameter from a full URI, if present.
    /// </summary>
    public static string? ExtractShareParam(string uri)
    {
        var queryStart = uri.IndexOf('?');
        if (queryStart < 0) return null;

        var query = uri[(queryStart + 1)..];
        foreach (var pair in query.Split('&'))
        {
            var eqIndex = pair.IndexOf('=');
            if (eqIndex < 0) continue;
            var key = pair[..eqIndex];
            if (key == "s")
                return Uri.UnescapeDataString(pair[(eqIndex + 1)..]);
        }
        return null;
    }

    private static SharePayload BuildPayload(SimulationState state)
    {
        var polling = new Dictionary<string, Dictionary<string, double>>();
        foreach (var (region, shares) in state.CurrentPolling)
        {
            var regionShares = new Dictionary<string, double>();
            foreach (var (party, value) in shares)
            {
                regionShares[party.ToString()] = Math.Round(value, 6);
            }
            polling[((int)region).ToString()] = regionShares;
        }

        var uncertainty = new Dictionary<string, double>();
        foreach (var (party, value) in state.PartyUncertainty)
        {
            uncertainty[party.ToString()] = Math.Round(value, 6);
        }

        return new SharePayload
        {
            V = 1,
            B = state.BaselineYear,
            P = polling,
            U = uncertainty,
            C = new ConfigPayload
            {
                N = state.Config.NumSimulations,
                Ns = state.Config.NationalSigma,
                Rs = state.Config.RegionalSigma,
                Ds = state.Config.RidingSigma,
                Df = state.Config.DegreesOfFreedom,
                Sb = state.Config.SwingBlendAlpha,
                Cn = state.Config.UseCorrelatedNoise,
                Dp = state.Config.UseDemographicPrior,
                Dw = state.Config.DemographicBlendWeight,
                Bw = state.Config.ByElectionBlendWeight,
            }
        };
    }

    private static void ApplyPayload(SharePayload payload, SimulationState state)
    {
        // Apply baseline year
        state.BaselineYear = payload.B;

        // Apply polling data
        var polling = new Dictionary<Region, Dictionary<Party, double>>();
        foreach (var (regionKey, shares) in payload.P)
        {
            if (!int.TryParse(regionKey, out var regionInt)) continue;
            var region = (Region)regionInt;
            var partyShares = new Dictionary<Party, double>();
            foreach (var (partyKey, value) in shares)
            {
                if (Enum.TryParse<Party>(partyKey, out var party))
                {
                    partyShares[party] = value;
                }
            }
            polling[region] = partyShares;
        }
        state.SetPolling(polling);

        // Apply uncertainty
        var uncertainty = new Dictionary<Party, double>();
        foreach (var (partyKey, value) in payload.U)
        {
            if (Enum.TryParse<Party>(partyKey, out var party))
            {
                uncertainty[party] = value;
            }
        }
        state.PartyUncertainty = uncertainty;

        // Apply config
        var c = payload.C;
        state.Config = new SimulationConfig(
            NumSimulations: c.N,
            NationalSigma: c.Ns,
            RegionalSigma: c.Rs,
            RidingSigma: c.Ds,
            DegreesOfFreedom: c.Df,
            SwingBlendAlpha: c.Sb,
            UseCorrelatedNoise: c.Cn,
            UseDemographicPrior: c.Dp,
            DemographicBlendWeight: c.Dw,
            ByElectionBlendWeight: c.Bw
        );
    }

    private static byte[] Compress(byte[] data)
    {
        using var output = new MemoryStream();
        using (var deflate = new DeflateStream(output, CompressionLevel.Optimal))
        {
            deflate.Write(data, 0, data.Length);
        }
        return output.ToArray();
    }

    private static byte[] Decompress(byte[] data)
    {
        using var input = new MemoryStream(data);
        using var deflate = new DeflateStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        deflate.CopyTo(output);
        return output.ToArray();
    }

    private static string Base64UrlEncode(byte[] data)
    {
        return Convert.ToBase64String(data)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }

    private static byte[] Base64UrlDecode(string encoded)
    {
        var base64 = encoded.Replace('-', '+').Replace('_', '/');
        switch (base64.Length % 4)
        {
            case 2: base64 += "=="; break;
            case 3: base64 += "="; break;
        }
        return Convert.FromBase64String(base64);
    }

    // Compact JSON payload types with short property names to minimize URL size.

    private class SharePayload
    {
        [JsonPropertyName("v")] public int V { get; set; }
        [JsonPropertyName("b")] public int B { get; set; }
        [JsonPropertyName("p")] public Dictionary<string, Dictionary<string, double>> P { get; set; } = new();
        [JsonPropertyName("u")] public Dictionary<string, double> U { get; set; } = new();
        [JsonPropertyName("c")] public ConfigPayload C { get; set; } = new();
    }

    private class ConfigPayload
    {
        [JsonPropertyName("n")] public int N { get; set; } = 10_000;
        [JsonPropertyName("ns")] public double Ns { get; set; } = 0.06;
        [JsonPropertyName("rs")] public double Rs { get; set; } = 0.026;
        [JsonPropertyName("ds")] public double Ds { get; set; } = 0.065;
        [JsonPropertyName("df")] public double? Df { get; set; }
        [JsonPropertyName("sb")] public double Sb { get; set; }
        [JsonPropertyName("cn")] public bool Cn { get; set; } = true;
        [JsonPropertyName("dp")] public bool Dp { get; set; }
        [JsonPropertyName("dw")] public double Dw { get; set; } = 0.02;
        [JsonPropertyName("bw")] public double Bw { get; set; } = 0.3;
    }
}
