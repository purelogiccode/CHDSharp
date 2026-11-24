using Microsoft.Extensions.Logging;

namespace CHDSharp.Tests;

/// <summary>
///     Verifies that diagnostics about unusable input - a child CHD opened without its parent, or a
///     file whose header/map cannot be decoded - are tagged with the <c>UserError</c> scope. Host
///     applications use that tag to keep expected input problems out of their bug-report API.
/// </summary>
[Collection("Logging")]
public sealed class UserErrorLoggingTests
{
    private static readonly string TestDataDir = Path.Combine(AppContext.BaseDirectory, "TestData");

    [Fact]
    public void CheckFile_ChildChd_TagsWarningAsUserError()
    {
        var (result, entries) = CaptureCheck("v5_child.chd");

        Assert.Equal(ChdError.Chderrrequiresparent, result.Error);
        Assert.Contains(
            entries,
            e =>
                e.Level == LogLevel.Warning
                && e.Message.Contains("Child CHD found", StringComparison.Ordinal)
                && e.IsUserError
        );
    }

    [Fact]
    public void CheckFile_UnreadableMap_TagsWarningAsUserError()
    {
        var (result, entries) = CaptureCheck("v5_tiny.chd");

        Assert.NotEqual(ChdError.Chderrnone, result.Error);
        Assert.Contains(
            entries,
            e =>
                e.Level == LogLevel.Warning
                && e.Message.Contains("Header/map read failed", StringComparison.Ordinal)
                && e.IsUserError
        );
    }

    private static (ChdResult Result, List<Entry> Entries) CaptureCheck(string file)
    {
        var path = Path.Combine(TestDataDir, file);
        if (!File.Exists(path))
            Assert.Skip("Test data missing: " + path);

        var previous = Chd.LoggerFactory;
        var capture = new CaptureLoggerFactory();
        Chd.LoggerFactory = capture;
        try
        {
            using var fs = File.OpenRead(path);
            var result = Chd.CheckFile(fs, file, false);
            lock (capture.Entries)
                return (result, [.. capture.Entries]);
        }
        finally
        {
            Chd.LoggerFactory = previous;
        }
    }

    private sealed record Entry(LogLevel Level, string Message, bool IsUserError);

    private sealed class CaptureLoggerFactory : ILoggerFactory
    {
        public List<Entry> Entries { get; } = [];

        public void AddProvider(ILoggerProvider provider)
        {
            ArgumentNullException.ThrowIfNull(provider);
        }

        public ILogger CreateLogger(string categoryName)
        {
            ArgumentNullException.ThrowIfNull(categoryName);
            return new CaptureLogger(this);
        }

        public void Dispose() { }

        private sealed class CaptureLogger(CaptureLoggerFactory owner) : ILogger
        {
            private readonly Stack<bool> _userErrorScopes = new();

            public IDisposable BeginScope<TState>(TState state)
                where TState : notnull
            {
                var isUserError =
                    state is IEnumerable<KeyValuePair<string, object?>> properties
                    && properties.Any(p =>
                        string.Equals(p.Key, "UserError", StringComparison.Ordinal)
                        && p.Value is true
                    );
                _userErrorScopes.Push(isUserError);
                return new Scope(this);
            }

            public bool IsEnabled(LogLevel logLevel)
            {
                return true;
            }

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter
            )
            {
                var entry = new Entry(
                    logLevel,
                    formatter(state, exception),
                    _userErrorScopes.Contains(true)
                );
                lock (owner.Entries)
                    owner.Entries.Add(entry);
            }

            private sealed class Scope(CaptureLogger logger) : IDisposable
            {
                private bool _disposed;

                public void Dispose()
                {
                    if (_disposed)
                        return;

                    _disposed = true;
                    logger._userErrorScopes.Pop();
                }
            }
        }
    }
}
