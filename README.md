[![](https://img.shields.io/nuget/v/soenneker.dictionaries.singletonkeys.leasedexpiration.svg?style=for-the-badge)](https://www.nuget.org/packages/soenneker.dictionaries.singletonkeys.leasedexpiration/)
[![](https://img.shields.io/github/actions/workflow/status/soenneker/soenneker.dictionaries.singletonkeys.leasedexpiration/publish-package.yml?style=for-the-badge)](https://github.com/soenneker/soenneker.dictionaries.singletonkeys.leasedexpiration/actions/workflows/publish-package.yml)
[![](https://img.shields.io/nuget/dt/soenneker.dictionaries.singletonkeys.leasedexpiration.svg?style=for-the-badge)](https://www.nuget.org/packages/soenneker.dictionaries.singletonkeys.leasedexpiration/)
[![](https://img.shields.io/github/actions/workflow/status/soenneker/soenneker.dictionaries.singletonkeys.leasedexpiration/codeql.yml?label=CodeQL&style=for-the-badge)](https://github.com/soenneker/soenneker.dictionaries.singletonkeys.leasedexpiration/actions/workflows/codeql.yml)

# Soenneker.Dictionaries.SingletonKeys.LeasedExpiration

A keyed singleton cache with idle expiration and leases that prevent a value from being disposed while it is in use.

## Installation

```bash
dotnet add package Soenneker.Dictionaries.SingletonKeys.LeasedExpiration
```

## Usage

```csharp
using Soenneker.Dictionaries.SingletonKeys.LeasedExpiration;

await using var clients = new LeasedExpirationSingletonKeyDictionary<string, ApiClient>(
    idleExpiration: TimeSpan.FromMinutes(10),
    func: (tenantId, cancellationToken) => ApiClient.Connect(tenantId, cancellationToken));

await using SingletonLease<string, ApiClient> lease =
    await clients.GetLease("tenant-42", cancellationToken);

await lease.Value.Send(request, cancellationToken);
```

Always keep the lease alive for the entire time the value is used. Do not retain or use `lease.Value` after the lease has been disposed.

Concurrent requests for one missing key share a single factory execution. Different keys can initialize concurrently. A failed or canceled factory leaves no cached value, so a later request can retry.

## Idle expiration

Each successful `GetLease` resets the key’s idle deadline. The deadline is measured from lease acquisition, not release. If the deadline passes while one or more leases are active, disposal is deferred until the last lease is released.

A dictionary-wide sweep discovers idle entries. Actual eviction can occur up to roughly one `SweepInterval` after the deadline. Requesting an already expired key also disposes its old value and creates a replacement without waiting for the next sweep.

```csharp
var clients = new LeasedExpirationSingletonKeyDictionary<string, ApiClient>(
    idleExpiration: TimeSpan.FromMinutes(10),
    sweepInterval: TimeSpan.FromSeconds(30));

clients.SetInitialization((key, cancellationToken) =>
    ApiClient.Connect(key, cancellationToken));
```

Configure the initialization function once, before concurrent use. `Initialize(state, static ...)` is available when avoiding a closure matters.

## Removal and clearing

`Remove` and `TryRemoveAndDispose` return `false` while a key has active leases. `TryRemove(key, out value)` also requires zero active leases and transfers ownership without disposing the value.

`Clear` detaches all current entries immediately. New leases can create replacements, while values from detached entries remain valid until their existing leases are released. Dictionary disposal follows the same lease-safe rule: it stops new acquisitions and the sweeper, but active values are disposed only after their last lease ends.

The cache owns its values and prefers `IAsyncDisposable` over `IDisposable`. Use async lease and dictionary disposal when values have asynchronous cleanup.
