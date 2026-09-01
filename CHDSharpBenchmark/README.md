# CHDSharpBenchmark

Benchmark suite for the CHDSharp library. Measures verification, random/sequential reads,
per-codec decode throughput, and per-codec encode throughput — plus a cross-tool harness that
runs stock MAME `chdman.exe` side-by-side with the library on identical inputs.

## Benchmark groups

| Group | Class | What it measures |
|---|---|---|
| Verify | `VerifyBenchmarks` | Full-image deep verification (every hunk decompressed + SHA-1) over the whole corpus: cold file open, warm precached streams, and child-with-parent chains. |
| Read | `ReadBenchmarks` | Byte-granular `ChdFile.Read` throughput (sequential whole-image + uniform random 4 KiB) over the largest corpus CHD, with 1/8/128-hunk LRU cache sizes. |
| Decode | `DecodeBenchmarks` | Per-codec decode throughput. One case per codec: `zlib`, `zstd`, `lzma`, `huff`, `flac`, `cdzl`, `cdlz`, `cdzs`, `cdfl`, `avhu`, and the uncompressed `none` map. Each case opens the corpus CHD declaring that codec and reads every hunk (sequential hunk decode + byte-granular stream read). |
| Encode | `EncodeBenchmarks` | Per-codec encode throughput: the 6 HD codecs (`zlib`, `zstd`, `lzma`, `huff`, `flac`, `none`) at 4 KiB hunks, the 4 CD codecs (`cdzl`, `cdlz`, `cdzs`, `cdfl`) at CD-sized hunks, the 4-slot fallback chain chdman defaults to (`lzma,zlib,huff,flac`), and the laserdisc `avhu` path via `EncodeLaserDisc` on a synthetic AVI. Each codec runs with 1 and 8 workers. |

## Corpus

The suite reads its CHD corpus from `CHDSharpTest/TestData` (resolved by walking up from the
working directory to the repo root). Those files are generated deterministically by the
`CHDSharpTestDataGeneration` project using period-correct chdman binaries; the `manifest.json`
next to them marks intentionally-invalid files (skipped) and child→parent links.

Override the corpus with `--corpus <dir>` or the `CHDSHARP_BENCH_CORPUS` environment variable.
Decode benchmarks prefer the standard single-codec V5 file names (`v5_zlib.chd`,
`v5_cd_cdzl.chd`, `v5_ld_avhu.chd`, …) and fall back to scanning corpus headers for the codec.

## Running

```powershell
# all benchmark groups (BenchmarkDotNet, ShortRun config)
dotnet run --project CHDSharpBenchmark -c Release --framework net10.0

# only the encode group, only zstd cases
dotnet run --project CHDSharpBenchmark -c Release --framework net10.0 -- --filter *Encode*  --filter *zstd*

# publishing-grade numbers (more iterations)
dotnet run --project CHDSharpBenchmark -c Release --framework net10.0 -- --job LongRun
```

BenchmarkDotNet exports results to `BenchmarkDotNet.Artifacts/results/` (GitHub + HTML + CSV)
and prints a summary table. `BenchConfig` uses `Job.ShortRun` (3 warmup + 3 measurement
iterations) and `MemoryDiagnoser`; override with `--job LongRun` for stable numbers.

Interpreting the tables: benchmarks that return a byte count process exactly that many bytes
per operation, so throughput = `value / Mean` (MB/s). `Allocated` is the managed allocation
per operation (the proxy for encode peak memory).

## chdman comparison harness

Compares the library against MAME's `chdman.exe` (wall-clock, median of N runs):

```powershell
dotnet run --project CHDSharpBenchmark -c Release --framework net10.0 -- --chdman chdman.exe
```

The harness runs four passes:

1. **VERIFY** — `chdman verify` vs. `Chd.CheckFileWithParent` on every corpus CHD
   (child files pass their parent via `-ip`).
2. **ENCODE HD** — `chdman createhd -c <codec>` vs. `ChdEncoder.EncodeRaw` on a synthetic
   64 MiB image (`--size-mb` to change).
3. **ENCODE CD** — `chdman createcd -c <codec>` vs. `ChdEncoder.EncodeCd` on a synthetic
   cue/bin disc (6000 mode1/2048 data sectors + 4000 audio sectors).
4. **ENCODE LD** — `chdman createld` vs. `ChdEncoder.EncodeLaserDisc` on a synthetic
   320x240@30 YUY2+PCM AVI (48 frames, one hunk per frame).

Options:

| Flag | Default | Meaning |
|---|---|---|
| `--chdman <path>` | — | Path to `chdman.exe`; enables the harness. |
| `--corpus <dir>` | repo `CHDSharpTest/TestData` | Corpus directory for the verify pass. |
| `--codecs <list>` | all | Comma-separated codecs for the encode passes (`zlib,zstd,lzma,huff,flac,none,cdzl,cdlz,cdzs,cdfl,avhu`); each routes to its device pass. |
| `--size-mb <n>` | 64 | Synthetic HD image size for the HD encode pass. |
| `--runs <n>` | 3 | Runs per measurement; the median is reported. |

Lines that fail on one side (e.g. chdman 0.289 cannot verify V1/V2 files) are collected under
`Warnings:` so a missing competitor never hides a library number.
