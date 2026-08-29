using System;
using System.Diagnostics.Contracts;
using System.Threading;
using System.Threading.Tasks;
using Soenneker.Dictionaries.SingletonKeys.LeasedExpiration;

namespace Soenneker.Dictionaries.SingletonKeys.LeasedExpiration.Abstract;

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
    /// <param name="key">Key used to locate the target entry.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>A task whose result is the requested singleton Lease.</returns>
    [Pure]
    ValueTask<SingletonLease<TKey, TValue>> GetLease(TKey key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Synchronously retrieves a lease for the singleton value associated with <paramref name="key"/>, creating and caching it if it does not already exist.
    /// Successful retrieval resets that key's idle expiration.
    /// </summary>
    /// <param name="key">Key used to locate the target entry.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>The resulting singleton Lease.</returns>
    [Pure]
    SingletonLease<TKey, TValue> GetLeaseSync(TKey key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves lease.
    /// </summary>
    /// <typeparam name="TState">Type of state passed to the callback.</typeparam>
    /// <param name="state">State value used by the variant.</param>
    /// <param name="keyFactory">Function that derives a key from the supplied state.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>A task whose result is the requested singleton Lease.</returns>
    [Pure]
    ValueTask<SingletonLease<TKey, TValue>> GetLease<TState>(TState state, Func<TState, TKey> keyFactory, CancellationToken cancellationToken = default)
        where TState : notnull;

    /// <summary>
    /// Synchronously retrieves a lease for the singleton value associated with a key derived from <paramref name="state"/>.
    /// Successful retrieval resets that key's idle expiration.
    /// </summary>
    /// <typeparam name="TState">Type of state passed to the callback.</typeparam>
    /// <param name="state">State value used by the variant.</param>
    /// <param name="keyFactory">Function that derives a key from the supplied state.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>The resulting singleton Lease.</returns>
    [Pure]
    SingletonLease<TKey, TValue> GetLeaseSync<TState>(TState state, Func<TState, TKey> keyFactory, CancellationToken cancellationToken = default)
        where TState : notnull;

    /// <summary>
    /// Configures the stateful initialization function used to create values for missing keys.
    /// </summary>
    /// <typeparam name="TState">Type of state passed to the callback.</typeparam>
    /// <param name="state">State value used by the variant.</param>
    /// <param name="factory">Factory used to create a value when one is needed.</param>
    /// <returns>The resulting leased Expiration Singleton Key Dictionary.</returns>
    LeasedExpirationSingletonKeyDictionary<TKey, TValue> Initialize<TState>(TState state,
        Func<TState, TKey, CancellationToken, ValueTask<TValue>> factory) where TState : notnull;

    /// <summary>
    /// Sets the async initialization function used to create values for a key.
    /// </summary>
    /// <param name="func">Function to invoke.</param>
    void SetInitialization(Func<TKey, ValueTask<TValue>> func);

    /// <summary>
    /// Sets the async initialization function used to create values for a key, with cancellation support.
    /// </summary>
    /// <param name="func">Function to invoke.</param>
    void SetInitialization(Func<TKey, CancellationToken, ValueTask<TValue>> func);

    /// <summary>
    /// Sets the async initialization function used to create values without a key.
    /// </summary>
    /// <param name="func">Function to invoke.</param>
    void SetInitialization(Func<ValueTask<TValue>> func);

    /// <summary>
    /// Sets the synchronous initialization function used to create values without a key.
    /// </summary>
    /// <param name="func">Function to invoke.</param>
    void SetInitialization(Func<TValue> func);

    /// <summary>
    /// Sets the synchronous initialization function used to create values for a key.
    /// </summary>
    /// <param name="func">Function to invoke.</param>
    void SetInitialization(Func<TKey, TValue> func);

    /// <summary>
    /// Sets the synchronous initialization function used to create values for a key, with cancellation support.
    /// </summary>
    /// <param name="func">Function to invoke.</param>
    void SetInitialization(Func<TKey, CancellationToken, TValue> func);

    /// <summary>
    /// Removes the cached value without disposing it only when no leases are active.
    /// </summary>
    /// <param name="key">Key used to locate the target entry.</param>
    /// <param name="value">Value to test, add, or remove from the set.</param>
    /// <returns>true if removes the cached value without disposing it only when no leases are active; otherwise, false.</returns>
    bool TryRemove(TKey key, out TValue? value);

    /// <summary>
    /// Removes and disposes the cached value only when no leases are active.
    /// </summary>
    /// <param name="key">Key used to locate the target entry.</param>
    /// <returns>true if removes and disposes the cached value only when no leases are active; otherwise, false.</returns>
    ValueTask<bool> TryRemoveAndDispose(TKey key);

    /// <summary>
    /// Synchronously removes and disposes the cached value only when no leases are active.
    /// </summary>
    /// <param name="key">Key used to locate the target entry.</param>
    /// <returns>true if synchronously removes and disposes the cached value only when no leases are active; otherwise, false.</returns>
    bool TryRemoveAndDisposeSync(TKey key);

    /// <summary>
    /// Removes and disposes the cached value only when no leases are active.
    /// </summary>
    /// <param name="key">Key used to locate the target entry.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>true if removes and disposes the cached value only when no leases are active; otherwise, false.</returns>
    ValueTask<bool> Remove(TKey key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Synchronously removes and disposes the cached value only when no leases are active.
    /// </summary>
    /// <param name="key">Key used to locate the target entry.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>true if synchronously removes and disposes the cached value only when no leases are active; otherwise, false.</returns>
    bool RemoveSync(TKey key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Clears and disposes all cached values. Active leases may observe disposed values after this call.
    /// </summary>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>A task that completes when the Leased Expiration Singleton Key Dictionary has been cleared.</returns>
    ValueTask Clear(CancellationToken cancellationToken = default);

    /// <summary>
    /// Synchronously clears and disposes all cached values. Active leases may observe disposed values after this call.
    /// </summary>
    void ClearSync();
}
