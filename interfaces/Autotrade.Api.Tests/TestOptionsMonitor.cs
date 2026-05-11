using Microsoft.Extensions.Options;

namespace Autotrade.Api.Tests;

internal sealed class TestOptionsMonitor<TOptions>(TOptions currentValue) : IOptionsMonitor<TOptions>
    where TOptions : class
{
    public TOptions CurrentValue { get; private set; } = currentValue;

    public TOptions Get(string? name)
    {
        return CurrentValue;
    }

    public IDisposable? OnChange(Action<TOptions, string?> listener)
    {
        return null;
    }

    public void SetCurrentValue(TOptions value)
    {
        CurrentValue = value;
    }
}
