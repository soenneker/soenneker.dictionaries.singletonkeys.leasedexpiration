using System;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Soenneker.Tests.Unit;

namespace Soenneker.Dictionaries.SingletonKeys.LeasedExpiration.Tests;

public sealed class LeasedExpirationSingletonKeyDictionaryTests : UnitTest
{
    [Test]
    public async Task GetLease_reuses_value_before_idle_expiration(CancellationToken cancellationToken)
    {
        var calls = 0;

        var dict = new LeasedExpirationSingletonKeyDictionary<string, object>(TimeSpan.FromMilliseconds(200), _ =>
        {
            Interlocked.Increment(ref calls);
            return new object();
        });

        object firstValue;

        await using (SingletonLease<string, object> first = await dict.GetLease("a", cancellationToken: cancellationToken))
        {
            firstValue = first.Value;
        }

        await Task.Delay(50);

        await using (SingletonLease<string, object> second = await dict.GetLease("a", cancellationToken: cancellationToken))
        {
            second.Value.Should().BeSameAs(firstValue);
        }

        calls.Should().Be(1);

        await dict.DisposeAsync();
    }

    [Test]
    public async Task Expiration_waits_for_active_lease(CancellationToken cancellationToken)
    {
        var calls = 0;
        var disposed = 0;

        var dict = new LeasedExpirationSingletonKeyDictionary<string, DisposableValue>(TimeSpan.FromMilliseconds(60),
            _ =>
            {
                Interlocked.Increment(ref calls);
                return new DisposableValue(() => Interlocked.Increment(ref disposed));
            });

        SingletonLease<string, DisposableValue> firstLease = await dict.GetLease("a", cancellationToken: cancellationToken);
        DisposableValue first = firstLease.Value;

        await Task.Delay(180);

        disposed.Should().Be(0);

        await firstLease.DisposeAsync();

        disposed.Should().Be(1);

        await using SingletonLease<string, DisposableValue> secondLease = await dict.GetLease("a", cancellationToken: cancellationToken);

        secondLease.Value.Should().NotBeSameAs(first);
        calls.Should().Be(2);

        await dict.DisposeAsync();
    }

    [Test]
    public async Task GetLease_resets_idle_expiration(CancellationToken cancellationToken)
    {
        var disposed = 0;

        var dict = new LeasedExpirationSingletonKeyDictionary<string, DisposableValue>(TimeSpan.FromMilliseconds(120),
            _ => new DisposableValue(() => Interlocked.Increment(ref disposed)));

        DisposableValue first;

        await using (SingletonLease<string, DisposableValue> lease = await dict.GetLease("a", cancellationToken: cancellationToken))
        {
            first = lease.Value;
        }

        await Task.Delay(80);

        await using (SingletonLease<string, DisposableValue> lease = await dict.GetLease("a", cancellationToken: cancellationToken))
        {
            lease.Value.Should().BeSameAs(first);
        }

        await Task.Delay(80);

        await using (SingletonLease<string, DisposableValue> lease = await dict.GetLease("a", cancellationToken: cancellationToken))
        {
            lease.Value.Should().BeSameAs(first);
        }

        disposed.Should().Be(0);

        await Task.Delay(180);

        disposed.Should().Be(1);

        await dict.DisposeAsync();
    }

    [Test]
    public async Task Remove_returns_false_while_value_is_leased(CancellationToken cancellationToken)
    {
        var disposed = 0;

        var dict = new LeasedExpirationSingletonKeyDictionary<string, DisposableValue>(TimeSpan.FromSeconds(5),
            _ => new DisposableValue(() => Interlocked.Increment(ref disposed)));

        SingletonLease<string, DisposableValue> lease = await dict.GetLease("a", cancellationToken: cancellationToken);

        bool removedWhileLeased = await dict.TryRemoveAndDispose("a");

        removedWhileLeased.Should().BeFalse();
        disposed.Should().Be(0);

        await lease.DisposeAsync();

        bool removedAfterRelease = await dict.TryRemoveAndDispose("a");

        removedAfterRelease.Should().BeTrue();
        disposed.Should().Be(1);

        await dict.DisposeAsync();
    }

    [Test]
    public async Task Different_keys_initialize_concurrently(CancellationToken cancellationToken)
    {
        var started = 0;
        var bothStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var dict = new LeasedExpirationSingletonKeyDictionary<string, string>(TimeSpan.FromSeconds(5), async key =>
        {
            if (Interlocked.Increment(ref started) == 2)
                bothStarted.TrySetResult();

            await bothStarted.Task;
            await release.Task;
            return key;
        });

        Task<SingletonLease<string, string>> first = GetLease("a");
        Task<SingletonLease<string, string>> second = GetLease("b");

        bool bothFactoriesStarted = await Task.WhenAny(bothStarted.Task, Task.Delay(TimeSpan.FromSeconds(1))) ==
                                    bothStarted.Task;

        bothStarted.TrySetResult();
        release.TrySetResult();

        bothFactoriesStarted.Should().BeTrue();

        SingletonLease<string, string>[] leases = await Task.WhenAll(first, second);

        try
        {
            leases[0].Value.Should().Be("a");
            leases[1].Value.Should().Be("b");
        }
        finally
        {
            await leases[0].DisposeAsync();
            await leases[1].DisposeAsync();
        }

        await dict.DisposeAsync();
        return;

        async Task<SingletonLease<string, string>> GetLease(string key)
        {
            return await dict.GetLease(key, cancellationToken: cancellationToken);
        }
    }

    [Test]
    public async Task Clear_defers_disposal_until_active_lease_is_released(CancellationToken cancellationToken)
    {
        var disposed = 0;
        var dict = new LeasedExpirationSingletonKeyDictionary<string, DisposableValue>(TimeSpan.FromMinutes(1),
            _ => new DisposableValue(() => Interlocked.Increment(ref disposed)));

        SingletonLease<string, DisposableValue> firstLease = await dict.GetLease("a", cancellationToken: cancellationToken);
        DisposableValue firstValue = firstLease.Value;

        await dict.Clear(cancellationToken: cancellationToken);
        disposed.Should().Be(0);

        SingletonLease<string, DisposableValue> replacementLease = await dict.GetLease("a", cancellationToken: cancellationToken);
        replacementLease.Value.Should().NotBeSameAs(firstValue);

        await firstLease.DisposeAsync();
        disposed.Should().Be(1);

        await dict.DisposeAsync();
        disposed.Should().Be(1);

        await replacementLease.DisposeAsync();
        disposed.Should().Be(2);
    }
}
