using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using ElectionSim.Core.Models;
using Microsoft.Extensions.Logging;

namespace ElectionSim.DailyRunner.ApiClient;

/// <summary>
/// HTTP client for the ElectionSim.Server simulation API. Posts weighted polling data
/// to trigger a background simulation run.
/// </summary>
public class SimulationApiClient(ILogger<SimulationApiClient> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        WriteIndented = true
    };

    public async Task<bool> RunSimulationAsync(
        string apiUrl,
        string apiKey,
        Dictionary<Region, Dictionary<Party, double>> polling,
        Dictionary<Party, double> partyUncertainty)
    {
        var endpoint = $"{apiUrl.TrimEnd('/')}/api/simulation/run";
        logger.LogInformation("Calling simulation API at {Endpoint}", endpoint);

        using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        httpClient.DefaultRequestHeaders.Add("X-Api-Key", apiKey);

        var request = new SimulationApiRequest
        {
            BaselineYear = 2025,
            NumSimulations = 1_000_000,
            Polling = polling,
            PartyUncertainty = partyUncertainty
        };

        var response = await httpClient.PostAsJsonAsync(endpoint, request, JsonOptions);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            logger.LogError("API call failed with status {Status}: {Body}",
                response.StatusCode, body);
            return false;
        }

        logger.LogInformation("Simulation request accepted (HTTP {Status}). The simulation will run in the background.",
            (int)response.StatusCode);
        return true;
    }
}

/// <summary>
/// Matches the SimulationRequest record in ElectionSim.Server.Services.SimulationService.
/// We define our own class here to avoid depending on the Server project.
/// </summary>
internal class SimulationApiRequest
{
    public int? BaselineYear { get; init; }
    public int? NumSimulations { get; init; }
    public double? NationalSigma { get; init; }
    public double? RegionalSigma { get; init; }
    public double? RidingSigma { get; init; }
    public int? Seed { get; init; }
    public Dictionary<Region, Dictionary<Party, double>>? Polling { get; init; }
    public Dictionary<Party, double>? PartyUncertainty { get; init; }
}
