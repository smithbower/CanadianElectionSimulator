using System.Threading.Channels;

namespace ElectionSim.Server.Services;

/// <summary>
/// Bounded channel (capacity=1) that decouples API request handling from simulation execution.
/// When a new request arrives while one is already queued, DropOldest discards the stale
/// request so only the latest polling data gets simulated.
/// </summary>
public class SimulationQueue
{
    private readonly Channel<SimulationRequest> _channel = Channel.CreateBounded<SimulationRequest>(
        new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.DropOldest
        });

    public void Enqueue(SimulationRequest request) =>
        _channel.Writer.TryWrite(request);

    public ChannelReader<SimulationRequest> Reader => _channel.Reader;
}

/// <summary>
/// Long-running hosted service that processes simulation requests from the queue sequentially.
/// Runs for the lifetime of the ASP.NET Core host. Each request triggers a full Monte Carlo
/// simulation via SimulationService, which persists the snapshot and updates the trend cache.
/// </summary>
public class SimulationBackgroundService(
    SimulationQueue queue,
    SimulationService simulationService,
    ILogger<SimulationBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var request in queue.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                logger.LogInformation("Starting background simulation (baseline={BaselineYear}, n={NumSims})",
                    request.BaselineYear ?? 2025, request.NumSimulations ?? 10_000);

                var snapshot = await simulationService.RunSimulationAsync(request);

                logger.LogInformation("Background simulation completed. Timestamp: {Timestamp}",
                    snapshot.Timestamp);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Background simulation failed");
            }
        }
    }
}
