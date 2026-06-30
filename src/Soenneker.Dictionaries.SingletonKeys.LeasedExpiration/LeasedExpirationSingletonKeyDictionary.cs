using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Soenneker.Asyncs.Locks;
using Soenneker.Atomics.ValueBools;
using Soenneker.Dictionaries.SingletonKeys.LeasedExpiration.Abstract;
using Soenneker.Extensions.ValueTask;

namespace Soenneker.Dictionaries.SingletonKeys.LeasedExpiration;

/// <summary>
/// A keyed singleton cache that returns leases and disposes values only after they are idle and no leases are active.
/// </summary>
/// <typeparam name="TKey">The key type. Must be non-null.</typeparam>
/// <typeparam name="TValue">The leased value type.</typeparam>
public class LeasedExpirationSingletonKeyDictionary<TKey, TValue> : ILeasedExpirationSingletonKeyDictionary<TKey, TValue>
    where TKey : notnull
{
    private readonly ConcurrentDictionary<TKey, LeasedExpirationEntry<TKey, TValue>> _entries = new();
    private readonly long _idleExpirationMilliseconds;
    private readonly Timer _sweepTimer;

    private Func<TKey, CancellationToken, ValueTask<TValue>>? _factory;
    private ValueAtomicBool _disposed;
    private ValueAtomicBool _sweeping;

    public TimeSpan IdleExpiration { get; }
    public TimeSpan SweepInterval { get; }

    public LeasedExpirationSingletonKeyDictionary(TimeSpan idleExpiration, TimeSpan? sweepInterval = null)
    {
        ValidateIdleExpiration(idleExpiration);

        IdleExpiration = idleExpiration;
        SweepInterval = sweepInterval ?? GetDefaultSweepInterval(idleExpiration);
        ValidateSweepInterval(SweepInterval);

        _idleExpirationMilliseconds = Math.Max(1L, (long)Math.Ceiling(idleExpiration.TotalMilliseconds));
        _sweepTimer = new Timer(static state =>
        {
            var dictionary = (LeasedExpirationSingletonKeyDictionary<TKey, TValue>)state!;
            dictionary.QueueSweep();
        }, this, SweepInterval, SweepInterval);
    }

    public LeasedExpirationSingletonKeyDictionary(TimeSpan idleExpiration, Func<TKey, ValueTask<TValue>> func,
        TimeSpan? sweepInterval = null) : this(idleExpiration, sweepInterval)
    {
        SetInitialization(func);
    }

    public LeasedExpirationSingletonKeyDictionary(TimeSpan idleExpiration,
        Func<TKey, CancellationToken, ValueTask<TValue>> func, TimeSpan? sweepInterval = null) : this(idleExpiration,
        sweepInterval)
    {
        SetInitialization(func);
    }

    public LeasedExpirationSingletonKeyDictionary(TimeSpan idleExpiration, Func<ValueTask<TValue>> func,
        TimeSpan? sweepInterval = null) : this(idleExpiration, sweepInterval)
    {
        SetInitialization(func);
    }

    public LeasedExpirationSingletonKeyDictionary(TimeSpan idleExpiration, Func<TKey, TValue> func,
        TimeSpan? sweepInterval = null) : this(idleExpiration, sweepInterval)
    {
        SetInitialization(func);
    }

    public LeasedExpirationSingletonKeyDictionary(TimeSpan idleExpiration, Func<TKey, CancellationToken, TValue> func,
        TimeSpan? sweepInterval = null) : this(idleExpiration, sweepInterval)
    {
        SetInitialization(func);
    }

    public LeasedExpirationSingletonKeyDictionary(TimeSpan idleExpiration, Func<TValue> func,
        TimeSpan? sweepInterval = null) : this(idleExpiration, sweepInterval)
    {
        SetInitialization(func);
    }

    public ValueTask<SingletonLease<TKey, TValue>> GetLease(TKey key, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        return TryGetLeaseFast(key, out SingletonLease<TKey, TValue>? lease)
            ? new ValueTask<SingletonLease<TKey, TValue>>(lease!)
            : GetLeaseSlow(key, cancellationToken);
    }

    private async ValueTask<SingletonLease<TKey, TValue>> GetLeaseSlow(TKey key, CancellationToken cancellationToken)
    {
        while (true)
        {
            ThrowIfDisposed();

            LeasedExpirationEntry<TKey, TValue> entry = GetOrAddEntry(key);

            using (await entry.Lock.Lock(cancellationToken).NoSync())
            {
                ThrowIfDisposed();

                if (!IsCurrentEntry(key, entry))
                    continue;

                await ExpireValueForReuseNoLock(entry).NoSync();

                try
                {
                    TValue value = await GetOrCreateNoLock(entry, key, cancellationToken).NoSync();
                    entry.LeaseCount++;
                    ResetExpirationNoLock(entry);
                    return new SingletonLease<TKey, TValue>(value, this, entry);
                }
                catch
                {
                    await RemoveEmptyEntryNoLock(entry.Key, entry).NoSync();
                    throw;
                }
            }
        }
    }

    public SingletonLease<TKey, TValue> GetLeaseSync(TKey key, CancellationToken cancellationToken = default)
    {
        while (true)
        {
            ThrowIfDisposed();

            LeasedExpirationEntry<TKey, TValue> entry = GetOrAddEntry(key);

            using (entry.Lock.LockSync(cancellationToken))
            {
                ThrowIfDisposed();

                if (!IsCurrentEntry(key, entry))
                    continue;

                ExpireValueForReuseNoLockSync(entry);

                try
                {
                    TValue value = GetOrCreateNoLock(entry, key, cancellationToken).AwaitSync();
                    entry.LeaseCount++;
                    ResetExpirationNoLock(entry);
                    return new SingletonLease<TKey, TValue>(value, this, entry);
                }
                catch
                {
                    RemoveEmptyEntryNoLockSync(key, entry);
                    throw;
                }
            }
        }
    }

    public ValueTask<SingletonLease<TKey, TValue>> GetLease<TState>(TState state, Func<TState, TKey> keyFactory,
        CancellationToken cancellationToken = default) where TState : notnull
    {
        TKey key = keyFactory(state);
        return GetLease(key, cancellationToken);
    }

    public SingletonLease<TKey, TValue> GetLeaseSync<TState>(TState state, Func<TState, TKey> keyFactory,
        CancellationToken cancellationToken = default) where TState : notnull
    {
        TKey key = keyFactory(state);
        return GetLeaseSync(key, cancellationToken);
    }

    private bool TryGetLeaseFast(TKey key, out SingletonLease<TKey, TValue>? lease)
    {
        lease = null;

        if (!_entries.TryGetValue(key, out LeasedExpirationEntry<TKey, TValue>? entry))
            return false;

        if (!entry.Lock.TryLock(out Releaser releaser))
            return false;

        using (releaser)
        {
            ThrowIfDisposed();

            if (!IsCurrentEntry(key, entry) || !entry.HasValue || IsExpired(entry))
                return false;

            entry.LeaseCount++;
            ResetExpirationNoLock(entry);
            lease = new SingletonLease<TKey, TValue>(entry.Value!, this, entry);
            return true;
        }
    }

    public LeasedExpirationSingletonKeyDictionary<TKey, TValue> Initialize<TState>(TState state,
        Func<TState, TKey, CancellationToken, ValueTask<TValue>> factory) where TState : notnull
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(factory);

        SetFactory((key, cancellationToken) => factory(state, key, cancellationToken));
        return this;
    }

    public void SetInitialization(Func<TKey, ValueTask<TValue>> func)
    {
        ArgumentNullException.ThrowIfNull(func);
        SetFactory((key, _) => func(key));
    }

    public void SetInitialization(Func<TKey, CancellationToken, ValueTask<TValue>> func)
    {
        ArgumentNullException.ThrowIfNull(func);
        SetFactory(func);
    }

    public void SetInitialization(Func<ValueTask<TValue>> func)
    {
        ArgumentNullException.ThrowIfNull(func);
        SetFactory((_, _) => func());
    }

    public void SetInitialization(Func<TValue> func)
    {
        ArgumentNullException.ThrowIfNull(func);
        SetFactory((_, _) => new ValueTask<TValue>(func()));
    }

    public void SetInitialization(Func<TKey, TValue> func)
    {
        ArgumentNullException.ThrowIfNull(func);
        SetFactory((key, _) => new ValueTask<TValue>(func(key)));
    }

    public void SetInitialization(Func<TKey, CancellationToken, TValue> func)
    {
        ArgumentNullException.ThrowIfNull(func);
        SetFactory((key, cancellationToken) => new ValueTask<TValue>(func(key, cancellationToken)));
    }

    public bool TryRemove(TKey key, out TValue? value)
    {
        value = default;
        ThrowIfDisposed();

        if (!_entries.TryGetValue(key, out LeasedExpirationEntry<TKey, TValue>? entry))
            return false;

        using (entry.Lock.LockSync())
        {
            ThrowIfDisposed();

            if (!IsCurrentEntry(key, entry) || entry.LeaseCount > 0)
                return false;

            if (!TryRemoveEntry(key, entry))
                return false;

            return entry.TryTakeValue(out value);
        }
    }

    public ValueTask<bool> TryRemoveAndDispose(TKey key)
    {
        ThrowIfDisposed();

        if (!_entries.TryGetValue(key, out LeasedExpirationEntry<TKey, TValue>? entry))
            return new ValueTask<bool>(false);

        return TryRemoveAndDispose(key, entry);
    }

    private async ValueTask<bool> TryRemoveAndDispose(TKey key, LeasedExpirationEntry<TKey, TValue> entry)
    {
        TValue? value;

        using (await entry.Lock.Lock(CancellationToken.None).NoSync())
        {
            ThrowIfDisposed();

            if (!IsCurrentEntry(key, entry) || entry.LeaseCount > 0)
                return false;

            if (!TryRemoveEntry(key, entry))
                return false;

            if (!entry.TryTakeValue(out value))
                return false;
        }

        await DisposeValue(value).NoSync();
        return true;
    }

    public bool TryRemoveAndDisposeSync(TKey key)
    {
        ThrowIfDisposed();

        if (!_entries.TryGetValue(key, out LeasedExpirationEntry<TKey, TValue>? entry))
            return false;

        TValue? value;

        using (entry.Lock.LockSync())
        {
            ThrowIfDisposed();

            if (!IsCurrentEntry(key, entry) || entry.LeaseCount > 0)
                return false;

            if (!TryRemoveEntry(key, entry))
                return false;

            if (!entry.TryTakeValue(out value))
                return false;
        }

        DisposeValueSync(value);
        return true;
    }

    public ValueTask<bool> Remove(TKey key, CancellationToken cancellationToken = default) => TryRemoveAndDispose(key);

    public bool RemoveSync(TKey key, CancellationToken cancellationToken = default) => TryRemoveAndDisposeSync(key);

    public ValueTask Clear(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return _entries.IsEmpty ? default : ClearEntries(cancellationToken);
    }

    public void ClearSync()
    {
        ThrowIfDisposed();
        ClearEntriesSync();
    }

    public void Dispose()
    {
        if (!_disposed.TrySetTrue())
            return;

        _sweepTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        _sweepTimer.Dispose();
        ClearEntriesSync();
    }

    public async ValueTask DisposeAsync()
    {
        if (!_disposed.TrySetTrue())
            return;

        _sweepTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        await _sweepTimer.DisposeAsync().NoSync();

        if (!_entries.IsEmpty)
            await ClearEntries(CancellationToken.None).NoSync();
    }

    private void QueueSweep()
    {
        if (_disposed.Value || !_sweeping.TrySetTrue())
            return;

        _ = SweepFromTimer();
    }

    private ValueTask<TValue> GetOrCreateNoLock(LeasedExpirationEntry<TKey, TValue> entry, TKey key,
        CancellationToken cancellationToken)
    {
        if (entry.HasValue)
            return new ValueTask<TValue>(entry.Value!);

        return CreateNoLock(entry, key, cancellationToken);
    }

    private async ValueTask<TValue> CreateNoLock(LeasedExpirationEntry<TKey, TValue> entry, TKey key,
        CancellationToken cancellationToken)
    {
        Func<TKey, CancellationToken, ValueTask<TValue>>? factory = _factory;

        if (factory is null)
            throw new InvalidOperationException(
                "Initialization func for LeasedExpirationSingletonKeyDictionary cannot be null");

        TValue value = await factory(key, cancellationToken).NoSync();

        entry.Value = value;
        entry.HasValue = true;

        return value;
    }

    private void SetFactory(Func<TKey, CancellationToken, ValueTask<TValue>> factory)
    {
        ThrowIfDisposed();

        if (Interlocked.CompareExchange(ref _factory, factory, null) is not null)
            throw new InvalidOperationException(
                "Setting the initialization of a LeasedExpirationSingletonKeyDictionary after it has already been set is not allowed");
    }

    private LeasedExpirationEntry<TKey, TValue> GetOrAddEntry(TKey key)
    {
        return _entries.GetOrAdd(key, static k => new LeasedExpirationEntry<TKey, TValue>(k));
    }

    private void ResetExpirationNoLock(LeasedExpirationEntry<TKey, TValue> entry)
    {
        entry.ExpirationPending = false;
        entry.ExpiresAtTick = Environment.TickCount64 + _idleExpirationMilliseconds;
    }

    private ValueTask ExpireValueForReuseNoLock(LeasedExpirationEntry<TKey, TValue> entry)
    {
        if (!entry.HasValue || entry.LeaseCount > 0 || !IsExpired(entry))
            return default;

        return entry.TryTakeValue(out TValue? value) ? DisposeValue(value) : default;
    }

    private void ExpireValueForReuseNoLockSync(LeasedExpirationEntry<TKey, TValue> entry)
    {
        if (!entry.HasValue || entry.LeaseCount > 0 || !IsExpired(entry))
            return;

        if (entry.TryTakeValue(out TValue? value))
            DisposeValueSync(value);
    }

    private ValueTask RemoveEmptyEntryNoLock(TKey key, LeasedExpirationEntry<TKey, TValue> entry)
    {
        if (entry.HasValue || entry.LeaseCount > 0)
            return default;

        TryRemoveEntry(key, entry);
        return default;
    }

    private void RemoveEmptyEntryNoLockSync(TKey key, LeasedExpirationEntry<TKey, TValue> entry)
    {
        if (entry.HasValue || entry.LeaseCount > 0)
            return;

        TryRemoveEntry(key, entry);
    }

    internal ValueTask ReleaseLease(LeasedExpirationEntry<TKey, TValue> entry)
    {
        return entry.Lock.TryLock(out Releaser releaser) ? ReleaseLeaseFast(entry, releaser) : ReleaseLeaseSlow(entry);
    }

    private ValueTask ReleaseLeaseFast(LeasedExpirationEntry<TKey, TValue> entry, Releaser releaser)
    {
        TValue? value = default;
        var shouldDispose = false;

        using (releaser)
        {
            if (entry.LeaseCount <= 0)
                return default;

            entry.LeaseCount--;

            if (_disposed.Value || !IsCurrentEntry(entry.Key, entry) || !entry.HasValue)
                return default;

            if (entry.LeaseCount > 0 || (!entry.ExpirationPending && !IsExpired(entry)))
                return default;

            if (!TryRemoveEntry(entry.Key, entry))
                return default;

            shouldDispose = entry.TryTakeValue(out value);
        }

        return shouldDispose ? DisposeValue(value) : default;
    }

    private async ValueTask ReleaseLeaseSlow(LeasedExpirationEntry<TKey, TValue> entry)
    {
        TValue? value = default;
        var shouldDispose = false;

        using (await entry.Lock.Lock(CancellationToken.None).NoSync())
        {
            if (entry.LeaseCount <= 0)
                return;

            entry.LeaseCount--;

            if (_disposed.Value || !IsCurrentEntry(entry.Key, entry) || !entry.HasValue)
                return;

            if (entry.LeaseCount > 0 || (!entry.ExpirationPending && !IsExpired(entry)))
                return;

            if (!TryRemoveEntry(entry.Key, entry))
                return;

            shouldDispose = entry.TryTakeValue(out value);
        }

        if (shouldDispose)
            await DisposeValue(value).NoSync();
    }

    internal void ReleaseLeaseSync(LeasedExpirationEntry<TKey, TValue> entry)
    {
        TValue? value = default;
        var shouldDispose = false;

        using (entry.Lock.LockSync())
        {
            if (entry.LeaseCount <= 0)
                return;

            entry.LeaseCount--;

            if (_disposed.Value || !IsCurrentEntry(entry.Key, entry) || !entry.HasValue)
                return;

            if (entry.LeaseCount > 0 || (!entry.ExpirationPending && !IsExpired(entry)))
                return;

            if (!TryRemoveEntry(entry.Key, entry))
                return;

            shouldDispose = entry.TryTakeValue(out value);
        }

        if (shouldDispose)
            DisposeValueSync(value);
    }

    private async ValueTask ClearEntries(CancellationToken cancellationToken)
    {
        foreach (KeyValuePair<TKey, LeasedExpirationEntry<TKey, TValue>> kvp in _entries)
        {
            LeasedExpirationEntry<TKey, TValue> entry = kvp.Value;
            TValue? value;
            bool shouldDispose;

            using (await entry.Lock.Lock(cancellationToken).NoSync())
            {
                if (!TryRemoveEntry(kvp.Key, entry))
                    continue;

                shouldDispose = entry.TryTakeValue(out value);
            }

            if (shouldDispose)
                await DisposeValue(value).NoSync();
        }
    }

    private void ClearEntriesSync()
    {
        foreach (KeyValuePair<TKey, LeasedExpirationEntry<TKey, TValue>> kvp in _entries)
        {
            LeasedExpirationEntry<TKey, TValue> entry = kvp.Value;
            TValue? value;
            bool shouldDispose;

            using (entry.Lock.LockSync())
            {
                if (!TryRemoveEntry(kvp.Key, entry))
                    continue;

                shouldDispose = entry.TryTakeValue(out value);
            }

            if (shouldDispose)
                DisposeValueSync(value);
        }
    }

    private async ValueTask SweepFromTimer()
    {
        try
        {
            await SweepExpired(CancellationToken.None).NoSync();
        }
        catch
        {
            // Timer callbacks cannot surface failures to callers.
        }
        finally
        {
            _sweeping.TrySetFalse();
        }
    }

    private async ValueTask SweepExpired(CancellationToken cancellationToken)
    {
        long now = Environment.TickCount64;

        foreach (KeyValuePair<TKey, LeasedExpirationEntry<TKey, TValue>> kvp in _entries)
        {
            if (_disposed.Value)
                return;

            LeasedExpirationEntry<TKey, TValue> entry = kvp.Value;

            if (entry.ExpiresAtTick > now && entry.HasValue)
                continue;

            TValue? value = default;
            var shouldDispose = false;

            using (await entry.Lock.Lock(cancellationToken).NoSync())
            {
                if (!IsCurrentEntry(kvp.Key, entry))
                    continue;

                if (!entry.HasValue)
                {
                    if (entry.LeaseCount <= 0)
                        TryRemoveEntry(kvp.Key, entry);

                    continue;
                }

                if (entry.ExpiresAtTick > now)
                    continue;

                if (entry.LeaseCount > 0)
                {
                    entry.ExpirationPending = true;
                    continue;
                }

                if (!TryRemoveEntry(kvp.Key, entry))
                    continue;

                shouldDispose = entry.TryTakeValue(out value);
            }

            if (shouldDispose)
                await DisposeValue(value).NoSync();
        }
    }

    private bool IsCurrentEntry(TKey key, LeasedExpirationEntry<TKey, TValue> entry)
    {
        return _entries.TryGetValue(key, out LeasedExpirationEntry<TKey, TValue>? current) &&
               ReferenceEquals(current, entry);
    }

    private bool TryRemoveEntry(TKey key, LeasedExpirationEntry<TKey, TValue> entry)
    {
        return ((ICollection<KeyValuePair<TKey, LeasedExpirationEntry<TKey, TValue>>>)_entries).Remove(
            new KeyValuePair<TKey, LeasedExpirationEntry<TKey, TValue>>(key, entry));
    }

    private static bool IsExpired(LeasedExpirationEntry<TKey, TValue> entry)
    {
        return entry.ExpiresAtTick <= Environment.TickCount64;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed.Value)
            throw new ObjectDisposedException(nameof(LeasedExpirationSingletonKeyDictionary<TKey, TValue>));
    }

    private static void ValidateIdleExpiration(TimeSpan idleExpiration)
    {
        if (idleExpiration <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(idleExpiration), "Idle expiration must be greater than zero.");
    }

    private static void ValidateSweepInterval(TimeSpan sweepInterval)
    {
        if (sweepInterval <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(sweepInterval), "Sweep interval must be greater than zero.");
    }

    private static TimeSpan GetDefaultSweepInterval(TimeSpan idleExpiration)
    {
        double milliseconds = Math.Clamp(idleExpiration.TotalMilliseconds / 4D, 10D, 60_000D);
        return TimeSpan.FromMilliseconds(milliseconds);
    }

    private static void DisposeValueSync(TValue? value)
    {
        switch (value)
        {
            case IAsyncDisposable asyncDisposable:
                asyncDisposable.DisposeAsync().AwaitSync();
                break;
            case IDisposable disposable:
                disposable.Dispose();
                break;
        }
    }

    private static ValueTask DisposeValue(TValue? value)
    {
        switch (value)
        {
            case IAsyncDisposable asyncDisposable:
                return asyncDisposable.DisposeAsync();
            case IDisposable disposable:
                disposable.Dispose();
                break;
        }

        return default;
    }
}
