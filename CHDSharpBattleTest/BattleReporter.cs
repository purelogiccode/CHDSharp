using System.Globalization;
using System.Text;

namespace CHDSharpBattleTest;

/// <summary>One timed battle outcome for one corpus file (both tools plus a cross parity row each battle).</summary>
internal sealed record BattleRow(
    string File,
    string Kind,
    uint Version,
    long ChdBytes,
    ulong LogicalBytes,
    string Battle,
    string Tool,
    bool Ok,
    double Seconds,
    long OutBytes,
    string? Hash,
    int ExitCode,
    double? MibPerSecond,
    double? Ratio,
    string? Error);

/// <summary>Writes the corpus battle results.csv incrementally and the battles.md summary at the end of the run.</summary>
internal static class BattleReporter
{
    private static readonly string[] CsvHeader =
    [
        "file", "kind", "version", "chd_mib", "logical_mib",
        "battle", "tool", "ok", "seconds", "mib_per_sec", "out_bytes", "ratio", "hash", "exit", "error"
    ];

    public static void AppendCsv(string csvPath, IReadOnlyList<BattleRow> rows)
    {
        var sb = new StringBuilder();
        if (!File.Exists(csvPath))
            sb.AppendLine(string.Join(',', CsvHeader));

        foreach (var r in rows)
            sb.Append(Csv(r.File)).Append(',')
                .Append(r.Kind).Append(',')
                .Append(r.Version).Append(',')
                .Append(Mib(r.ChdBytes)).Append(',')
                .Append(MibU(r.LogicalBytes)).Append(',')
                .Append(Csv(r.Battle)).Append(',')
                .Append(Csv(r.Tool)).Append(',')
                .Append(r.Ok ? "1" : "0").Append(',')
                .Append(r.Seconds.ToString("F3", CultureInfo.InvariantCulture)).Append(',')
                .Append(r.MibPerSecond?.ToString("F2", CultureInfo.InvariantCulture) ?? "").Append(',')
                .Append(r.OutBytes).Append(',')
                .Append(r.Ratio?.ToString("F4", CultureInfo.InvariantCulture) ?? "").Append(',')
                .Append(r.Hash ?? "").Append(',')
                .Append(r.ExitCode).Append(',')
                .AppendLine(Csv(r.Error ?? ""));

        File.AppendAllText(csvPath, sb.ToString());
    }

    public static HashSet<string> LoadCompletedKeys(string csvPath)
    {
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(csvPath)) return keys;
        foreach (var line in File.ReadLines(csvPath).Skip(1))
        {
            var i = line.IndexOf(',');
            if (i <= 0) continue;
            keys.Add(line[..i]);
        }

        return keys;
    }

    public static void WriteMarkdown(
        string mdPath,
        IReadOnlyList<BattleRow> rows,
        CorpusOptions cfg,
        string chdmanPath,
        string? cliPath)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# chdman vs CHDSharp — Corpus Battle Results");
        sb.AppendLine();
        sb.AppendLine(CultureInfo.InvariantCulture, $"- Date: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"- Files run: {rows.Select(r => r.File).Distinct(StringComparer.OrdinalIgnoreCase).Count()}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"- Workers (-np): {cfg.Workers} | Codecs: raw/copy/dvd/hd=`{cfg.CodecRaw}`, cd/gd=`{cfg.CodecCd}`");
        sb.AppendLine($"- chdman: `{chdmanPath}` | CHDSharp: `{cliPath ?? "(not found)"}`");
        sb.AppendLine();

        foreach (var g in rows
                     .Where(r => !string.Equals(r.Tool, "cross", StringComparison.OrdinalIgnoreCase))
                     .GroupBy(r => r.Battle, StringComparer.OrdinalIgnoreCase)
                     .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
            AppendBattleSummary(sb, g.Key, g.ToList());

        sb.AppendLine("## Parity (cross-tool agreement)");
        sb.AppendLine();
        sb.AppendLine("| battle | result | files |");
        sb.AppendLine("|---|---|---|");
        foreach (var g in rows
                     .Where(r => string.Equals(r.Tool, "cross", StringComparison.OrdinalIgnoreCase))
                     .GroupBy(r => r.Battle, StringComparer.OrdinalIgnoreCase)
                     .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
        {
            var ok = g.Count(r => r.Ok);
            sb.AppendLine(CultureInfo.InvariantCulture,
                $"| {g.Key} | {ok}/{g.Count()} {(ok == g.Count() ? "MATCH" : "MISMATCH/FAIL")} | {g.Count()} |");
        }

        sb.AppendLine();

        sb.AppendLine("## Per-file results");
        sb.AppendLine();
        foreach (var gr in rows.GroupBy(r => r.File, StringComparer.OrdinalIgnoreCase)
                     .OrderByDescending(g => g.First().LogicalBytes))
        {
            var first = gr.First();
            sb.AppendLine(CultureInfo.InvariantCulture,
                $"### {first.File}  ({MibU(first.LogicalBytes)} MiB logical, V{first.Version}, {first.Kind})");
            sb.AppendLine();
            sb.AppendLine("| battle | tool | ok | seconds | MiB/s | out bytes | ratio | hash12 | error |");
            sb.AppendLine("|---|---|---|---|---|---|---|---|---|");
            foreach (var r in gr)
                sb.AppendLine(CultureInfo.InvariantCulture,
                    $"| {r.Battle} | {r.Tool} | {(r.Ok ? "OK" : "FAIL")} | " +
                    $"{(r.Seconds > 0 ? r.Seconds.ToString("F2", CultureInfo.InvariantCulture) : "")} | " +
                    $"{r.MibPerSecond?.ToString("F1", CultureInfo.InvariantCulture) ?? ""} | " +
                    $"{(r.OutBytes > 0 ? r.OutBytes : "")} | " +
                    $"{r.Ratio?.ToString("F4", CultureInfo.InvariantCulture) ?? ""} | {ShortHash(r.Hash)} | " +
                    $"{(r.Error ?? "").Replace('|', '/')} |");

            sb.AppendLine();
        }

        sb.AppendLine("> Note: the CHDSharp CLI deep-verifies every encode with its own library before exiting; " +
                      "chdman does not verify. Encode timings therefore include that extra pass for CHDSharp.");
        File.WriteAllText(mdPath, sb.ToString());
    }

    private static void AppendBattleSummary(StringBuilder sb, string battle, List<BattleRow> steps)
    {
        var byTool = steps
            .GroupBy(r => r.Tool, StringComparer.OrdinalIgnoreCase)
            .Where(g => !string.Equals(g.Key, "cross", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (byTool.Count == 0) return;

        sb.AppendLine(CultureInfo.InvariantCulture, $"## Battle: {battle}");
        sb.AppendLine();
        sb.AppendLine("| tool | runs | ok | total sec | weighted MiB/s | wins (faster) |");
        sb.AppendLine("|---|---|---|---|---|---|");

        var stats = byTool.ToDictionary(
            g => g.Key,
            g =>
            {
                var secs = g.Sum(r => r.Seconds);
                var bytes = g.Sum(r => (double)r.OutBytes);
                var wMibs = secs > 0 ? bytes / 1048576.0 / secs : 0;
                return (Runs: g.Count(), Ok: g.Count(r => r.Ok), Secs: secs, Mibs: wMibs);
            }, StringComparer.OrdinalIgnoreCase);

        string? winner = null;
        var valid = stats.Where(kv => kv.Value.Ok > 0 && kv.Value.Mibs > 0).ToList();
        if (valid.Count == 2)
            winner = valid[0].Value.Mibs > valid[1].Value.Mibs ? valid[0].Key : valid[1].Key;

        foreach (var (tool, st) in stats.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase))
        {
            var wins = string.Equals(winner, tool, StringComparison.OrdinalIgnoreCase) && valid.Count == 2 ? st.Ok : 0;
            sb.AppendLine(CultureInfo.InvariantCulture, $"| {tool} | {st.Runs} | {st.Ok} | {st.Secs:F1} | {st.Mibs:F1} | {wins} |");
        }

        sb.AppendLine();
    }

    private static string ShortHash(string? hash)
    {
        return string.IsNullOrEmpty(hash) ? "-" : hash[..Math.Min(12, hash.Length)];
    }

    private static string Mib(long bytes)
    {
        return (bytes / 1048576.0).ToString("F2", CultureInfo.InvariantCulture);
    }

    private static string MibU(ulong bytes)
    {
        return (bytes / 1048576.0).ToString("F2", CultureInfo.InvariantCulture);
    }

    private static string Csv(string field)
    {
        if (field.Contains('"') || field.Contains(',') || field.Contains('\n'))
            return "\"" + field.Replace("\"", "\"\"") + "\"";
        return field;
    }
}
