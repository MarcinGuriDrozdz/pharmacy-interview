namespace DeliveryTracking.Application;

/// <summary>
/// One async writer per key, entries removed when idle. In-process stand-in for partitioning the location
/// stream by delivery id (one consumer owns a partition), which is how single-writer holds across instances.
/// </summary>
public sealed class KeyedLock<TKey> where TKey : notnull
{
    private readonly Dictionary<TKey, Entry> _entries = [];

    public async ValueTask<IDisposable> AcquireAsync(TKey key, CancellationToken cancellationToken)
    {
        Entry entry;
        lock (_entries)
        {
            if (!_entries.TryGetValue(key, out entry!))
            {
                entry = new Entry();
                _entries[key] = entry;
            }

            entry.References++;
        }

        try
        {
            await entry.Semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            Forget(key, entry);
            throw;
        }

        return new Releaser(this, key, entry);
    }

    internal int ActiveKeys
    {
        get
        {
            lock (_entries)
            {
                return _entries.Count;
            }
        }
    }

    private void Forget(TKey key, Entry entry)
    {
        lock (_entries)
        {
            if (--entry.References == 0)
            {
                _entries.Remove(key);
            }
        }
    }

    private sealed class Entry
    {
        public SemaphoreSlim Semaphore { get; } = new(1, 1);
        public int References { get; set; }
    }

    private sealed class Releaser(KeyedLock<TKey> owner, TKey key, Entry entry) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 1)
            {
                return;
            }

            entry.Semaphore.Release();
            owner.Forget(key, entry);
        }
    }
}
