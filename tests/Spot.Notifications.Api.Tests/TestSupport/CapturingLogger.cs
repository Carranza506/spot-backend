using Microsoft.Extensions.Logging;

namespace Spot.Notifications.Api.Tests.TestSupport;

/// <summary>
/// Keeps every log entry in memory so tests can assert what was logged (and that secrets weren't).
/// Same as Spot.AiSearch.Api.Tests' CapturingLogger.
/// </summary>
public sealed class CapturingLogger<T> : ILogger<T>
{
    public sealed record Entry(LogLevel Level, string Message, Exception? Exception);

    public List<Entry> Entries { get; } = [];

    /// <summary>Every message and exception text, to search for anything that shouldn't be there.</summary>
    public string AllText => string.Join("\n", Entries.Select(e => $"{e.Message} {e.Exception}"));

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        => Entries.Add(new Entry(logLevel, formatter(state, exception), exception));
}
