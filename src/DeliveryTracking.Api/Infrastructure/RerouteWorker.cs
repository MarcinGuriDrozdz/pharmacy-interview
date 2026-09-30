using System.Threading.Channels;
using DeliveryTracking.Application;
using DeliveryTracking.Domain;

namespace DeliveryTracking.Api.Infrastructure;

public sealed class RerouteQueue
{
    private readonly Channel<RerouteRequested> _channel = Channel.CreateUnbounded<RerouteRequested>(
        new UnboundedChannelOptions { SingleReader = true });

    public void Enqueue(RerouteRequested request) => _channel.Writer.TryWrite(request);

    public IAsyncEnumerable<RerouteRequested> ReadAllAsync(CancellationToken cancellationToken)
        => _channel.Reader.ReadAllAsync(cancellationToken);
}

/// <summary>
/// Calls the routing provider off the location hot path. A failed call is logged and dropped: the courier stays
/// off-route and the customer keeps seeing the last on-route position, which is the documented degradation.
/// </summary>
public sealed class RerouteWorker(RerouteQueue queue, TrackingService tracking, ILogger<RerouteWorker> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var request in queue.ReadAllAsync(stoppingToken))
        {
            try
            {
                await tracking.HandleRerouteAsync(request, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Reroute {RequestId} for delivery {DeliveryId} failed.", request.RequestId, request.DeliveryId);
            }
        }
    }
}
