namespace DeliveryTracking.Domain;

public enum DeliveryPhase
{
    /// <summary>Pharmacy is known and shown to the customer; the parcel has not been handed to a courier yet.</summary>
    AwaitingPickup = 0,
    InTransit = 1,
    Arrived = 2,
}

public enum LocationDecision
{
    Applied = 0,
    Duplicate = 1,
    Stale = 2,
    ClockSkew = 3,
    NotPickedUp = 4,
    OffRoute = 5,
    Outlier = 6,
    BackwardIgnored = 7,
    AlreadyArrived = 8,
}

public sealed record PharmacyInfo(Guid Id, string Name, string Address, GeoPoint Location);

public readonly record struct LocationFix(
    Guid EventId,
    long Sequence,
    DateTimeOffset RecordedAt,
    GeoPoint Location);

public sealed record LocationHandlingResult(LocationDecision Decision, IReadOnlyList<IDomainEvent> Events);

public interface IDomainEvent
{
    Guid DeliveryId { get; }
    Guid OrderId { get; }
}

public sealed record CourierPickedUp(Guid DeliveryId, Guid OrderId, Guid CourierId, DateTimeOffset PickedUpAt) : IDomainEvent;

/// <summary>The customer map must redraw the polyline. Emitted on pickup and on every applied reroute.</summary>
public sealed record RouteChanged(Guid DeliveryId, Guid OrderId, int RouteVersion) : IDomainEvent;

/// <summary>Snapped position advanced by at least the publish threshold since the previous progress event, or the courier arrived.</summary>
public sealed record CourierProgressed(
    Guid DeliveryId,
    Guid OrderId,
    double Progress,
    double DistanceAlongMeters,
    GeoPoint SnappedLocation,
    DateTimeOffset RecordedAt) : IDomainEvent;

/// <summary>Customer-facing ETA moved by at least the publish threshold. Speed is the pace actually used, after fallback and clamp.</summary>
public sealed record EtaChanged(
    Guid DeliveryId,
    Guid OrderId,
    DateTimeOffset Eta,
    double RemainingMeters,
    double AssumedSpeedMetersPerSecond) : IDomainEvent;

/// <summary>Emitted once, when remaining distance first falls inside the arrival radius.</summary>
public sealed record CourierArrived(Guid DeliveryId, Guid OrderId, DateTimeOffset ArrivedAt) : IDomainEvent;

/// <summary>
/// Internal event: the courier has left the planned route for longer than the policy allows.
/// The routing adapter answers with a new route carrying the same <see cref="RequestId"/>.
/// </summary>
public sealed record RerouteRequested(
    Guid DeliveryId,
    Guid OrderId,
    Guid RequestId,
    GeoPoint From,
    GeoPoint Destination) : IDomainEvent;

public sealed class DeliveryStateException(string message) : InvalidOperationException(message);
