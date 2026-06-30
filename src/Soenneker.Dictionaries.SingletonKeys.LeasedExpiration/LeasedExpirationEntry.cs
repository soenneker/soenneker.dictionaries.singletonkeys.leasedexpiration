using Soenneker.Asyncs.Locks;

namespace Soenneker.Dictionaries.SingletonKeys.LeasedExpiration;

internal sealed class LeasedExpirationEntry<TKey, TValue> where TKey : notnull
{
    public readonly TKey Key;
    public readonly AsyncLock Lock;

    public TValue? Value;
    public bool HasValue;
    public int LeaseCount;
    public long ExpiresAtTick;
    public bool ExpirationPending;

    public LeasedExpirationEntry(TKey key)
    {
        Key = key;
        Lock = new AsyncLock();
    }

    public bool TryTakeValue(out TValue? value)
    {
        if (!HasValue)
        {
            value = default;
            return false;
        }

        value = Value;
        Value = default;
        HasValue = false;
        ExpirationPending = false;
        return true;
    }
}
