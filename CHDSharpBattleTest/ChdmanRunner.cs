using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;

namespace CHDSharpBattleTest;

/// <summary>
///     Thin wrapper around chdman.exe (MAME): runs commands, captures output, and parses
///     <c>chdman info</c> output into strongly typed fields for cross-checking against
///     CHDSharpLib's <see cref="CHDSharp.Chd.ReadHeader(string, out CHDSharp.Models.ChdHeaderInfo?)" />.
/// </summary>
internal sealed class ChdmanRunner
{
    internal ChdmanRunner(string exePath, int timeoutMs = 300_000)
    {
        ExePath = exePath;
        TimeoutMs = timeoutMs;
    }

    internal string ExePath { get; }
    internal int TimeoutMs { get; }

    /// <summary>Runs <c>chdman &lt;command&gt; [args...]</c> and captures stdout/stderr.</summary>
    internal RunResult Run(string command, params string[] args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = ExePath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        psi.ArgumentList.Add(command);
        foreach (var a in args)
            psi.ArgumentList.Add(a);

        using var p =
            Process.Start(psi) ?? throw new InvalidOperationException($"Failed to start {ExePath}");
        var tOut = p.StandardOutput.ReadToEndAsync();
        var tErr = p.StandardError.ReadToEndAsync();
        var sw = Stopwatch.StartNew();
        if (!p.WaitForExit(TimeoutMs))
        {
            try
            {
                p.Kill(true);
            }
            catch
            {
                // ignore
            }

            throw new TimeoutException($"chdman {command} timed out after {TimeoutMs}ms");
        }

        sw.Stop();
        return new RunResult(p.ExitCode, tOut.Result, tErr.Result, sw.Elapsed.TotalSeconds);
    }

    /// <summary>The version banner line, e.g. "chdman - MAME Compressed Hunks of Data (CHD) manager 0.289 (mame0289)".</summary>
    internal string VersionBanner()
    {
        var r = Run("help", "createraw");
        var first = r
            .Combined.Split(
                '\n',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
            )
            .FirstOrDefault();
        return string.IsNullOrEmpty(first) ? "(unknown chdman version)" : first;
    }

    /// <summary>Runs <c>chdman info</c> and parses the output; returns null when the file cannot be read.</summary>
    internal ChdmanInfo? Info(string chdPath)
    {
        var r = Run("info", "-i", chdPath);
        if (r.ExitCode != 0)
            return null;

        return ParseInfo(r.Combined);
    }

    /// <summary>Parses the text output of <c>chdman info</c>.</summary>
    internal static ChdmanInfo? ParseInfo(string output)
    {
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rawLine in output.Split('\n'))
        {
            var line = rawLine.Trim();
            var m = Regex.Match(line, @"^([A-Za-z][A-Za-z ]*?):\s+(.+?)\s*$", RegexOptions.ExplicitCapture,
                TimeSpan.FromSeconds(1));
            if (!m.Success)
                continue;

            fields[m.Groups[1].Value.Trim()] = m.Groups[2].Value.Trim();
        }

        if (
            !fields.TryGetValue("File Version", out var versionText)
            || !int.TryParse(versionText, CultureInfo.InvariantCulture, out var version))
            return null;

        return new ChdmanInfo(
            version,
            fields.TryGetValue("Logical size", out var ls) ? ParseNum(ls) : 0,
            fields.TryGetValue("Hunk Size", out var hs) ? (uint)ParseNum(hs) : 0,
            fields.TryGetValue("Total Hunks", out var th) ? (uint)ParseNum(th) : 0,
            fields.TryGetValue("Unit Size", out var us) ? (uint)ParseNum(us) : 0,
            fields.TryGetValue("Total Units", out var tu) ? (uint)ParseNum(tu) : 0,
            fields.GetValueOrDefault("Compression", "none"),
            fields.TryGetValue("CHD size", out var cs) ? (long)ParseNum(cs) : 0,
            NormalizeHash(fields, "SHA1"),
            NormalizeHash(fields, "Data SHA1"),
            NormalizeHash(fields, "MD5"),
            NormalizeHash(fields, "Parent SHA1"),
            NormalizeHash(fields, "Parent MD5")
        );

        static ulong ParseNum(string text)
        {
            text = text.Replace(",", "", StringComparison.Ordinal).Split(' ')[0];
            return ulong.Parse(text, CultureInfo.InvariantCulture);
        }
    }

    private static string? NormalizeHash(Dictionary<string, string> fields, string key)
    {
        if (!fields.TryGetValue(key, out var v))
            return null;

        v = v.Trim();
        return string.IsNullOrEmpty(v) || v.Equals("(none)", StringComparison.OrdinalIgnoreCase)
            ? null
            : v.ToLowerInvariant();
    }
}