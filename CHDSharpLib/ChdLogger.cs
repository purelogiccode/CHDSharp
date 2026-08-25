using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace CHDSharp;

/// <summary>
///     Provides lazy-resolving loggers for CHDSharp that defer to an externally-supplied
///     <see cref="ILoggerFactory" />.
/// </summary>
internal static class ChdLogger
{
    private static volatile ILoggerFactory? _factory;

    /// <summary>
    ///     Gets or sets the <see cref="ILoggerFactory" /> used to create loggers. Can be set at any time; loggers resolve
    ///     the factory lazily.
    /// </summary>
    internal static ILoggerFactory? Factory
    {
        get => _factory;
        set => _factory = value;
    }

    /// <summary>
    ///     Returns a logger that resolves the real logger from <see cref="Factory" />
    ///     on every use. This makes it safe to capture the returned instance in
    ///     <c>static readonly</c> fields before the factory has been assigned.
    /// </summary>
    internal static ILogger GetLogger(string category)
    {
        return new LazyLogger(category);
    }

    /// <summary>Returns a lazy-resolving logger for the type <typeparamref name="T" />.</summary>
    /// <typeparam name="T">The type whose full name is used as the logger category.</typeparam>
    internal static ILogger GetLogger<T>()
    {
        return GetLogger(typeof(T).FullName!);
    }

    private sealed class LazyLogger(string category) : ILogger
    {
        private readonly string _category = category;

        private volatile CachedLogger? _cached;

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            return Resolve().BeginScope(state);
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return Resolve().IsEnabled(logLevel);
        }

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        )
        {
            Resolve().Log(logLevel, eventId, state, exception, formatter);
        }

        private ILogger Resolve()
        {
            var factory = _factory;
            if (factory is null)
                return NullLogger.Instance;

            var cached = _cached;
            if (cached is null || !ReferenceEquals(cached.SourceFactory, factory))
            {
                cached = new CachedLogger(factory, factory.CreateLogger(_category));
                _cached = cached;
            }

            return cached.Logger;
        }

        private sealed record CachedLogger(ILoggerFactory SourceFactory, ILogger Logger);
    }
}