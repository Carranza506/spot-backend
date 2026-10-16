using Microsoft.Extensions.Logging;

namespace Spot.Business.Api.Tests.Fakes;

/// <summary>Records log entries so tests can assert what was logged (same as Spot.AiSearch.Api.Tests').</summary>
public sealed class CapturingLogger<T> : ILogger<T>
{
    public sealed record Entry(LogLevel Level, string Message, Exception? Exception);

    public List<Entry> Entries { get; } = [];

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
        Entries.Add(new Entry(logLevel, formatter(state, exception), exception));
}
