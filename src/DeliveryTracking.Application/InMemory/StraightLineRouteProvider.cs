using DeliveryTracking.Domain;

namespace DeliveryTracking.Application.InMemory;

/// <summary>
/// Demo stand-in for the maps provider: a straight polyline split into ~100 m segments, so snapping and
/// progress behave as they would on street geometry. Nothing else in the module assumes a straight line.
/// </summary>
public sealed class StraightLineRouteProvider : IRouteProvider
{
    private const double SegmentMeters = 100;

    public Task<PlannedRoute> GetRouteAsync(GeoPoint from, GeoPoint to, CancellationToken cancellationToken)
    {
        var length = GeoMath.DistanceMeters(from, to);
        var segments = Math.Max(1, (int)Math.Ceiling(length / SegmentMeters));
        var vertices = new GeoPoint[segments + 1];
        for (var i = 0; i <= segments; i++)
        {
            var t = (double)i / segments;
            vertices[i] = new GeoPoint(
                from.Latitude + (to.Latitude - from.Latitude) * t,
                from.Longitude + (to.Longitude - from.Longitude) * t);
        }

        return Task.FromResult(new PlannedRoute(vertices));
    }
}
