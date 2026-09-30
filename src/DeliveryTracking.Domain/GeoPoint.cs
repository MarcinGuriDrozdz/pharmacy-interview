namespace DeliveryTracking.Domain;

/// <summary>
/// WGS84 coordinate. Equality is exact; tracking compares positions by meters, not by this equality.
/// </summary>
public readonly record struct GeoPoint
{
    public double Latitude { get; }
    public double Longitude { get; }

    public GeoPoint(double latitude, double longitude)
    {
        if (!double.IsFinite(latitude) || latitude is < -90 or > 90)
        {
            throw new ArgumentOutOfRangeException(nameof(latitude), latitude, "Latitude must be in [-90, 90].");
        }

        if (!double.IsFinite(longitude) || longitude is < -180 or > 180)
        {
            throw new ArgumentOutOfRangeException(nameof(longitude), longitude, "Longitude must be in [-180, 180].");
        }

        Latitude = latitude;
        Longitude = longitude;
    }

    public override string ToString() => FormattableString.Invariant($"({Latitude:0.00000}, {Longitude:0.00000})");
}

/// <summary>
/// Local equirectangular meters. Accurate enough for urban route segments; this is not a geodesy library.
/// </summary>
public static class GeoMath
{
    /// <summary>Mean meters per degree of latitude. Longitude scale is this value times cos(latitude).</summary>
    public const double MetersPerDegreeLatitude = 111_132.92;

    public static double MetersPerDegreeLongitude(double latitudeDegrees)
        => MetersPerDegreeLatitude * Math.Cos(latitudeDegrees * Math.PI / 180d);

    public static GeoPoint Offset(GeoPoint origin, double northMeters, double eastMeters)
    {
        var metersPerDegreeLongitude = MetersPerDegreeLongitude(origin.Latitude);
        if (Math.Abs(metersPerDegreeLongitude) < 1e-6)
        {
            throw new ArgumentOutOfRangeException(nameof(origin), "Cannot offset from a pole with this approximation.");
        }

        return new GeoPoint(
            origin.Latitude + northMeters / MetersPerDegreeLatitude,
            origin.Longitude + eastMeters / metersPerDegreeLongitude);
    }

    public static double DistanceMeters(GeoPoint from, GeoPoint to)
    {
        var east = (to.Longitude - from.Longitude) * MetersPerDegreeLongitude(from.Latitude);
        var north = (to.Latitude - from.Latitude) * MetersPerDegreeLatitude;
        return Math.Sqrt(east * east + north * north);
    }
}
