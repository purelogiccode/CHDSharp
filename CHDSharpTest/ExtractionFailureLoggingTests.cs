using Microsoft.Extensions.Logging;

namespace CHDSharp.Tests;

public sealed class ExtractionFailureLoggingTests
{
    private static readonly string TestDataDir = Path.Combine(AppContext.BaseDirectory, "TestData");

    [Fact]
    public void ExtractToDirectoryWithReporting_UnwritableTarget_LogsRealException()
    {
        var outputDir = Path.Combine(Path.GetTempPath(), $"chd_extract_fail_{Guid.NewGuid():N}");
        var previous = Chd.LoggerFactory;
        var capture = new CaptureLoggerFactory();
        Chd.LoggerFactory = capture;
        try
        {
            Directory.CreateDirectory(outputDir);
            // A directory where the output BIN must go makes FileStream creation fail.
            Directory.CreateDirectory(Path.Combine(outputDir, "test.bin"));

            var err = ChdFile.Open(Path.Combine(TestDataDir, "v5_cd_default.chd"), out var chd);
            Assert.Equal(ChdError.Chderrnone, err);
            using (chd)
            {
                var result = chd!.ExtractToDirectoryWithReporting(
                    outputDir,
                    "test",
                    cooked: true
                );
                Assert.Equal(ChdError.Chderrwriteerror, result.Error);
            }

            lock (capture.Entries)
            {
                Assert.Contains(
                    capture.Entries,
                    e =>
                        e.Level == LogLevel.Warning
                        && e.Message.Contains(
                            "Failed to write extracted data",
                            StringComparison.Ordinal
                        )
                        && e.Exception is not null
                );
            }
        }
        finally
        {
            Chd.LoggerFactory = previous;
            try
            {
                if (Directory.Exists(outputDir))
                    Directory.Delete(outputDir, true);
            }
            catch
            {
                // ignored
            }
        }
    }

    private sealed class CaptureLoggerFactory : ILoggerFactory
    {
        public List<(LogLevel Level, string Message, Exception? Exception)> Entries { get; } = [];

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
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull
            {
                return null;
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
                lock (owner.Entries)
                    owner.Entries.Add((logLevel, formatter(state, exception), exception));
            }
        }
    }
}
