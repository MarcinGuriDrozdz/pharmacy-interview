using DeliveryTracking.Domain;

namespace DeliveryTracking.Tests;

internal static class TestKit
{
    public static readonly GeoPoint Origin = new(52.229675, 21.012230);
    public static readonly DateTimeOffset T0 = new(2026, 9, 27, 10, 0, 0, TimeSpan.Zero);

    public static readonly PharmacyInfo Pharmacy = new(
        Guid.Parse("99999999-8888-7777-6666-555555555555"),
        "Apteka Pod Lwem",
        "ul. Marszałkowska 10, Warszawa",
        Origin);

    public static GeoPoint East(double meters, double north = 0) => GeoMath.Offset(Origin, north, meters);

    public static PlannedRoute Eastbound(double lengthMeters) => new([Origin, East(lengthMeters)]);

    public static TrackedDelivery NewDelivery(double destinationEastMeters = 1000)
        => TrackedDelivery.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Pharmacy, East(destinationEastMeters));
}

internal sealed class ManualTimeProvider(DateTimeOffset utcNow) : TimeProvider
{
    public DateTimeOffset UtcNow { get; set; } = utcNow;

    public override DateTimeOffset GetUtcNow() => UtcNow;
}
