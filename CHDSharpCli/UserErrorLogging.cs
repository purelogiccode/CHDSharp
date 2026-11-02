using Serilog;

namespace CHDSharp;

/// <summary>
///     Logging helpers for expected user and usage errors.
/// </summary>
/// <remarks>
///     A user error (an unknown option, an invalid numeric argument, an input file that is not usable as the
///     requested format, ...) is a normal outcome of running a command-line tool. It must be visible in the
///     console and in the rolling file log, but it is not an application defect, so it must never be forwarded
///     to the Bug Report API. <see cref="UserError" /> logs such an event at
///     <see cref="Serilog.Events.LogEventLevel.Warning" /> while tagging it with
///     <see cref="BugReportSink.UserErrorProperty" />, which <see cref="BugReportSink" /> filters out.
/// </remarks>
internal static class UserErrorLogging
{
    /// <summary>
    ///     Logs an expected user or usage error. The event keeps warning-level severity for the console and file
    ///     sinks but is tagged so <see cref="BugReportSink" /> does not report it as an application bug.
    /// </summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="messageTemplate">Message template describing the problem.</param>
    /// <param name="args">Values for the message template.</param>
    /// <exception cref="ArgumentNullException"><paramref name="logger" /> is <see langword="null" />.</exception>
    public static void UserError(this ILogger logger, string messageTemplate, params object?[] args)
    {
        ArgumentNullException.ThrowIfNull(logger);
        logger
            .ForContext(BugReportSink.UserErrorProperty, true)
            .Warning(messageTemplate, args);
    }
}
