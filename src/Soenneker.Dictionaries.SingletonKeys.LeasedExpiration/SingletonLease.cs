using System;
using System.Threading;
using System.Threading.Tasks;
using Soenneker.Atomics.ValueBools;

namespace Soenneker.Dictionaries.SingletonKeys.LeasedExpiration;

/// <summary>
/// Represents an active lease for a singleton value.
/// </summary>
/// <typeparam name="TKey">The key type. Must be non-null.</typeparam>
/// <typeparam name="TValue">The leased value type.</typeparam>
public sealed class SingletonLease<TKey, TValue> : IDisposable, IAsyncDisposable where TKey : notnull
{
    private LeasedExpirationSingletonKeyDictionary<TKey, TValue>? _owner;
    private LeasedExpirationEntry<TKey, TValue>? _entry;
    private ValueAtomicBool _disposed;

    /// <summary>
    /// Gets the leased value. Do not use this value after the lease has been disposed.
    /// </summary>
    public TValue Value { get; }

    internal SingletonLease(TValue value, LeasedExpirationSingletonKeyDictionary<TKey, TValue> owner,
        LeasedExpirationEntry<TKey, TValue> entry)
    {
        Value = value;
        _owner = owner;
        _entry = entry;
    }

    /// <summary>
    /// Asynchronously releases the lease and allows the owning dictionary to dispose the value if it has expired and no leases remain.
    /// </summary>
    public ValueTask DisposeAsync()
    {
        if (!_disposed.TrySetTrue())
            return default;

        LeasedExpirationSingletonKeyDictionary<TKey, TValue>? owner = Interlocked.Exchange(ref _owner, null);
        LeasedExpirationEntry<TKey, TValue>? entry = Interlocked.Exchange(ref _entry, null);

        return owner is not null && entry is not null ? owner.ReleaseLease(entry) : default;
    }

    /// <summary>
    /// Releases the lease and allows the owning dictionary to dispose the value if it has expired and no leases remain.
    /// </summary>
    public void Dispose()
    {
        if (!_disposed.TrySetTrue())
            return;

        LeasedExpirationSingletonKeyDictionary<TKey, TValue>? owner = Interlocked.Exchange(ref _owner, null);
        LeasedExpirationEntry<TKey, TValue>? entry = Interlocked.Exchange(ref _entry, null);

        if (owner is not null && entry is not null)
            owner.ReleaseLeaseSync(entry);
    }
}
