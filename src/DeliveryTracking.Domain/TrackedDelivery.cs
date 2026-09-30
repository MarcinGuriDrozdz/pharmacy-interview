namespace DeliveryTracking.Domain;

/// <summary>
/// Tracking state of one order's delivery, from pharmacy assignment to arrival at the customer's address.
/// <para>
/// Invariants: position only moves forward along the current <see cref="Route"/>; a retried fix
/// (same <see cref="LocationFix.EventId"/>) is a no-op; an older fix never overwrites a newer one;
/// arrival is final. The aggregate is not thread-safe: the application layer runs one writer per delivery.
/// </para>
/// </summary>
public sealed class TrackedDelivery
{
    private readonly HashSet<Guid> _processedEventIds = [];
    private readonly List<PaceSample> _pace = [];
    private double _lastPublishedAlongMeters;
    private double _completedMeters;
    private DateTimeOffset? _offRouteSince;
    private Guid? _pendingRerouteId;

    private TrackedDelivery(Guid id, Guid orderId, Guid customerId, PharmacyInfo pharmacy, GeoPoint destination)
    {
        Id = id;
        OrderId = orderId;
        CustomerId = customerId;
        Pharmacy = pharmacy;
        Destination = destination;
    }

    public Guid Id { get; }
    public Guid OrderId { get; }
    public Guid CustomerId { get; }
    public PharmacyInfo Pharmacy { get; }
    public GeoPoint Destination { get; }
    public DeliveryPhase Phase { get; private set; } = DeliveryPhase.AwaitingPickup;
    public Guid? CourierId { get; private set; }
    public PlannedRoute? Route { get; private set; }
    public int RouteVersion { get; private set; }
    public DateTimeOffset? PickedUpAt { get; private set; }
    public DateTimeOffset? ArrivedAt { get; private set; }

    /// <summary>Distance along the current route. Resets to zero when a reroute is applied.</summary>
    public double DistanceAlongMeters { get; private set; }

    public double RemainingMeters => Route is null ? 0 : Math.Max(0d, Route.TotalLengthMeters - DistanceAlongMeters);

    /// <summary>Share of the journey behind the courier, counting legs completed before a reroute.</summary>
    public double Progress
    {
        get
        {
            if (Phase == DeliveryPhase.Arrived)
            {
                return 1;
            }

            var covered = _completedMeters + DistanceAlongMeters;
            var total = covered + RemainingMeters;
            return total <= 0 ? 0 : Math.Clamp(covered / total, 0d, 1d);
        }
    }

    public GeoPoint? CourierLocation { get; private set; }
    public DateTimeOffset? Eta { get; private set; }
    public double? AssumedSpeedMetersPerSecond { get; private set; }
    public long? LastSequence { get; private set; }
    public DateTimeOffset? LastRecordedAt { get; private set; }
    public bool IsOffRoute => _offRouteSince is not null;

    /// <summary>Bumped on every change a customer can see. Clients use it to drop out-of-order pushes.</summary>
    public long Version { get; private set; }

    public static TrackedDelivery Create(
        Guid deliveryId,
        Guid orderId,
        Guid customerId,
        PharmacyInfo pharmacy,
        GeoPoint destination)
    {
        RequireId(deliveryId, nameof(deliveryId));
        RequireId(orderId, nameof(orderId));
        RequireId(customerId, nameof(customerId));
        ArgumentNullException.ThrowIfNull(pharmacy);
        ArgumentException.ThrowIfNullOrWhiteSpace(pharmacy.Name);

        return new TrackedDelivery(deliveryId, orderId, customerId, pharmacy, destination) { Version = 1 };
    }

    /// <summary>Courier confirmed the handover at the pharmacy. A retry by the same courier is a no-op.</summary>
    public IReadOnlyList<IDomainEvent> PickUp(
        Guid courierId,
        PlannedRoute route,
        DateTimeOffset pickedUpAt,
        DateTimeOffset utcNow,
        TrackingPolicy policy)
    {
        RequireId(courierId, nameof(courierId));
        ArgumentNullException.ThrowIfNull(route);
        policy.EnsureValid();

        if (Phase != DeliveryPhase.AwaitingPickup)
        {
            return CourierId == courierId
                ? []
                : throw new DeliveryStateException($"Delivery {Id} was already picked up by another courier.");
        }

        CourierId = courierId;
        PickedUpAt = pickedUpAt;
        Phase = DeliveryPhase.InTransit;
        Route = route;
        RouteVersion = 1;
        CourierLocation = route.Start;
        AssumedSpeedMetersPerSecond = policy.FallbackSpeedMetersPerSecond;
        Eta = utcNow.AddSeconds(route.TotalLengthMeters / policy.FallbackSpeedMetersPerSecond);
        Version++;

        return
        [
            new CourierPickedUp(Id, OrderId, courierId, pickedUpAt),
            new RouteChanged(Id, OrderId, RouteVersion),
            new EtaChanged(Id, OrderId, Eta.Value, RemainingMeters, policy.FallbackSpeedMetersPerSecond),
        ];
    }

    public LocationHandlingResult Apply(LocationFix fix, DateTimeOffset utcNow, TrackingPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        policy.EnsureValid();
        Validate(fix);

        // Not consumed: the fix predates the leg, and it is stale by timestamp once the leg exists.
        if (Phase == DeliveryPhase.AwaitingPickup || Route is null)
        {
            return NoEvents(LocationDecision.NotPickedUp);
        }

        // Not consumed either, so the device can resend it with a corrected clock.
        if ((fix.RecordedAt - utcNow).TotalSeconds > policy.MaxClockSkewSeconds)
        {
            return NoEvents(LocationDecision.ClockSkew);
        }

        if (!_processedEventIds.Add(fix.EventId))
        {
            return NoEvents(LocationDecision.Duplicate);
        }

        if (IsStale(fix, policy))
        {
            return NoEvents(LocationDecision.Stale);
        }

        if (Phase == DeliveryPhase.Arrived)
        {
            AdvanceWatermark(fix);
            return NoEvents(LocationDecision.AlreadyArrived);
        }

        var projection = Route.Project(fix.Location);

        if (projection.CrossTrackMeters > policy.OffRouteMeters)
        {
            AdvanceWatermark(fix);
            return new LocationHandlingResult(LocationDecision.OffRoute, TrackOffRoute(fix, policy));
        }

        // Back on the route: whatever reroute is still in flight is obsolete.
        _offRouteSince = null;
        _pendingRerouteId = null;

        // Event id is already consumed. Leaving the sequence watermark alone lets a delayed
        // intermediate fix (sequence between the last good sample and this spike) still apply.
        if (IsImplausibleForwardJump(fix, projection, policy))
        {
            return NoEvents(LocationDecision.Outlier);
        }

        if (projection.DistanceAlongMeters + policy.ProgressEpsilonMeters < DistanceAlongMeters)
        {
            AdvanceWatermark(fix);
            return NoEvents(LocationDecision.BackwardIgnored);
        }

        return Advance(fix, projection, utcNow, policy);
    }

    /// <summary>
    /// Applies the routing provider's answer to <see cref="RerouteRequested"/>. An answer for a request that is
    /// no longer pending (courier came back, delivery arrived, a newer request exists) is ignored.
    /// </summary>
    public IReadOnlyList<IDomainEvent> Reroute(Guid requestId, PlannedRoute route, DateTimeOffset utcNow, TrackingPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(route);
        policy.EnsureValid();

        if (Phase != DeliveryPhase.InTransit || _pendingRerouteId != requestId)
        {
            return [];
        }

        _completedMeters += DistanceAlongMeters;
        Route = route;
        RouteVersion++;
        DistanceAlongMeters = 0;
        _lastPublishedAlongMeters = 0;
        _pace.Clear();
        _offRouteSince = null;
        _pendingRerouteId = null;
        CourierLocation = route.Start;
        AssumedSpeedMetersPerSecond = policy.FallbackSpeedMetersPerSecond;
        Eta = utcNow.AddSeconds(route.TotalLengthMeters / policy.FallbackSpeedMetersPerSecond);
        Version++;

        return
        [
            new RouteChanged(Id, OrderId, RouteVersion),
            new EtaChanged(Id, OrderId, Eta.Value, RemainingMeters, policy.FallbackSpeedMetersPerSecond),
        ];
    }

    private List<IDomainEvent> TrackOffRoute(LocationFix fix, TrackingPolicy policy)
    {
        _offRouteSince ??= fix.RecordedAt;
        if (_pendingRerouteId is not null
            || (fix.RecordedAt - _offRouteSince.Value).TotalSeconds < policy.RerouteAfterSeconds)
        {
            return [];
        }

        // Deterministic id: replaying the same fix history yields the same request.
        _pendingRerouteId = fix.EventId;
        return [new RerouteRequested(Id, OrderId, fix.EventId, fix.Location, Destination)];
    }

    private LocationHandlingResult Advance(
        LocationFix fix,
        RouteProjection projection,
        DateTimeOffset utcNow,
        TrackingPolicy policy)
    {
        var route = Route!;
        var previousEta = Eta;

        if (projection.DistanceAlongMeters >= DistanceAlongMeters)
        {
            DistanceAlongMeters = projection.DistanceAlongMeters;
            CourierLocation = projection.SnappedPoint;
        }

        var arrived = RemainingMeters <= policy.ArrivalRadiusMeters;
        if (arrived)
        {
            DistanceAlongMeters = route.TotalLengthMeters;
            CourierLocation = route.End;
            Phase = DeliveryPhase.Arrived;
            ArrivedAt = fix.RecordedAt;
        }

        AddPaceSample(fix.RecordedAt, DistanceAlongMeters, policy.PaceSampleCapacity);
        var speed = ResolveSpeed(policy);
        AssumedSpeedMetersPerSecond = speed;

        var eta = arrived ? fix.RecordedAt : utcNow.AddSeconds(RemainingMeters / speed);
        var publishEta = previousEta is null
            || Math.Abs((eta - previousEta.Value).TotalSeconds) >= policy.EtaPublishThresholdSeconds;
        Eta = eta;
        AdvanceWatermark(fix);

        var events = new List<IDomainEvent>(3);
        if (arrived || DistanceAlongMeters - _lastPublishedAlongMeters >= policy.ProgressEpsilonMeters)
        {
            _lastPublishedAlongMeters = DistanceAlongMeters;
            events.Add(new CourierProgressed(Id, OrderId, Progress, DistanceAlongMeters, CourierLocation!.Value, fix.RecordedAt));
        }

        if (arrived)
        {
            events.Add(new CourierArrived(Id, OrderId, fix.RecordedAt));
        }
        else if (publishEta)
        {
            events.Add(new EtaChanged(Id, OrderId, eta, RemainingMeters, speed));
        }

        if (events.Count > 0)
        {
            Version++;
        }

        return new LocationHandlingResult(LocationDecision.Applied, events);
    }

    private bool IsStale(LocationFix fix, TrackingPolicy policy)
    {
        if (PickedUpAt is DateTimeOffset pickedUpAt && fix.RecordedAt < pickedUpAt)
        {
            return true;
        }

        if (LastRecordedAt is not DateTimeOffset lastRecordedAt)
        {
            return false;
        }

        if (fix.RecordedAt < lastRecordedAt)
        {
            return true;
        }

        // A lower sequence is either a late packet or a restarted app. Only the latter is much newer.
        return LastSequence is long sequence
            && fix.Sequence <= sequence
            && (fix.RecordedAt - lastRecordedAt).TotalSeconds < policy.SequenceResetGraceSeconds;
    }

    private bool IsImplausibleForwardJump(LocationFix fix, RouteProjection projection, TrackingPolicy policy)
    {
        if (_pace.Count == 0)
        {
            return false;
        }

        var last = _pace[^1];
        var jump = projection.DistanceAlongMeters - last.AlongMeters;
        if (jump <= policy.ProgressEpsilonMeters)
        {
            return false;
        }

        var elapsedSeconds = (fix.RecordedAt - last.At).TotalSeconds;
        return elapsedSeconds <= 0 || jump / elapsedSeconds > policy.MaxSpeedMetersPerSecond;
    }

    private void AddPaceSample(DateTimeOffset at, double alongMeters, int capacity)
    {
        _pace.Add(new PaceSample(at, alongMeters));
        if (_pace.Count > capacity)
        {
            _pace.RemoveRange(0, _pace.Count - capacity);
        }
    }

    /// <summary>
    /// Pace over samples that are still inside the horizon. A stopped courier, a window that is too short,
    /// or a single fresh sample all fall back to nominal speed so ETA cannot explode.
    /// </summary>
    private double ResolveSpeed(TrackingPolicy policy)
    {
        if (_pace.Count < 2)
        {
            return policy.FallbackSpeedMetersPerSecond;
        }

        var newest = _pace[^1];
        var startIndex = _pace.Count - 1;
        while (startIndex > 0
               && (newest.At - _pace[startIndex - 1].At).TotalSeconds <= policy.PaceHorizonSeconds)
        {
            startIndex--;
        }

        var elapsedSeconds = (newest.At - _pace[startIndex].At).TotalSeconds;
        if (startIndex == _pace.Count - 1 || elapsedSeconds < policy.MinPaceWindowSeconds)
        {
            return policy.FallbackSpeedMetersPerSecond;
        }

        var observed = (newest.AlongMeters - _pace[startIndex].AlongMeters) / elapsedSeconds;
        return observed < policy.MinReliableSpeedMetersPerSecond
            ? policy.FallbackSpeedMetersPerSecond
            : Math.Min(observed, policy.MaxSpeedMetersPerSecond);
    }

    private void AdvanceWatermark(LocationFix fix)
    {
        LastSequence = fix.Sequence;
        LastRecordedAt = fix.RecordedAt;
    }

    private static LocationHandlingResult NoEvents(LocationDecision decision) => new(decision, []);

    private static void Validate(LocationFix fix)
    {
        if (fix.EventId == Guid.Empty)
        {
            throw new ArgumentException("Event id is required.", nameof(fix));
        }

        if (fix.Sequence < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(fix), "Sequence cannot be negative.");
        }
    }

    private static void RequireId(Guid id, string name)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Identifier is required.", name);
        }
    }

    private readonly record struct PaceSample(DateTimeOffset At, double AlongMeters);
}
