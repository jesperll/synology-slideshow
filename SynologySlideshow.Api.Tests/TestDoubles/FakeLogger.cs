using Microsoft.Extensions.Logging;

namespace SynologySlideshow.Api.Tests.TestDoubles;

public class FakeLogger<T> : ILogger<T>
{
    public record Entry(LogLevel LogLevel, string Message, Exception? Exception);

    public List<Entry> Entries { get; } = new();

    public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        Entries.Add(new Entry(logLevel, formatter(state, exception), exception));
    }

    private class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();
        public void Dispose()
        {
        }
    }
}
