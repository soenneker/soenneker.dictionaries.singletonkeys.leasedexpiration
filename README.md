[![](https://img.shields.io/nuget/v/soenneker.dictionaries.singletonkeys.leasedexpiration.svg?style=for-the-badge)](https://www.nuget.org/packages/soenneker.dictionaries.singletonkeys.leasedexpiration/)
[![](https://img.shields.io/github/actions/workflow/status/soenneker/soenneker.dictionaries.singletonkeys.leasedexpiration/publish-package.yml?style=for-the-badge)](https://github.com/soenneker/soenneker.dictionaries.singletonkeys.leasedexpiration/actions/workflows/publish-package.yml)
[![](https://img.shields.io/nuget/dt/soenneker.dictionaries.singletonkeys.leasedexpiration.svg?style=for-the-badge)](https://www.nuget.org/packages/soenneker.dictionaries.singletonkeys.leasedexpiration/)

# Soenneker.Dictionaries.SingletonKeys.LeasedExpiration

Defines leased access to singleton values with idle-expiration semantics.

## Install

```bash
dotnet add package Soenneker.Dictionaries.SingletonKeys.LeasedExpiration
```

## Quick start

```csharp
using Soenneker.Dictionaries.SingletonKeys.LeasedExpiration.Abstract;

ILeasedExpirationSingletonKeyDictionary<TKey, TValue> leasedExpirationSingletonKeyDictionary = /* resolve from DI */;
var result = await leasedExpirationSingletonKeyDictionary.GetLease(/* supply key */ default!, default);
```

Retrieves a lease for the singleton value associated with `key`, creating and caching it if it does not already exist. Successful retrieval resets that key's idle expiration.

## What you get

- `ILeasedExpirationSingletonKeyDictionary<TKey, TValue>` — Defines leased access to singleton values with idle-expiration semantics.

## API at a glance

| API | What it does | Result / important behavior |
| --- | --- | --- |
| `ILeasedExpirationSingletonKeyDictionary<TKey, TValue>.IdleExpiration` | Gets the idle duration after which a cached value is evicted when it has not been leased. | Gets the idle duration after which a cached value is evicted when it has not been leased. |
| `ILeasedExpirationSingletonKeyDictionary<TKey, TValue>.SweepInterval` | Gets the interval used by the dictionary-wide sweeper to scan for expired idle entries. | Gets the interval used by the dictionary-wide sweeper to scan for expired idle entries. |
| `ILeasedExpirationSingletonKeyDictionary<TKey, TValue>.GetLease(state, keyFactory, cancellationToken)` | Retrieves lease. | A task whose result is the requested singleton Lease. |
| `ILeasedExpirationSingletonKeyDictionary<TKey, TValue>.Initialize(state, factory)` | Configures the stateful initialization function used to create values for missing keys. | The resulting leased Expiration Singleton Key Dictionary. |
| `ILeasedExpirationSingletonKeyDictionary<TKey, TValue>.SetInitialization(func)` | Sets the async initialization function used to create values for a key. | Returns no value; the requested change is complete when the method returns. |
| `ILeasedExpirationSingletonKeyDictionary<TKey, TValue>.TryRemove(key, value)` | Removes the cached value without disposing it only when no leases are active. | true if removes the cached value without disposing it only when no leases are active; otherwise, false. |
| `ILeasedExpirationSingletonKeyDictionary<TKey, TValue>.TryRemoveAndDispose(key)` | Removes and disposes the cached value only when no leases are active. | true if removes and disposes the cached value only when no leases are active; otherwise, false. |
| `ILeasedExpirationSingletonKeyDictionary<TKey, TValue>.TryRemoveAndDisposeSync(key)` | Synchronously removes and disposes the cached value only when no leases are active. | true if synchronously removes and disposes the cached value only when no leases are active; otherwise, false. |
| `ILeasedExpirationSingletonKeyDictionary<TKey, TValue>.Remove(key, cancellationToken)` | Removes and disposes the cached value only when no leases are active. | true if removes and disposes the cached value only when no leases are active; otherwise, false. |
| `ILeasedExpirationSingletonKeyDictionary<TKey, TValue>.RemoveSync(key, cancellationToken)` | Synchronously removes and disposes the cached value only when no leases are active. | true if synchronously removes and disposes the cached value only when no leases are active; otherwise, false. |
| `ILeasedExpirationSingletonKeyDictionary<TKey, TValue>.Clear(cancellationToken)` | Clears and disposes all cached values. Active leases may observe disposed values after this call. | A task that completes when the Leased Expiration Singleton Key Dictionary has been cleared. |
| `ILeasedExpirationSingletonKeyDictionary<TKey, TValue>.ClearSync()` | Synchronously clears and disposes all cached values. Active leases may observe disposed values after this call. | Returns no value; the requested change is complete when the method returns. |

## Practical notes

- Cancellation stops pending work; it does not undo work that has already completed.
- Calls that return a cached or singleton value reuse the same instance until the owning service is disposed.
- Dispose instances you own when their scope ends so held resources can be released.
