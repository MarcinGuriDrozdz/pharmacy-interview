using System.Collections.Concurrent;
using DeliveryTracking.Domain;

namespace DeliveryTracking.Application.InMemory;

/// <summary>
/// Identity map for the demo. Safe only because <see cref="TrackingService"/> serializes writers per delivery;
/// a real store (Redis hash / PostgreSQL row per active delivery) would add a version check on save.
/// </summary>
public sealed class InMemoryDeliveryRepository : IDeliveryRepository
{
    private readonly ConcurrentDictionary<Guid, TrackedDelivery> _deliveries = new();

    public Task<bool> TryAddAsync(TrackedDelivery delivery, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(delivery);
        return Task.FromResult(_deliveries.TryAdd(delivery.Id, delivery));
    }

    public Task<TrackedDelivery?> GetAsync(Guid deliveryId, CancellationToken cancellationToken)
        => Task.FromResult(_deliveries.GetValueOrDefault(deliveryId));

    public Task SaveAsync(TrackedDelivery delivery, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(delivery);
        _deliveries[delivery.Id] = delivery;
        return Task.CompletedTask;
    }
}
