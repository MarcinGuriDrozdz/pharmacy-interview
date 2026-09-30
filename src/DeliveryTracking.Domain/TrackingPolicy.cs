namespace DeliveryTracking.Domain;

/// <summary>
/// Numeric limits for live tracking. Defaults are the v1 contract described in the design document (section 8).
/// </summary>
public sealed record TrackingPolicy
{
    /// <summary>Cross-track beyond this does not move the courier. The courier stays on the last on-route fix.</summary>
    public double OffRouteMeters { get; init; } = 120;

    /// <summary>A courier who stays off the route for this long (device time) triggers one reroute request.</summary>
    public double RerouteAfterSeconds { get; init; } = 20;

    /// <summary>
    /// Backward gap that is ignored, and the smallest unpublished forward gap that emits <see cref="CourierProgressed"/>.
    /// Smaller forward steps accumulate until they cross this threshold.
    /// </summary>
    public double ProgressEpsilonMeters { get; init; } = 8;

    /// <summary>Remaining distance at or below this completes the delivery leg.</summary>
    public double ArrivalRadiusMeters { get; init; } = 35;

    /// <summary>Nominal urban courier speed (~21.6 km/h), used when pace is missing or not trustworthy.</summary>
    public double FallbackSpeedMetersPerSecond { get; init; } = 6;

    /// <summary>Observed pace below this is treated as stationary and replaced by the fallback, so ETA stays finite.</summary>
    public double MinReliableSpeedMetersPerSecond { get; init; } = 1.2;

    /// <summary>Forward jump faster than this, relative to the last on-route sample, is rejected.</summary>
    public double MaxSpeedMetersPerSecond { get; init; } = 22;

    /// <summary>Pace is not trusted until the sample window covers at least this many seconds.</summary>
    public double MinPaceWindowSeconds { get; init; } = 5;

    /// <summary>
    /// Samples older than this, relative to the newest on-route fix, do not affect ETA.
    /// A courier who has been still for longer than the horizon therefore falls back to nominal speed.
    /// </summary>
    public double PaceHorizonSeconds { get; init; } = 90;

    /// <summary>Absolute ETA must move by at least this many seconds before <see cref="EtaChanged"/> is emitted.</summary>
    public double EtaPublishThresholdSeconds { get; init; } = 20;

    /// <summary>
    /// A fix stamped further than this in the future (device clock ahead of the server) is rejected.
    /// Accepting it would move the time watermark forward and make every honest fix look stale.
    /// </summary>
    public double MaxClockSkewSeconds { get; init; } = 30;

    /// <summary>
    /// A lower sequence is accepted as a device restart (new sequence epoch) only when its timestamp is at least
    /// this much newer than the watermark. A late packet always carries an older timestamp, so it cannot pass.
    /// </summary>
    public double SequenceResetGraceSeconds { get; init; } = 60;

    public int PaceSampleCapacity { get; init; } = 24;

    public static TrackingPolicy Default { get; } = new();

    public void EnsureValid()
    {
        if (FallbackSpeedMetersPerSecond <= 0
            || MaxSpeedMetersPerSecond <= 0
            || MinReliableSpeedMetersPerSecond < 0
            || ProgressEpsilonMeters < 0
            || OffRouteMeters < 0
            || RerouteAfterSeconds < 0
            || ArrivalRadiusMeters < 0
            || MinPaceWindowSeconds < 0
            || PaceHorizonSeconds <= 0
            || EtaPublishThresholdSeconds < 0
            || MaxClockSkewSeconds < 0
            || SequenceResetGraceSeconds <= 0
            || PaceSampleCapacity < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(TrackingPolicy), "Tracking policy contains an invalid limit.");
        }
    }
}
