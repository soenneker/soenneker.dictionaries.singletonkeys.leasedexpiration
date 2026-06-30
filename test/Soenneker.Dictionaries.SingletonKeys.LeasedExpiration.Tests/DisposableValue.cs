using System;

namespace Soenneker.Dictionaries.SingletonKeys.LeasedExpiration.Tests;

internal sealed class DisposableValue : IDisposable
{
    private readonly Action _dispose;

    public DisposableValue(Action dispose)
    {
        _dispose = dispose;
    }

    public void Dispose()
    {
        _dispose();
    }
}
