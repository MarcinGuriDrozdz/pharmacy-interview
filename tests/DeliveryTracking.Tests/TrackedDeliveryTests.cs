using DeliveryTracking.Domain;
using static DeliveryTracking.Tests.TestKit;

namespace DeliveryTracking.Tests;

public class TrackedDeliveryTests
{
    [Fact]
    public void Out_of_order_gps_does_not_move_the_courier()
    {
        var leg = Leg.Start(1000);

        var established = leg.Report(seq: 5, eastMeters: 300, at: T0);
        var lateAndBehind = leg.Report(seq: 4, eastMeters: 40, at: T0.AddSeconds(30));
        var lateButGeographicallyAhead = leg.Report(seq: 3, eastMeters: 800, at: T0.AddSeconds(40));

        Assert.Equal(LocationDecision.Applied, established.Decision);
        Assert.Equal(LocationDecision.Stale, lateAndBehind.Decision);
        Assert.Equal(LocationDecision.Stale, lateButGeographicallyAhead.Decision);
        Assert.Empty(lateButGeographicallyAhead.Events);
        Assert.InRange(leg.Delivery.DistanceAlongMeters, 290, 310);
    }

    [Fact]
    public void Older_timestamp_is_stale_even_with_a_higher_sequence()
    {
        var leg = Leg.Start(1000);

        leg.Report(seq: 1, eastMeters: 100, at: T0.AddMinutes(1));
        var clockWentBackwards = leg.Report(seq: 2, eastMeters: 400, at: T0.AddSeconds(10));

        Assert.Equal(LocationDecision.Stale, clockWentBackwards.Decision);
        Assert.InRange(leg.Delivery.DistanceAlongMeters, 90, 110);
    }

    [Fact]
    public void Fix_recorded_before_pickup_is_stale()
    {
        var leg = Leg.Start(1000);

        var onTheWayToPharmacy = leg.Report(seq: 1, eastMeters: 500, at: T0.AddMinutes(-3));

        Assert.Equal(LocationDecision.Stale, onTheWayToPharmacy.Decision);
        Assert.Equal(0, leg.Delivery.DistanceAlongMeters);
    }

    [Fact]
    public void Fix_before_pickup_is_not_tracked()
    {
        var delivery = NewDelivery();

        var result = delivery.Apply(new LocationFix(Guid.NewGuid(), 1, T0, Origin), T0, TrackingPolicy.Default);

        Assert.Equal(LocationDecision.NotPickedUp, result.Decision);
        Assert.Equal(DeliveryPhase.AwaitingPickup, delivery.Phase);
        Assert.Null(delivery.CourierLocation);
    }

    [Fact]
    public void Duplicate_event_does_not_double_apply()
    {
        var leg = Leg.Start(1000);
        var eventId = Guid.NewGuid();

        leg.Report(seq: 1, eastMeters: 0, at: T0);
        var move = leg.Report(seq: 2, eastMeters: 250, at: T0.AddSeconds(40), eventId: eventId);
        var version = leg.Delivery.Version;
        var replay = leg.Report(seq: 2, eastMeters: 250, at: T0.AddSeconds(40), eventId: eventId);

        Assert.Contains(move.Events, e => e is CourierProgressed);
        Assert.Equal(LocationDecision.Duplicate, replay.Decision);
        Assert.Empty(replay.Events);
        Assert.Equal(version, leg.Delivery.Version);
    }

    [Fact]
    public void Progress_does_not_go_backwards_when_a_newer_fix_snaps_behind()
    {
        var leg = Leg.Start(1000);
        leg.Report(seq: 1, eastMeters: 0, at: T0);

        leg.Report(seq: 2, eastMeters: 400, at: T0.AddSeconds(60));
        var noiseBehind = leg.Report(seq: 3, eastMeters: 250, at: T0.AddSeconds(70));
        var afterNoise = leg.Delivery.DistanceAlongMeters;
        var jitterWithinEpsilon = leg.Report(seq: 4, eastMeters: 395, at: T0.AddSeconds(80));

        Assert.Equal(LocationDecision.BackwardIgnored, noiseBehind.Decision);
        Assert.InRange(afterNoise, 390, 410);
        Assert.Equal(LocationDecision.Applied, jitterWithinEpsilon.Decision);
        Assert.True(leg.Delivery.DistanceAlongMeters >= afterNoise - 0.01);
        Assert.DoesNotContain(jitterWithinEpsilon.Events, e => e is CourierProgressed);
    }

    [Fact]
    public void Sub_epsilon_steps_publish_progress_once_their_sum_crosses_the_threshold()
    {
        var leg = Leg.Start(1000);
        leg.Report(seq: 1, eastMeters: 0, at: T0);

        var firstStep = leg.Report(seq: 2, eastMeters: 6, at: T0.AddSeconds(1));
        var secondStep = leg.Report(seq: 3, eastMeters: 12, at: T0.AddSeconds(2));

        Assert.DoesNotContain(firstStep.Events, e => e is CourierProgressed);
        var progressed = Assert.Single(secondStep.Events.OfType<CourierProgressed>());
        Assert.InRange(progressed.DistanceAlongMeters, 11, 13);
    }

    [Fact]
    public void Stationary_courier_keeps_eta_finite_using_fallback_speed()
    {
        var leg = Leg.Start(1000);
        var policy = TrackingPolicy.Default;

        leg.Report(seq: 1, eastMeters: 0, at: T0);
        leg.Now = T0.AddMinutes(3);
        var stopped = leg.Report(seq: 2, eastMeters: 0, at: T0.AddMinutes(3));

        var etaChanged = Assert.Single(stopped.Events.OfType<EtaChanged>());
        Assert.Equal(policy.FallbackSpeedMetersPerSecond, etaChanged.AssumedSpeedMetersPerSecond, precision: 3);
        var seconds = (etaChanged.Eta - leg.Now).TotalSeconds;
        Assert.InRange(seconds, 1000 / policy.FallbackSpeedMetersPerSecond - 1, 1000 / policy.FallbackSpeedMetersPerSecond + 1);
    }

    [Fact]
    public void On_pace_movement_publishes_progress_without_a_noisy_eta()
    {
        var leg = Leg.Start(1000);
        leg.Now = T0;

        var started = leg.Report(seq: 1, eastMeters: 0, at: T0);
        leg.Now = T0.AddSeconds(20);
        var moved = leg.Report(seq: 2, eastMeters: 120, at: T0.AddSeconds(20));

        Assert.Empty(started.Events);
        Assert.Single(moved.Events.OfType<CourierProgressed>());
        Assert.DoesNotContain(moved.Events, e => e is EtaChanged);
        Assert.Equal(6, leg.Delivery.AssumedSpeedMetersPerSecond!.Value, precision: 2);
        Assert.InRange(leg.Delivery.Progress, 0.11, 0.13);
    }

    [Fact]
    public void Pace_uses_device_time_not_receive_time()
    {
        var leg = Leg.Start(1000);

        leg.Report(seq: 1, eastMeters: 0, at: T0);
        var delayedBatch = leg.Report(seq: 2, eastMeters: 200, at: T0.AddSeconds(20));

        var eta = Assert.Single(delayedBatch.Events.OfType<EtaChanged>());
        Assert.Equal(10, eta.AssumedSpeedMetersPerSecond, precision: 2);
        Assert.InRange(eta.RemainingMeters, 790, 810);
    }

    [Fact]
    public void Implausible_jump_is_rejected_and_does_not_block_the_next_legal_fix()
    {
        var leg = Leg.Start(1000);

        leg.Report(seq: 1, eastMeters: 0, at: T0);
        var spikeId = Guid.NewGuid();
        var spike = leg.Report(seq: 3, eastMeters: 800, at: T0.AddSeconds(1), eventId: spikeId);
        var spikeReplay = leg.Report(seq: 3, eastMeters: 800, at: T0.AddSeconds(1), eventId: spikeId);
        var delayedButLegal = leg.Report(seq: 2, eastMeters: 50, at: T0.AddSeconds(10));

        Assert.Equal(LocationDecision.Outlier, spike.Decision);
        Assert.Equal(LocationDecision.Duplicate, spikeReplay.Decision);
        Assert.Equal(LocationDecision.Applied, delayedButLegal.Decision);
        Assert.InRange(leg.Delivery.DistanceAlongMeters, 45, 55);
    }

    [Fact]
    public void Device_clock_far_ahead_is_rejected_without_poisoning_the_watermark()
    {
        var leg = Leg.Start(1000);
        var eventId = Guid.NewGuid();

        var fromTheFuture = leg.Report(seq: 1, eastMeters: 100, at: T0.AddHours(2), eventId: eventId);
        var resentWithFixedClock = leg.Report(seq: 1, eastMeters: 100, at: T0.AddSeconds(15), eventId: eventId);

        Assert.Equal(LocationDecision.ClockSkew, fromTheFuture.Decision);
        Assert.Equal(LocationDecision.Applied, resentWithFixedClock.Decision);
        Assert.Equal(T0.AddSeconds(15), leg.Delivery.LastRecordedAt);
    }

    [Fact]
    public void Lower_sequence_is_a_new_epoch_only_when_it_is_clearly_newer()
    {
        var leg = Leg.Start(1000);

        leg.Report(seq: 50, eastMeters: 100, at: T0.AddSeconds(10));
        var ambiguous = leg.Report(seq: 1, eastMeters: 150, at: T0.AddSeconds(20));
        var afterAppRestart = leg.Report(seq: 2, eastMeters: 700, at: T0.AddMinutes(2));

        Assert.Equal(LocationDecision.Stale, ambiguous.Decision);
        Assert.Equal(LocationDecision.Applied, afterAppRestart.Decision);
        Assert.Equal(2, leg.Delivery.LastSequence);
    }

    [Fact]
    public void Off_route_point_does_not_advance_progress()
    {
        var leg = Leg.Start(1000);

        leg.Report(seq: 1, eastMeters: 100, at: T0);
        var before = leg.Delivery.DistanceAlongMeters;
        var drifted = leg.Report(seq: 2, eastMeters: 100, at: T0.AddSeconds(15), north: 250);

        Assert.Equal(LocationDecision.OffRoute, drifted.Decision);
        Assert.Empty(drifted.Events);
        Assert.Equal(before, leg.Delivery.DistanceAlongMeters);
        Assert.True(leg.Delivery.IsOffRoute);
        Assert.Equal(2, leg.Delivery.LastSequence);
    }

    [Fact]
    public void Sustained_detour_requests_one_reroute_and_the_answer_continues_the_journey()
    {
        var leg = Leg.Start(1000);
        leg.Report(seq: 1, eastMeters: 300, at: T0.AddSeconds(50));

        var firstOff = leg.Report(seq: 2, eastMeters: 350, at: T0.AddSeconds(60), north: 200);
        var stillOff = leg.Report(seq: 3, eastMeters: 400, at: T0.AddSeconds(85), north: 200);
        var keepsDriving = leg.Report(seq: 4, eastMeters: 450, at: T0.AddSeconds(95), north: 200);

        Assert.Empty(firstOff.Events);
        var request = Assert.Single(stillOff.Events.OfType<RerouteRequested>());
        Assert.Empty(keepsDriving.Events);

        var detour = new PlannedRoute([request.From, East(1000)]);
        var applied = leg.Delivery.Reroute(request.RequestId, detour, T0.AddSeconds(96), leg.Policy);
        var replayed = leg.Delivery.Reroute(request.RequestId, detour, T0.AddSeconds(97), leg.Policy);

        Assert.Contains(applied, e => e is RouteChanged { RouteVersion: 2 });
        Assert.Empty(replayed);
        Assert.False(leg.Delivery.IsOffRoute);
        Assert.Equal(0, leg.Delivery.DistanceAlongMeters);
        Assert.InRange(leg.Delivery.Progress, 0.3, 0.6);

        var onDetour = leg.Report(seq: 5, eastMeters: 500, at: T0.AddSeconds(110), north: 180);
        Assert.Equal(LocationDecision.Applied, onDetour.Decision);
    }

    [Fact]
    public void Reroute_answer_is_ignored_when_the_courier_already_came_back()
    {
        var leg = Leg.Start(1000);
        leg.Report(seq: 1, eastMeters: 300, at: T0.AddSeconds(50));
        leg.Report(seq: 2, eastMeters: 320, at: T0.AddSeconds(60), north: 200);
        var request = leg.Report(seq: 3, eastMeters: 340, at: T0.AddSeconds(85), north: 200)
            .Events.OfType<RerouteRequested>().Single();

        leg.Report(seq: 4, eastMeters: 500, at: T0.AddSeconds(100));
        var late = leg.Delivery.Reroute(request.RequestId, new PlannedRoute([request.From, East(1000)]), T0.AddSeconds(101), leg.Policy);

        Assert.Empty(late);
        Assert.Equal(1, leg.Delivery.RouteVersion);
    }

    [Fact]
    public void Arrival_near_the_end_of_the_route_is_emitted_once()
    {
        var leg = Leg.Start(500);

        leg.Report(seq: 1, eastMeters: 0, at: T0);
        leg.Report(seq: 2, eastMeters: 200, at: T0.AddSeconds(30));
        leg.Report(seq: 3, eastMeters: 400, at: T0.AddSeconds(60));
        var almostThere = leg.Report(seq: 4, eastMeters: 470, at: T0.AddSeconds(75));
        var extra = leg.Report(seq: 5, eastMeters: 490, at: T0.AddSeconds(90));

        Assert.Equal(DeliveryPhase.Arrived, leg.Delivery.Phase);
        Assert.Equal(1, leg.Delivery.Progress, precision: 5);
        Assert.Equal(0, leg.Delivery.RemainingMeters, precision: 3);
        Assert.Equal(T0.AddSeconds(75), Assert.Single(almostThere.Events.OfType<CourierArrived>()).ArrivedAt);
        Assert.Contains(almostThere.Events, e => e is CourierProgressed { Progress: 1 });
        Assert.Equal(LocationDecision.AlreadyArrived, extra.Decision);
        Assert.Empty(extra.Events);
    }

    [Fact]
    public void Pickup_is_idempotent_for_the_same_courier_and_exclusive_otherwise()
    {
        var delivery = NewDelivery();
        var courier = Guid.NewGuid();
        var route = Eastbound(1000);

        var first = delivery.PickUp(courier, route, T0, T0, TrackingPolicy.Default);
        var retry = delivery.PickUp(courier, route, T0, T0, TrackingPolicy.Default);

        Assert.Contains(first, e => e is CourierPickedUp);
        Assert.Empty(retry);
        Assert.Throws<DeliveryStateException>(() => delivery.PickUp(Guid.NewGuid(), route, T0, T0, TrackingPolicy.Default));
    }

    private sealed class Leg
    {
        private Leg(TrackedDelivery delivery) => Delivery = delivery;

        public TrackedDelivery Delivery { get; }
        public DateTimeOffset Now { get; set; } = T0.AddMinutes(5);
        public TrackingPolicy Policy { get; } = TrackingPolicy.Default;

        public static Leg Start(double lengthMeters)
        {
            var delivery = NewDelivery(lengthMeters);
            delivery.PickUp(Guid.NewGuid(), Eastbound(lengthMeters), pickedUpAt: T0, utcNow: T0, TrackingPolicy.Default);
            return new Leg(delivery);
        }

        public LocationHandlingResult Report(long seq, double eastMeters, DateTimeOffset at, Guid? eventId = null, double north = 0)
            => Delivery.Apply(new LocationFix(eventId ?? Guid.NewGuid(), seq, at, East(eastMeters, north)), Now, Policy);
    }
}
