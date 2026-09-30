using DeliveryTracking.Application;
using DeliveryTracking.Domain;

namespace DeliveryTracking.Api.Infrastructure;

/// <summary>
/// In-process outbox relay: refreshes the customer read model, hands reroute requests to the routing worker,
/// and logs what would go to the message bus (Order service closes the order on <see cref="CourierArrived"/>,
/// Notifications push "courier is 2 minutes away" on <see cref="EtaChanged"/>).
/// </summary>
public sealed class TrackingEventDispatcher(
    TrackingReadModel readModel,
    RerouteQueue reroutes,
    ILogger<TrackingEventDispatcher> logger) : ITrackingEventSink
{
    public ValueTask PublishAsync(TrackedDelivery delivery, IReadOnlyList<IDomainEvent> events, CancellationToken cancellationToken)
    {
        readModel.Upsert(TrackingView.From(delivery));

        foreach (var domainEvent in events)
        {
            if (domainEvent is RerouteRequested reroute)
            {
                reroutes.Enqueue(reroute);
                continue;
            }

            logger.LogInformation(
                "Integration event {EventType} for order {OrderId}: {Event}",
                domainEvent.GetType().Name,
                domainEvent.OrderId,
                domainEvent);
        }

        return ValueTask.CompletedTask;
    }
}
