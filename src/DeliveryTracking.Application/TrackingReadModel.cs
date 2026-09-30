using System.Collections.Concurrent;
using System.Threading.Channels;

namespace DeliveryTracking.Application;

/// <summary>
/// Customer-side projection keyed by order id, plus fan-out to live subscribers.
/// In production the views live in Redis and the fan-out goes through a SignalR backplane / pub-sub channel;
/// the contract stays the same: newest version wins, every push is the full state, delivery per subscriber is lossy.
/// </summary>
public sealed class TrackingReadModel
{
    private const int SubscriberBuffer = 4;

    private readonly ConcurrentDictionary<Guid, TrackingView> _views = new();
    private readonly ConcurrentDictionary<Guid, Subscribers> _subscribers = new();

    public TrackingView? Find(Guid orderId) => _views.GetValueOrDefault(orderId);

    public void Upsert(TrackingView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        var stored = _views.AddOrUpdate(
            view.OrderId,
            view,
            (_, existing) => view.Version > existing.Version ? view : existing);

        if (!ReferenceEquals(stored, view) || !_subscribers.TryGetValue(view.OrderId, out var subscribers))
        {
            return;
        }

        foreach (var channel in subscribers.Snapshot())
        {
            // Bounded, drop-oldest: a slow phone only skips intermediate states and never blocks the writer.
            channel.Writer.TryWrite(view);
        }
    }

    /// <summary>
    /// Subscribe first, then read <see cref="Find"/>: nothing published in between is lost,
    /// and the reader skips anything that is not newer than what it already sent.
    /// </summary>
    public Subscription Subscribe(Guid orderId)
    {
        var channel = Channel.CreateBounded<TrackingView>(new BoundedChannelOptions(SubscriberBuffer)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false,
        });

        var subscribers = _subscribers.GetOrAdd(orderId, _ => new Subscribers());
        subscribers.Add(channel);
        return new Subscription(channel.Reader, () =>
        {
            subscribers.Remove(channel);
            channel.Writer.TryComplete();
        });
    }

    internal int SubscriberCount(Guid orderId)
        => _subscribers.TryGetValue(orderId, out var subscribers) ? subscribers.Snapshot().Length : 0;

    public sealed class Subscription(ChannelReader<TrackingView> reader, Action unsubscribe) : IDisposable
    {
        private int _disposed;

        public ChannelReader<TrackingView> Reader { get; } = reader;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                unsubscribe();
            }
        }
    }

    private sealed class Subscribers
    {
        private readonly Lock _gate = new();
        private Channel<TrackingView>[] _channels = [];

        public Channel<TrackingView>[] Snapshot() => Volatile.Read(ref _channels);

        public void Add(Channel<TrackingView> channel)
        {
            lock (_gate)
            {
                _channels = [.. _channels, channel];
            }
        }

        public void Remove(Channel<TrackingView> channel)
        {
            lock (_gate)
            {
                _channels = _channels.Where(c => c != channel).ToArray();
            }
        }
    }
}
