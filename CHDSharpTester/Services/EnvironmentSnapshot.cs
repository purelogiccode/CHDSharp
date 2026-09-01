using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace CHDSharpTester.Services;

/// <summary>
///     Captures the static environment details that must accompany every forwarded bug report.
///     Computed once at construction from the running process and operating system.
/// </summary>
internal sealed class EnvironmentSnapshot
{
    /// <summary>Initializes a new snapshot for the given application name.</summary>
    /// <param name="applicationName">The application name to embed in reports.</param>
    public EnvironmentSnapshot(string applicationName)
    {
        ApplicationName = applicationName;
        ApplicationVersion =
            Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "Unknown";
        WindowsVersion = GetWindowsVersion();
    }

    /// <summary>Local timestamp when the snapshot was created.</summary>
    public static string Date => DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

    /// <summary>The friendly application name (e.g. <c>CHDSharpTester</c>).</summary>
    public string ApplicationName { get; }

    /// <summary>The entry-assembly version, or <c>Unknown</c> when it cannot be resolved.</summary>
    public string ApplicationVersion { get; }

    /// <summary>The operating-system version string reported by the runtime.</summary>
    public static string OsVersion => Environment.OSVersion.VersionString;

    /// <summary>The processor architecture of the operating system (e.g. <c>X64</c>).</summary>
    public static string Architecture => RuntimeInformation.OSArchitecture.ToString();

    /// <summary>Whether the OS process is 64-bit or 32-bit.</summary>
    public static string Bitness => Environment.Is64BitOperatingSystem ? "64-bit" : "32-bit";

    /// <summary>A human-readable Windows version description.</summary>
    public string WindowsVersion { get; }

    /// <summary>The number of logical processors available to the process.</summary>
    public static int ProcessorCount => Environment.ProcessorCount;

    /// <summary>The base directory of the application.</summary>
    public static string BaseDirectory => AppContext.BaseDirectory;

    /// <summary>The system temporary directory path.</summary>
    public static string TempPath => Path.GetTempPath();

    [SuppressMessage(
        "ReSharper",
        "CA1031",
        Justification = "Best-effort environment detection; fall back to OS version."
    )]
    private static string GetWindowsVersion()
    {
        try
        {
            var desc = RuntimeInformation.OSDescription.Trim();
            if (!string.IsNullOrEmpty(desc))
                return desc;
        }
        catch
        {
            // ignore and fall back
        }

        return Environment.OSVersion.ToString();
    }
}