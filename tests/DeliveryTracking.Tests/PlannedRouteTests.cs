using DeliveryTracking.Domain;

namespace DeliveryTracking.Tests;

public class PlannedRouteTests
{
    private static readonly GeoPoint Origin = new(52.229675, 21.012230);

    [Fact]
    public void Straight_route_length_matches_the_offset_used_to_build_it()
    {
        var route = Eastbound(800);

        Assert.InRange(route.TotalLengthMeters, 799, 801);
    }

    [Fact]
    public void Point_on_the_second_leg_snaps_to_that_segment()
    {
        var corner = GeoMath.Offset(Origin, northMeters: 0, eastMeters: 500);
        var northEnd = GeoMath.Offset(Origin, northMeters: 400, eastMeters: 500);
        var route = new PlannedRoute([Origin, corner, northEnd]);

        var nearSecondLeg = GeoMath.Offset(Origin, northMeters: 200, eastMeters: 530);
        var projection = route.Project(nearSecondLeg);

        Assert.Equal(1, projection.SegmentIndex);
        Assert.InRange(projection.CrossTrackMeters, 25, 35);
        Assert.InRange(projection.DistanceAlongMeters, 680, 720);
        Assert.InRange(projection.Fraction, 0, 1);
    }

    [Fact]
    public void Point_near_the_first_leg_does_not_snap_to_the_later_segment()
    {
        var corner = GeoMath.Offset(Origin, northMeters: 0, eastMeters: 500);
        var northEnd = GeoMath.Offset(Origin, northMeters: 400, eastMeters: 500);
        var route = new PlannedRoute([Origin, corner, northEnd]);

        var nearFirstLeg = GeoMath.Offset(Origin, northMeters: 15, eastMeters: 250);
        var projection = route.Project(nearFirstLeg);

        Assert.Equal(0, projection.SegmentIndex);
        Assert.InRange(projection.DistanceAlongMeters, 230, 270);
        Assert.InRange(projection.CrossTrackMeters, 10, 20);
    }

    [Fact]
    public void Point_before_the_start_clamps_to_the_start()
    {
        var route = Eastbound(600);
        var before = GeoMath.Offset(Origin, northMeters: 0, eastMeters: -80);

        var projection = route.Project(before);

        Assert.Equal(0, projection.SegmentIndex);
        Assert.InRange(projection.DistanceAlongMeters, 0, 0.5);
        Assert.InRange(projection.Fraction, 0, 0.001);
        Assert.InRange(GeoMath.DistanceMeters(projection.SnappedPoint, route.Start), 0, 1);
    }

    [Fact]
    public void Point_past_the_end_clamps_to_the_end()
    {
        var route = Eastbound(600);
        var past = GeoMath.Offset(Origin, northMeters: 0, eastMeters: 680);

        var projection = route.Project(past);

        Assert.InRange(projection.DistanceAlongMeters, route.TotalLengthMeters - 0.5, route.TotalLengthMeters + 0.5);
        Assert.InRange(GeoMath.DistanceMeters(projection.SnappedPoint, route.End), 0, 1);
    }

    private static PlannedRoute Eastbound(double lengthMeters)
        => new([Origin, GeoMath.Offset(Origin, northMeters: 0, eastMeters: lengthMeters)]);
}
