namespace DeliveryTracking.Domain;

/// <summary>
/// Ordered polyline of the delivery leg (pharmacy to customer), in WGS84.
/// </summary>
public sealed class PlannedRoute
{
    private const double MinimumSegmentMeters = 1d;
    private const double CrossTrackTieMeters = 0.05;

    private readonly GeoPoint[] _vertices;
    private readonly double[] _segmentLengths;
    private readonly double[] _cumulativeMeters;

    public PlannedRoute(IReadOnlyList<GeoPoint> vertices)
    {
        ArgumentNullException.ThrowIfNull(vertices);
        if (vertices.Count < 2)
        {
            throw new ArgumentException("A route needs at least two vertices.", nameof(vertices));
        }

        _vertices = vertices.ToArray();
        _segmentLengths = new double[_vertices.Length - 1];
        _cumulativeMeters = new double[_vertices.Length];

        for (var i = 0; i < _segmentLengths.Length; i++)
        {
            var length = SegmentLength(_vertices[i], _vertices[i + 1]);
            if (length < MinimumSegmentMeters)
            {
                throw new ArgumentException(
                    $"Segment {i} is {length:0.###} m. Consecutive vertices must be at least {MinimumSegmentMeters:0.#} m apart.",
                    nameof(vertices));
            }

            _segmentLengths[i] = length;
            _cumulativeMeters[i + 1] = _cumulativeMeters[i] + length;
        }

        TotalLengthMeters = _cumulativeMeters[^1];
    }

    public IReadOnlyList<GeoPoint> Vertices => _vertices;

    public double TotalLengthMeters { get; }

    public GeoPoint Start => _vertices[0];

    public GeoPoint End => _vertices[^1];

    /// <summary>
    /// Nearest point on the polyline. The projection is clamped to each segment, so progress cannot fall outside the route.
    /// When two segments are equally close, the one farther along the route wins.
    /// </summary>
    public RouteProjection Project(GeoPoint point)
    {
        var bestCross = double.PositiveInfinity;
        var bestAlong = 0d;
        var bestSegment = 0;
        var bestPoint = _vertices[0];

        for (var i = 0; i < _segmentLengths.Length; i++)
        {
            var projected = ProjectOntoSegment(_vertices[i], _vertices[i + 1], point, _segmentLengths[i]);
            var along = _cumulativeMeters[i] + projected.AlongMeters;
            var closer = projected.CrossTrackMeters < bestCross - CrossTrackTieMeters;
            var tiedButFarther = Math.Abs(projected.CrossTrackMeters - bestCross) <= CrossTrackTieMeters && along > bestAlong;
            if (!closer && !tiedButFarther)
            {
                continue;
            }

            bestCross = projected.CrossTrackMeters;
            bestAlong = along;
            bestSegment = i;
            bestPoint = projected.Point;
        }

        var fraction = TotalLengthMeters <= 0 ? 0 : bestAlong / TotalLengthMeters;
        return new RouteProjection(bestSegment, bestAlong, fraction, bestCross, bestPoint);
    }

    private readonly record struct SegmentProjection(GeoPoint Point, double AlongMeters, double CrossTrackMeters);

    private static SegmentProjection ProjectOntoSegment(GeoPoint a, GeoPoint b, GeoPoint p, double segmentLength)
    {
        var metersPerDegreeLongitude = GeoMath.MetersPerDegreeLongitude(a.Latitude);
        var bx = (b.Longitude - a.Longitude) * metersPerDegreeLongitude;
        var by = (b.Latitude - a.Latitude) * GeoMath.MetersPerDegreeLatitude;
        var px = (p.Longitude - a.Longitude) * metersPerDegreeLongitude;
        var py = (p.Latitude - a.Latitude) * GeoMath.MetersPerDegreeLatitude;
        var lengthSquared = bx * bx + by * by;
        var t = lengthSquared < 1e-8
            ? 0d
            : Math.Clamp((px * bx + py * by) / lengthSquared, 0d, 1d);

        var snappedEast = t * bx;
        var snappedNorth = t * by;
        var cross = Math.Sqrt(Math.Pow(px - snappedEast, 2) + Math.Pow(py - snappedNorth, 2));
        var snapped = new GeoPoint(
            a.Latitude + snappedNorth / GeoMath.MetersPerDegreeLatitude,
            a.Longitude + snappedEast / metersPerDegreeLongitude);

        return new SegmentProjection(snapped, t * segmentLength, cross);
    }

    private static double SegmentLength(GeoPoint a, GeoPoint b)
    {
        var metersPerDegreeLongitude = GeoMath.MetersPerDegreeLongitude(a.Latitude);
        var east = (b.Longitude - a.Longitude) * metersPerDegreeLongitude;
        var north = (b.Latitude - a.Latitude) * GeoMath.MetersPerDegreeLatitude;
        return Math.Sqrt(east * east + north * north);
    }
}

public readonly record struct RouteProjection(
    int SegmentIndex,
    double DistanceAlongMeters,
    double Fraction,
    double CrossTrackMeters,
    GeoPoint SnappedPoint);
