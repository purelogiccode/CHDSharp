---
layout: default
---

# Getting Started

This page walks through installing CHDSharp, writing your first program, and using the bundled command-line tool.

---

## 1. Installation

### NuGet

```bash
dotnet add package CHDSharp
```

or via the Package Manager Console:

```powershell
Install-Package CHDSharp
```

The package targets `net8.0`, `net9.0`, and `net10.0` and has **no native dependencies**. The only runtime dependency is [ZstdSharp.Port](https://www.nuget.org/packages/ZstdSharp.Port/) (a pure-C# Zstd decompressor); every other codec is implemented from scratch in managed code.

### From source

```bash
git clone https://github.com/purelogiccode/CHDSharp.git
cd CHDSharp
dotnet build -c Release
```

See [Building](building.md) for the full build, pack, and publish workflow.

---

## 2. First program

The two central types are:

- **`Chd`** — a static class for verification and quick checks.
- **`ChdFile`** — an instance-based random-access reader.

```csharp
using CHDSharp;
using CHDSharp.Models;

// 1. Is this even a CHD?
if (!Chd.IsChdFile("game.chd", out uint version))
{
    Console.WriteLine("Not a CHD file");
    return;
}
Console.WriteLine($"Detected CHD V{version}");

// 2. Deep verification: decompress every hunk, compare SHA1/MD5.
using var stream = File.OpenRead("game.chd");
var result = Chd.CheckFile(stream, "game.chd", deepCheck: true);
Console.WriteLine(result.IsSuccess
    ? $"OK — V{result.Version}, SHA1: {result.Sha1Hex}"
    : $"FAILED — {result.Error.GetMessage()}");

// 3. Random access: open once, read on demand.
var err = ChdFile.Open("game.chd", out var chd);
if (err != ChdError.Chderrnone)
{
    Console.WriteLine($"Open failed: {err.GetMessage()}");
    return;
}

using (chd)
{
    // Metadata (game name, disc label, ...)
    foreach (var meta in chd.Metadata)
        Console.WriteLine(meta);            // e.g. "GAME: gauntlet"

    // A single decompressed hunk
    var hunk = new byte[chd.HunkBytes];
    chd.ReadHunk(0, hunk);

    // An arbitrary byte range (crosses hunk boundaries automatically)
    var buf = new byte[4096];
    chd.Read(byteOffset: 1_000_000, buf, 0, buf.Length);

    // The whole image in one shot
    chd.ReadAllBytes(out var image);
}
```

> **Thread safety:** a `ChdFile` instance is **not** thread-safe — it seeks a shared stream and mutates shared buffers. Serialize all calls on one instance. Multiple instances over separate streams can be used in parallel.

---

## 3. Async usage

Every blocking operation has an async twin:

```csharp
var (err, chd) = await ChdFile.OpenAsync("game.chd");
if (err != ChdError.Chderrnone) return;

await using (chd)
{
    var hunk = new byte[chd.HunkBytes];
    await chd.ReadHunkAsync(0, hunk);

    var buf = new byte[1024];
    await chd.ReadAsync(0x10000, buf, 0, buf.Length);
}
```

Async overloads exist for **all** `Open` variants, including the parent-aware ones:

```csharp
// Standalone
await ChdFile.OpenAsync("game.chd");
// Child with parent path
await ChdFile.OpenAsync("child.chd", "parent.chd");
// Child with an already-open parent instance
await ChdFile.OpenAsync("child.chd", parent);
// From a stream
await ChdFile.OpenAsync(stream, leaveOpen: true);
// From a stream with a parent
await ChdFile.OpenAsync(stream, leaveOpen: true, parent);
```

---

## 4. Working with CD/GD-ROM images

CHDSharp parses the CD track layout (TOC) stored in the metadata and can generate standard descriptor files:

```csharp
var err = ChdFile.Open("game.chd", out var chd);
using (chd)
{
    if (chd.IsCd)
    {
        // Track layout
        foreach (var track in chd.Tracks!)
            Console.WriteLine($"Track {track.TrackNumber}: {track.GetTypeString()} " +
                              $"{track.Frames} frames, pregap {track.PreGap}");

        // CUE sheet for burning/emulation
        var cue = chd.GenerateCueSheet("game.bin");
        File.WriteAllText("game.cue", cue);
    }
    else if (chd.IsGdRom)
    {
        var gdi = chd.GenerateGdiDescriptor(["track01.bin", "track02.bin"]);
        File.WriteAllText("game.gdi", gdi);
    }

    // One-call extraction
    var created = chd.ExtractToDirectory("out", "game");
    Console.WriteLine(string.Join("\n", created));
}
```

See [Extraction](extraction.md) for details.

---

## 5. The CLI tool (`CHDSharp`)

`CHDSharp` is a command-line CHD manager compatible with MAME's `chdman` syntax. It is useful both as a verification tool and as a reference for calling the API.

```bash
# chdman-style subcommands
CHDSharp info -i game.chd
CHDSharp verify -i game.chd
CHDSharp createcd -o game.chd -i game.cue
CHDSharp extractcd -o game.cue -i game.chd
CHDSharp copy -o new.chd -i old.chd -c zstd
CHDSharp listtemplates

# Convenience commands (CHDSharp extensions)
CHDSharp D:\CHD                  # verify all .chd in directory
CHDSharp --list chd_paths.txt    # verify from a list file
CHDSharp --random game.chd       # random-access self-test
CHDSharp --parent child.chd parent.chd
CHDSharp --toc game.chd          # print table of contents
CHDSharp --cue game.chd          # generate CUE sheet
CHDSharp --classify game.chd     # classify media type
```

Run `CHDSharp help` for the full command list, or `CHDSharp help <command>` for detailed help on a specific command.

---

## 6. Next steps

- Read the [API Reference](api-reference.md) for every member.
- Learn how the format works in the [CHD Format Reference](chd-format.md).
- Understand verification semantics in [Verification](verification.md).
- Handle differential CHDs with [Parent/Child CHDs](parent-child-chds.md).
- Create CHDs with the [Encoder](encoder.md).
