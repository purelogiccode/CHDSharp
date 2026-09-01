using System.Security.Cryptography;
using System.Text;
using CHDSharp;

namespace CHDSharpBattleTest;

/// <summary>
///     SHA-256 helpers for corpus battle parity (single files and whole extract
///     directories, where extractcd produces CUE + multiple BINs) plus an in-process
///     library decode used for the optional lib-decode timing row.
/// </summary>
internal static class Hashing
{
    public static (string Hash, long Bytes) Sha256File(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            1024 * 1024, FileOptions.SequentialScan);
        using var sha = SHA256.Create();
        return (Convert.ToHexString(sha.ComputeHash(fs)), fs.Length);
    }

    public static (string Hash, long Bytes) Sha256Directory(string dir)
    {
        var files = Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)
            .OrderBy(f => Path.GetRelativePath(dir, f), StringComparer.Ordinal)
            .ToList();
        using var sha = SHA256.Create();
        long total = 0;
        foreach (var f in files)
        {
            var rel = Path.GetRelativePath(dir, f).Replace('\\', '/');
            var nameBytes = Encoding.UTF8.GetBytes(rel.ToLowerInvariant() + ":" + new FileInfo(f).Length + ":");
            sha.TransformBlock(nameBytes, 0, nameBytes.Length, null, 0);
            using var fs = new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.Read,
                1024 * 1024, FileOptions.SequentialScan);
            var buf = new byte[1024 * 1024];
            int read;
            while ((read = fs.Read(buf, 0, buf.Length)) > 0)
            {
                sha.TransformBlock(buf, 0, read, null, 0);
                total += read;
            }
        }

        sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        return (Convert.ToHexString(sha.Hash ?? []), total);
    }

    /// <summary>Decodes every hunk of a CHD through CHDSharpLib into <paramref name="outPath" />; returns bytes written.</summary>
    public static long LibDecodeTo(string chdPath, string outPath)
    {
        var err = ChdFile.Open(chdPath, out var chd);
        if (err != ChdError.Chderrnone)
            throw new InvalidOperationException($"ChdFile.Open failed: {err}");

        try
        {
            if (chd == null)
                throw new InvalidOperationException("ChdFile.Open returned a null instance");

            long total = 0;
            var hunkBytes = chd.HunkBytes;
            var hunkCount = chd.HunkCount;
            var buf = new byte[hunkBytes];
            using var fs = new FileStream(outPath, FileMode.Create, FileAccess.Write, FileShare.None,
                1024 * 1024, FileOptions.SequentialScan);
            for (uint i = 0; i < hunkCount; i++)
            {
                var e2 = chd.ReadHunk(i, buf);
                if (e2 != ChdError.Chderrnone)
                    throw new InvalidOperationException($"ReadHunk({i}) failed: {e2}");
                fs.Write(buf, 0, (int)Math.Min(hunkBytes, (uint)buf.Length));
                total += hunkBytes;
            }

            return total;
        }
        finally
        {
            chd?.Dispose();
        }
    }
}