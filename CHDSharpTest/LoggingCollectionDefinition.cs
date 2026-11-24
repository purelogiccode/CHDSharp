namespace CHDSharp.Tests;

/// <summary>
///     Serializes the tests that temporarily replace <see cref="Chd.LoggerFactory" />. The
///     collection is also opted out of cross-collection parallelism so no other test can call
///     into the library while a capture factory is installed.
/// </summary>
[CollectionDefinition("Logging", DisableParallelization = true)]
public sealed class LoggingCollectionDefinition
{
}

/// <summary>
///     Process-wide gate for tests that mutate <see cref="Chd.LoggerFactory" />.
/// </summary>
internal sealed class LoggingTestGate : IDisposable
{
    private static readonly object Sync = new();

    private bool _disposed;

    private LoggingTestGate()
    {
        Monitor.Enter(Sync);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        Monitor.Exit(Sync);
    }

    public static LoggingTestGate Enter()
    {
        return new LoggingTestGate();
    }
}
