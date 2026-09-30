using System.Text.Json.Serialization;
using DeliveryTracking.Domain;

namespace DeliveryTracking.Application;

public sealed record PointView(double Latitude, double Longitude)
{
    public static PointView From(GeoPoint point) => new(point.Latitude, point.Longitude);
}

public sealed record PharmacyView(string Name, string Address, PointView Location);

public sealed record CourierView(
    PointView Location,
    double Progress,
    double RemainingMeters,
    DateTimeOffset? Eta,
    bool OffRoute);

/// <summary>
/// What the customer's app renders. It is the complete state, not a delta, so a client that misses pushes
/// or reconnects only needs the newest one. <see cref="Route"/> may be omitted in a push when the client
/// already holds <see cref="RouteVersion"/>.
/// </summary>
public sealed record TrackingView(
    Guid OrderId,
    long Version,
    DeliveryPhase Phase,
    PharmacyView Pharmacy,
    PointView Destination,
    CourierView? Courier,
    int RouteVersion,
    IReadOnlyList<PointView>? Route)
{
    [JsonIgnore]
    public Guid CustomerId { get; init; }

    public static TrackingView From(TrackedDelivery delivery)
    {
        ArgumentNullException.ThrowIfNull(delivery);

        // Before pickup there is nothing to follow, and the courier's own whereabouts are not the customer's business.
        var courier = delivery.Phase == DeliveryPhase.AwaitingPickup || delivery.CourierLocation is null
            ? null
            : new CourierView(
                PointView.From(delivery.CourierLocation.Value),
                Math.Round(delivery.Progress, 4),
                Math.Round(delivery.RemainingMeters),
                delivery.Eta,
                delivery.IsOffRoute);

        return new TrackingView(
            delivery.OrderId,
            delivery.Version,
            delivery.Phase,
            new PharmacyView(delivery.Pharmacy.Name, delivery.Pharmacy.Address, PointView.From(delivery.Pharmacy.Location)),
            PointView.From(delivery.Destination),
            courier,
            delivery.RouteVersion,
            delivery.Route?.Vertices.Select(PointView.From).ToArray())
        {
            CustomerId = delivery.CustomerId,
        };
    }
}
