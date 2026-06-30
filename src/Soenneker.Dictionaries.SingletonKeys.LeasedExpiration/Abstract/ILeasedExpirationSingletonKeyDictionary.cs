using System;
using System.Diagnostics.Contracts;
using System.Threading;
using System.Threading.Tasks;
using Soenneker.Dictionaries.SingletonKeys.LeasedExpiration;

namespace Soenneker.Dictionaries.SingletonKeys.LeasedExpiration.Abstract;

/// <summary>
/// A keyed singleton cache that returns leases and disposes values only after they are idle and no leases are active.
/// </summary>
/// <typeparam name="TKey">The key type. Must be non-null.</typeparam>
/// <typeparam name="TValue">The leased value type.</typeparam>
public interface ILeasedExpirationSingletonKeyDictionary<TKey, TValue> : IDisposable, IAsyncDisposable where TKey : notnull
{
    /// <summary>
    /// Gets the idle duration after which a cached value is evicted when it has not been leased.
    /// </summary>
    TimeSpan IdleExpiration { get; }

    /// <summary>
    /// Gets the interval used by the dictionary-wide sweeper to scan for expired idle entries.
    /// </summary>
    TimeSpan SweepInterval { get; }

    /// <summary>
    /// Retrieves a lease for the singleton value associated with <paramref name="key"/>, creating and caching it if it does not already exist.
    /// Successful retrieval resets that key's idle expiration.
    /// </summary>
    [Pure]
    ValueTask<SingletonLease<TKey, TValue>> GetLease(TKey key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Synchronously retrieves a lease for the singleton value associated with <paramref name="key"/>, creating and caching it if it does not already exist.
    /// Successful retrieval resets that key's idle expiration.
    /// </summary>
    [Pure]
    SingletonLease<TKey, TValue> GetLeaseSync(TKey key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves a lease for the singleton value associated with a key derived from <paramref name="state"/>.
    /// Successful retrieval resets that key's idle expiration.
    /// </summary>
    [Pure]
    ValueTask<SingletonLease<TKey, TValue>> GetLease<TState>(TState state, Func<TState, TKey> keyFactory, CancellationToken cancellationToken = default)
        where TState : notnull;

    /// <summary>
    /// Synchronously retrieves a lease for the singleton value associated with a key derived from <paramref name="state"/>.
    /// Successful retrieval resets that key's idle expiration.
    /// </summary>
    [Pure]
    SingletonLease<TKey, TValue> GetLeaseSync<TState>(TState state, Func<TState, TKey> keyFactory, CancellationToken cancellationToken = default)
        where TState : notnull;

    /// <summary>
    /// Configures the stateful initialization function used to create values for missing keys.
    /// </summary>
    LeasedExpirationSingletonKeyDictionary<TKey, TValue> Initialize<TState>(TState state,
        Func<TState, TKey, CancellationToken, ValueTask<TValue>> factory) where TState : notnull;

    /// <summary>
    /// Sets the async initialization function used to create values for a key.
    /// </summary>
    void SetInitialization(Func<TKey, ValueTask<TValue>> func);

    /// <summary>
    /// Sets the async initialization function used to create values for a key, with cancellation support.
    /// </summary>
    void SetInitialization(Func<TKey, CancellationToken, ValueTask<TValue>> func);

    /// <summary>
    /// Sets the async initialization function used to create values without a key.
    /// </summary>
    void SetInitialization(Func<ValueTask<TValue>> func);

    /// <summary>
    /// Sets the synchronous initialization function used to create values without a key.
    /// </summary>
    void SetInitialization(Func<TValue> func);

    /// <summary>
    /// Sets the synchronous initialization function used to create values for a key.
    /// </summary>
    void SetInitialization(Func<TKey, TValue> func);

    /// <summary>
    /// Sets the synchronous initialization function used to create values for a key, with cancellation support.
    /// </summary>
    void SetInitialization(Func<TKey, CancellationToken, TValue> func);

    /// <summary>
    /// Removes the cached value without disposing it only when no leases are active.
    /// </summary>
    bool TryRemove(TKey key, out TValue? value);

    /// <summary>
    /// Removes and disposes the cached value only when no leases are active.
    /// </summary>
    ValueTask<bool> TryRemoveAndDispose(TKey key);

    /// <summary>
    /// Synchronously removes and disposes the cached value only when no leases are active.
    /// </summary>
    bool TryRemoveAndDisposeSync(TKey key);

    /// <summary>
    /// Removes and disposes the cached value only when no leases are active.
    /// </summary>
    ValueTask<bool> Remove(TKey key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Synchronously removes and disposes the cached value only when no leases are active.
    /// </summary>
    bool RemoveSync(TKey key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Clears and disposes all cached values. Active leases may observe disposed values after this call.
    /// </summary>
    ValueTask Clear(CancellationToken cancellationToken = default);

    /// <summary>
    /// Synchronously clears and disposes all cached values. Active leases may observe disposed values after this call.
    /// </summary>
    void ClearSync();
}
