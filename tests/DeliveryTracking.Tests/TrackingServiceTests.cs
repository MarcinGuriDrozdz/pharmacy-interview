using DeliveryTracking.Application;
using DeliveryTracking.Application.InMemory;
using DeliveryTracking.Domain;
using static DeliveryTracking.Tests.TestKit;

namespace DeliveryTracking.Tests;

public class TrackingServiceTests
{
    [Fact]
    public async Task Offline_batch_is_applied_in_device_order()
    {
        var lab = await Lab.PickedUp();

        var outcomes = await lab.Service.ReportLocationsAsync(lab.DeliveryId, lab.CourierId,
        [
            Fix(3, 300, T0.AddSeconds(60)),
            Fix(1, 100, T0.AddSeconds(20)),
            Fix(2, 200, T0.AddSeconds(40)),
        ]);

        Assert.All(outcomes, o => Assert.Equal(LocationDecision.Applied, o.Decision));
        var view = lab.ReadModel.Find(lab.OrderId)!;
        Assert.InRange(view.Courier!.Progress, 0.29, 0.31);
    }

    [Fact]
    public async Task Another_courier_cannot_report_for_the_delivery()
    {
        var lab = await Lab.PickedUp();

        await Assert.ThrowsAsync<CourierNotAssignedException>(() =>
            lab.Service.ReportLocationsAsync(lab.DeliveryId, Guid.NewGuid(), [Fix(1, 100, T0)]));
    }

    [Fact]
    public async Task Redelivered_registration_is_accepted_once()
    {
        var lab = await Lab.Registered();

        var again = await lab.Service.RegisterAsync(lab.Registration);

        Assert.False(again);
        Assert.Equal(DeliveryPhase.AwaitingPickup, lab.ReadModel.Find(lab.OrderId)!.Phase);
    }

    [Fact]
    public async Task Customer_sees_the_pharmacy_before_pickup_but_no_courier()
    {
        var lab = await Lab.Registered();

        var view = lab.ReadModel.Find(lab.OrderId)!;

        Assert.Equal(Pharmacy.Name, view.Pharmacy.Name);
        Assert.Null(view.Courier);
        Assert.Null(view.Route);
    }

    [Fact]
    public async Task Concurrent_batches_for_one_delivery_are_serialized()
    {
        var lab = await Lab.PickedUp();

        // Parallel requests from a retrying app. Without the per-delivery lock the aggregate's collections
        // are mutated concurrently; with it every fix gets a decision and the newest one wins.
        var tasks = Enumerable.Range(1, 60)
            .Select(i => Task.Run(() => lab.Service.ReportLocationsAsync(
                lab.DeliveryId, lab.CourierId, [Fix(i, i * 10, T0.AddSeconds(i))])))
            .ToArray();
        var outcomes = (await Task.WhenAll(tasks)).SelectMany(o => o).ToArray();

        Assert.Equal(60, outcomes.Length);
        Assert.Contains(outcomes, o => o.Decision == LocationDecision.Applied);
        var delivery = (await lab.Repository.GetAsync(lab.DeliveryId, default))!;
        Assert.Equal(60, delivery.LastSequence);
        Assert.Equal(0, lab.Service.ActiveLocks);
    }

    [Fact]
    public async Task Detour_is_rerouted_through_the_route_provider()
    {
        var lab = await Lab.PickedUp();
        await lab.Service.ReportLocationsAsync(lab.DeliveryId, lab.CourierId,
        [
            Fix(1, 300, T0.AddSeconds(50)),
            Fix(2, 350, T0.AddSeconds(60), north: 200),
            Fix(3, 400, T0.AddSeconds(85), north: 200),
        ]);

        var request = Assert.Single(lab.Sink.Events.OfType<RerouteRequested>());
        await lab.Service.HandleRerouteAsync(request);

        var view = lab.ReadModel.Find(lab.OrderId)!;
        Assert.Equal(2, view.RouteVersion);
        Assert.False(view.Courier!.OffRoute);
        Assert.Equal(request.From, lab.Routes.Calls[^1].From);
    }

    [Fact]
    public async Task Read_model_keeps_the_newest_state_for_a_slow_subscriber()
    {
        var lab = await Lab.PickedUp();
        using var subscription = lab.ReadModel.Subscribe(lab.OrderId);

        for (var i = 1; i <= 20; i++)
        {
            await lab.Service.ReportLocationsAsync(lab.DeliveryId, lab.CourierId, [Fix(i, i * 20, T0.AddSeconds(i * 4))]);
        }

        var received = new List<TrackingView>();
        while (subscription.Reader.TryRead(out var view))
        {
            received.Add(view);
        }

        Assert.InRange(received.Count, 1, 4);
        Assert.Equal(lab.ReadModel.Find(lab.OrderId)!.Version, received[^1].Version);
        subscription.Dispose();
        Assert.Equal(0, lab.ReadModel.SubscriberCount(lab.OrderId));
    }

    private static LocationFix Fix(long seq, double eastMeters, DateTimeOffset at, double north = 0)
        => new(Guid.NewGuid(), seq, at, East(eastMeters, north));

    private sealed class RecordingSink(TrackingReadModel readModel) : ITrackingEventSink
    {
        private readonly List<IDomainEvent> _events = [];

        public IReadOnlyList<IDomainEvent> Events
        {
            get
            {
                lock (_events)
                {
                    return _events.ToArray();
                }
            }
        }

        public ValueTask PublishAsync(TrackedDelivery delivery, IReadOnlyList<IDomainEvent> events, CancellationToken cancellationToken)
        {
            readModel.Upsert(TrackingView.From(delivery));
            lock (_events)
            {
                _events.AddRange(events);
            }

            return ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingRouteProvider : IRouteProvider
    {
        private readonly StraightLineRouteProvider _inner = new();

        public List<(GeoPoint From, GeoPoint To)> Calls { get; } = [];

        public Task<PlannedRoute> GetRouteAsync(GeoPoint from, GeoPoint to, CancellationToken cancellationToken)
        {
            Calls.Add((from, to));
            return _inner.GetRouteAsync(from, to, cancellationToken);
        }
    }

    private sealed class Lab
    {
        public InMemoryDeliveryRepository Repository { get; } = new();
        public RecordingRouteProvider Routes { get; } = new();
        public TrackingReadModel ReadModel { get; } = new();
        public RecordingSink Sink { get; }
        public ManualTimeProvider Time { get; } = new(T0.AddMinutes(5));
        public TrackingService Service { get; }
        public Guid DeliveryId { get; } = Guid.NewGuid();
        public Guid OrderId { get; } = Guid.NewGuid();
        public Guid CourierId { get; } = Guid.NewGuid();
        public RegisterDelivery Registration { get; }

        private Lab()
        {
            Sink = new RecordingSink(ReadModel);
            Service = new TrackingService(Repository, Routes, Sink, Time, TrackingPolicy.Default);
            Registration = new RegisterDelivery(DeliveryId, OrderId, Guid.NewGuid(), Pharmacy, East(1000));
        }

        public static async Task<Lab> Registered()
        {
            var lab = new Lab();
            Assert.True(await lab.Service.RegisterAsync(lab.Registration));
            return lab;
        }

        public static async Task<Lab> PickedUp()
        {
            var lab = await Registered();
            await lab.Service.PickUpAsync(lab.DeliveryId, lab.CourierId, T0);
            return lab;
        }
    }
}
