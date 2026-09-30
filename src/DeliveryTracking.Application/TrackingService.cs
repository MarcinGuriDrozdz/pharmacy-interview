using DeliveryTracking.Domain;

namespace DeliveryTracking.Application;

public sealed record RegisterDelivery(
    Guid DeliveryId,
    Guid OrderId,
    Guid CustomerId,
    PharmacyInfo Pharmacy,
    GeoPoint Destination);

public sealed record FixOutcome(Guid EventId, LocationDecision Decision);

/// <summary>
/// Use cases of the tracking module. Every write runs under a per-delivery lock:
/// load, apply, save, publish. Slow I/O (routing) happens before the lock is taken.
/// </summary>
public sealed class TrackingService(
    IDeliveryRepository repository,
    IRouteProvider routes,
    ITrackingEventSink sink,
    TimeProvider time,
    TrackingPolicy policy)
{
    public const int MaxFixesPerBatch = 200;

    private readonly KeyedLock<Guid> _locks = new();

    internal int ActiveLocks => _locks.ActiveKeys;

    /// <summary>Fulfillment assigned a pharmacy. From now on the customer sees where the order comes from.</summary>
    /// <returns><c>false</c> when the delivery was already registered (redelivered message).</returns>
    public async Task<bool> RegisterAsync(RegisterDelivery command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var delivery = TrackedDelivery.Create(
            command.DeliveryId,
            command.OrderId,
            command.CustomerId,
            command.Pharmacy,
            command.Destination);

        using var _ = await _locks.AcquireAsync(command.DeliveryId, cancellationToken).ConfigureAwait(false);
        if (!await repository.TryAddAsync(delivery, cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        await sink.PublishAsync(delivery, [], cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <summary>Courier scanned the parcel at the pharmacy. Idempotent for the same courier.</summary>
    public async Task<TrackedDelivery> PickUpAsync(
        Guid deliveryId,
        Guid courierId,
        DateTimeOffset pickedUpAt,
        CancellationToken cancellationToken = default)
    {
        var current = await LoadAsync(deliveryId, cancellationToken).ConfigureAwait(false);
        if (current.Phase != DeliveryPhase.AwaitingPickup && current.CourierId == courierId)
        {
            return current;
        }

        var route = await routes
            .GetRouteAsync(current.Pharmacy.Location, current.Destination, cancellationToken)
            .ConfigureAwait(false);

        using var _ = await _locks.AcquireAsync(deliveryId, cancellationToken).ConfigureAwait(false);
        var delivery = await LoadAsync(deliveryId, cancellationToken).ConfigureAwait(false);
        var events = delivery.PickUp(courierId, route, pickedUpAt, time.GetUtcNow(), policy);
        await CommitAsync(delivery, events, cancellationToken).ConfigureAwait(false);
        return delivery;
    }

    /// <summary>
    /// A batch from the courier app. The app buffers fixes while offline, so a batch can be large, unordered,
    /// and overlap an earlier one. Fixes are applied in device order; the aggregate filters the rest.
    /// </summary>
    public async Task<IReadOnlyList<FixOutcome>> ReportLocationsAsync(
        Guid deliveryId,
        Guid courierId,
        IReadOnlyList<LocationFix> fixes,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(fixes);
        if (fixes.Count is 0 or > MaxFixesPerBatch)
        {
            throw new ArgumentOutOfRangeException(nameof(fixes), $"A batch carries 1 to {MaxFixesPerBatch} fixes.");
        }

        using var _ = await _locks.AcquireAsync(deliveryId, cancellationToken).ConfigureAwait(false);
        var delivery = await LoadAsync(deliveryId, cancellationToken).ConfigureAwait(false);
        if (delivery.CourierId is Guid assigned && assigned != courierId)
        {
            throw new CourierNotAssignedException(deliveryId, courierId);
        }

        var now = time.GetUtcNow();
        var outcomes = new List<FixOutcome>(fixes.Count);
        var events = new List<IDomainEvent>();
        foreach (var fix in fixes.OrderBy(f => f.Sequence).ThenBy(f => f.RecordedAt))
        {
            var result = delivery.Apply(fix, now, policy);
            outcomes.Add(new FixOutcome(fix.EventId, result.Decision));
            events.AddRange(result.Events);
        }

        await CommitAsync(delivery, events, cancellationToken).ConfigureAwait(false);
        return outcomes;
    }

    /// <summary>Answers a <see cref="RerouteRequested"/> event. A late or superseded answer changes nothing.</summary>
    public async Task HandleRerouteAsync(RerouteRequested request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var route = await routes.GetRouteAsync(request.From, request.Destination, cancellationToken).ConfigureAwait(false);

        using var _ = await _locks.AcquireAsync(request.DeliveryId, cancellationToken).ConfigureAwait(false);
        var delivery = await LoadAsync(request.DeliveryId, cancellationToken).ConfigureAwait(false);
        var events = delivery.Reroute(request.RequestId, route, time.GetUtcNow(), policy);
        if (events.Count > 0)
        {
            await CommitAsync(delivery, events, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task CommitAsync(TrackedDelivery delivery, IReadOnlyList<IDomainEvent> events, CancellationToken cancellationToken)
    {
        await repository.SaveAsync(delivery, cancellationToken).ConfigureAwait(false);
        await sink.PublishAsync(delivery, events, cancellationToken).ConfigureAwait(false);
    }

    private async Task<TrackedDelivery> LoadAsync(Guid deliveryId, CancellationToken cancellationToken)
        => await repository.GetAsync(deliveryId, cancellationToken).ConfigureAwait(false)
           ?? throw new DeliveryNotFoundException(deliveryId);
}
