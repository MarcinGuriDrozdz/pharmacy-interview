using DeliveryTracking.Domain;

namespace DeliveryTracking.Application;

public interface IDeliveryRepository
{
    /// <returns><c>false</c> when a delivery with the same id already exists.</returns>
    Task<bool> TryAddAsync(TrackedDelivery delivery, CancellationToken cancellationToken);

    Task<TrackedDelivery?> GetAsync(Guid deliveryId, CancellationToken cancellationToken);

    Task SaveAsync(TrackedDelivery delivery, CancellationToken cancellationToken);
}

/// <summary>Street routing (external maps provider in production). Called outside the per-delivery lock.</summary>
public interface IRouteProvider
{
    Task<PlannedRoute> GetRouteAsync(GeoPoint from, GeoPoint to, CancellationToken cancellationToken);
}

/// <summary>
/// Receives the state change of one command, in commit order per delivery. In production this is an outbox row
/// written in the same transaction as the aggregate; a relay then feeds the read model and the message bus.
/// </summary>
public interface ITrackingEventSink
{
    ValueTask PublishAsync(TrackedDelivery delivery, IReadOnlyList<IDomainEvent> events, CancellationToken cancellationToken);
}
