using System;
using Microsoft.Extensions.Options;

namespace SteamInputBridge.Tests;

internal sealed class TestOptionsMonitor<T>(T currentValue) : IOptionsMonitor<T>
{
    private event Action<T, string?>? Changed;

    public T CurrentValue { get; private set; } = currentValue;

    public T Get(string? name)
    {
        _ = name;
        return CurrentValue;
    }

    public IDisposable OnChange(Action<T, string?> listener)
    {
        Changed += listener;
        return new Subscription(() => Changed -= listener);
    }

    public void Set(T value)
    {
        CurrentValue = value;
        Changed?.Invoke(value, Options.DefaultName);
    }

    private sealed class Subscription(Action dispose) : IDisposable
    {
        public void Dispose()
        {
            dispose();
        }
    }
}
