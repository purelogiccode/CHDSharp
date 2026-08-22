# CHDSharp Bug & Inconsistency Report

Deep codebase review conducted on 2026-08-22. Findings organized by severity.

---

## Critical Bugs

### C-01: `ecc_compute_bytes_result` computes ECC parity incorrectly
- **File:** `CHDSharpLib/Utils/cdRom.cs:279-282`
- **Description:** Both `val1` and `val2` are XORed with the same `ecc_source_byte` call on each iteration, making them always equal before the final Ecclow/Ecchigh correction. The original C code XORs `val2` separately. This produces incorrect ECC parity bytes for mode-2 sectors.
- **Fix:** The loop should XOR `val1` through `Ecclow` on each iteration but `val2` should accumulate raw XOR without the Ecclow step.

### C-02: CdflCodec uses wrong FLAC block size limit
- **File:** `CHDSharpEncoder/CdflCodec.cs:35`
- **Description:** Block size is halved while `> CdConstants.MaxSectorData` (2352), but MAME's `chd_cd_flac_compressor` uses 2048 as the limit. This produces different FLAC frames than chdman, breaking byte-identical output for cdfl hunks.
- **Fix:** Change to `while (_blockSize > 2048)`.

### C-03: `EncodeLaserDisc` integer overflow in VBI buffer allocation
- **File:** `CHDSharpEncoder/ChdEncoder.cs:302`
- **Description:** `ldFrameData = new byte[frames * VbiParse.PackedBytes]` — `frames` is `ulong`. If `frames` exceeds `int.MaxValue / 16`, the multiplication overflows or the array allocation throws. No bounds check exists.
- **Fix:** Validate `frames` fits in a reasonable range before allocating.

### C-04: `EncodeLaserDisc` integer overflow in fullFrame allocation
- **File:** `CHDSharpEncoder/ChdEncoder.cs:314`
- **Description:** `new byte[(int)(width * height * interlaceFactor * 2)]` — the multiplication is in `uint` arithmetic. For large video dimensions the intermediate product overflows before the cast to `int`.
- **Fix:** Cast to `ulong` before multiplying.

---

## High Bugs

### H-01: `ChdImageStream.Seek` from `SeekOrigin.End` can underflow
- **File:** `CHDSharpLib/ChdImageStream.cs:171`
- **Description:** `_chd.TotalBytes - (ulong)(-offset)` — when offset's absolute value exceeds `TotalBytes`, the subtraction wraps (ulong underflow) producing a huge value instead of throwing.
- **Fix:** Add bounds check: `if ((ulong)(-offset) > _chd.TotalBytes) throw new ArgumentOutOfRangeException(...)`.

### H-02: `ReadHeaderV3` Length field parsed incorrectly (byte order)
- **File:** `CHDSharpLib/CHDHeaders.cs:294`
- **Description:** `Length = (uint)((br.ReadByte() << 8) | (br.ReadByte() << 0) | (br.ReadByte() << 16))` — the three bytes are assembled as `[1][0][2]` instead of big-endian `[0][1][2]`. This corrupts every V3 hunk map entry's length.
- **Fix:** Change to `(uint)((br.ReadByte() << 16) | (br.ReadByte() << 8) | br.ReadByte())`.

### H-03: `ReadHeaderV4` Length field parsed incorrectly
- **File:** `CHDSharpLib/CHDHeaders.cs:350`
- **Description:** `Length = (uint)(br.ReadUInt16Be() | (br.ReadByte() << 16))` — produces `[byte1][byte0][byte2]` ordering instead of correct big-endian `[byte0][byte1][byte2]`.
- **Fix:** Use `(uint)((br.ReadByte() << 16) | (br.ReadByte() << 8) | br.ReadByte())`.

### H-04: `MapCompressor.RleEncode` silent truncation of parent unit index
- **File:** `CHDSharpEncoder/MapCompressor.cs:266`
- **Description:** `maxParent = (uint)Math.Max(maxParent, refUnit)` — `refUnit` is `ulong` but cast to `uint`. If parent unit index exceeds `uint.MaxValue` (parent images > 2TB with 512-byte units), value truncates silently, corrupting the map's `parentBits` calculation.
- **Fix:** Validate `refUnit <= uint.MaxValue` or use `ulong` for `maxParent`.

### H-05: `ReadCdHunk` integer overflow in hunkStartFrame
- **File:** `CHDSharpEncoder/ChdEncoder.cs:1304`
- **Description:** `hunkStartFrame = hunkIndex * framesPerHunk` — both are `int`/`int`. For CD images with > 2M frames, this multiplication overflows `int`, causing incorrect frame lookups.
- **Fix:** Use `long` arithmetic: `var hunkStartFrame = (long)hunkIndex * framesPerHunk;`

### H-06: `VbiParse.ParseManchesterCode` array bounds risk
- **File:** `CHDSharpEncoder/VbiParse.cs:199-204`
- **Description:** `srcAbs[curBit + offBy + 0]` — `curBit` can exceed `MaxSourceWidth - 1` when `clock` is large. The `offBy` addition (up to 3) can push the index past the array boundary.
- **Fix:** Add bounds checking: `if (curBit + offBy + 1 >= sourceWidth) break;`

### H-07: `ExtractLaserDisc` potential out-of-bounds read
- **File:** `CHDSharpEncoder/ChdEncoder.cs:577-580`
- **Description:** When combining interlaced fields, header fields are read from `prevBuf` without verifying the previous hunk has a valid 'chav' header. A corrupt hunk could cause incorrect offsets.
- **Fix:** Validate the previous hunk's 'chav' magic before reading its header fields.

---

## Medium Bugs

### M-01: `compressed_v5_map` — `Crc16` nullable accessed with null-forgiving operator
- **File:** `CHDSharpLib/CHDHeaders.cs:614`
- **Description:** `map[blockIndex].Crc16!.Value` — for SELF/PARENT entries where `Crc16` is null, this causes `NullReferenceException`.
- **Fix:** Default to 0: `map[blockIndex].Crc16 ?? 0`.

### M-02: `DecompressDataParallel` — division by zero risk
- **File:** `CHDSharpLib/CHD.cs:391`
- **Description:** `var blocksToKeep = 1024 * 1024 * 512 / (int)chd.Blocksize` — if `Blocksize` is 0, division by zero occurs.
- **Fix:** Add guard before division.

### M-03: `RawDeflate.Compress` silently ignores initialization error
- **File:** `CHDSharpEncoder/RawDeflate.cs:21`
- **Description:** `_ = zlib.DeflateInit(ref zs, ...)` discards the return value. If initialization fails, subsequent `Deflate` operates on uninitialized stream.
- **Fix:** Check return value and throw on failure.

### M-04: `ChdMetadataEntry.Equals` uses reference equality for Data
- **File:** `CHDSharpLib/Models/ChdMetadataEntry.cs:22`
- **Description:** `ReferenceEquals(Data, other.Data)` means two entries with identical byte content but different array instances are not equal. Surprising for a `record` type.
- **Fix:** Use `SequenceEqual` for byte array comparison.

### M-05: `ReadAheadManager.Clear()` can release more semaphores than acquired
- **File:** `CHDSharpLib/CHDFile.cs:3186`
- **Description:** `_semaphore.Release(LookAhead - _semaphore.CurrentCount)` — if a task completes between reading `CurrentCount` and calling `Release`, the count can exceed `LookAhead`.
- **Fix:** Use a try/finally pattern or `SemaphoreSlim` with max count.

### M-06: `BattleHarness.SummaryText()` accesses `_checks[0]` without empty check
- **File:** `CHDSharpBattleTest/BattleHarness.cs:186`
- **Description:** If `Run()` records no checks (all suites skipped), this throws `ArgumentOutOfRangeException`.
- **Fix:** Guard with `if (_checks.Count == 0) return "No checks recorded.";`

### M-07: `BattleHarness.CdEncodeCase` — `refExtract!` null dereference
- **File:** `CHDSharpBattleTest/BattleHarness.cs:449-460`
- **Description:** If the "extract parity" check fails, `refExtract` remains null. The subsequent "decode" check uses `refExtract!` causing `NullReferenceException`.
- **Fix:** Guard with `if (refExtract != null)`.

### M-08: `ChdmanWrapper.Process.Start(psi)!` — null-forgiving on failed process start
- **File:** `CHDSharpTester/Services/ChdmanWrapper.cs:83,136,173`
- **Description:** If process fails to start, `Process.Start` returns null and `!` suppresses the warning, leading to `NullReferenceException`.
- **Fix:** Use `Process.Start(psi) ?? throw new InvalidOperationException(...)`.

### M-09: `CliAdditionalTests` missing `[Collection("CLI")]` attribute
- **File:** `CHDSharpTest/CliAdditionalTests.cs:5`
- **Description:** `CliIntegrationTests` has `[Collection("CLI")]` to prevent parallel CLI execution, but `CliAdditionalTests` lacks it. Both spawn external processes against the same binary.
- **Fix:** Add `[Collection("CLI")]` to `CliAdditionalTests`.

### M-10: `CliAdditionalTests` hardcoded "Debug" configuration
- **File:** `CHDSharpTest/CliAdditionalTests.cs:20`
- **Description:** Hardcodes `"Debug"` while `CliIntegrationTests` dynamically resolves. Tests fail in Release configuration.
- **Fix:** Use dynamic resolution like `CliIntegrationTests`.

### M-11: `CliAdditionalTests` double-quoting of path arguments
- **File:** `CHDSharpTest/CliAdditionalTests.cs:96,107,118`
- **Description:** `RunCli("--toc", $"\"{path}\"")` passes already-quoted paths, but `RunCli` also quotes arguments containing spaces. Triple-quoting breaks paths with spaces.
- **Fix:** Pass `path` directly without inner quoting.

### M-12: `DeflateInfiniteLoopTests` double Join on same thread
- **File:** `CHDSharpTest/DeflateInfiniteLoopTests.cs:109-111`
- **Description:** `thread.Join(timeoutMs + 1000)` then `thread.Join(timeoutMs)` — second Join returns immediately since thread already joined. Timeout logic is broken.
- **Fix:** Remove the first `Join` call.

### M-13: Tests silently pass when test data missing
- **File:** `CHDSharpTest/ChdApiTests.cs`, `CHDSharpTest/ChdFileTests.cs` (~20 tests)
- **Description:** `if (!File.Exists(path)) return;` causes tests to silently pass when test data is absent. Masks real failures.
- **Fix:** Use `Assert.True(File.Exists(path), "Test data missing")` or proper skip mechanism.

### M-14: `CueParser.Parse` WAV frame count overflow
- **File:** `CHDSharpEncoder/CueParser.cs:100`
- **Description:** `track.Frames = (int)(wavLength / CdConstants.MaxSectorData)` — for WAV files > ~5GB, result exceeds `int.MaxValue`.
- **Fix:** Validate or use `long` for `Frames`.

### M-15: `GdiParser.Parse` frame count overflow
- **File:** `CHDSharpEncoder/GdiParser.cs:99`
- **Description:** `track.Frames = (int)(new FileInfo(fileName).Length / trksize)` — for large data files, overflows `int`.
- **Fix:** Validate or use `long` arithmetic.

### M-16: `IsoParser.Parse` frame count overflow
- **File:** `CHDSharpEncoder/IsoParser.cs:44,51,58`
- **Description:** `track.Frames = (int)(size / 2048)` — ISO files > ~4GB overflow `int`.
- **Fix:** Validate or use `long` arithmetic.

---

## Low Bugs

### L-01: `ChdFile.Dispose` does not clear `_concurrentCodec` ThreadLocal values
- **File:** `CHDSharpLib/CHDFile.cs:2709-2720`
- **Description:** `ThreadLocal<T>` with `trackAllValues: false` won't enumerate values on dispose. Individual thread-owned `ChdCodecState` instances may leak.
- **Fix:** Use `trackAllValues: true` and dispose all values.

### L-02: `HashUtil.IsAllZero` does not handle null input
- **File:** `CHDSharpTester/Services/HashUtil.cs:19`
- **Description:** Throws `NullReferenceException` if called with null, unlike other `IsAllZero` implementations.
- **Fix:** Add null check.

### L-03: `ShowAbout()` — `Application.Current` can be null
- **File:** `CHDSharpTester/ViewModels/MainViewModel.cs:631-635`
- **Description:** In unit tests, `Application.Current` is null, causing `NullReferenceException`.
- **Fix:** Guard with `Application.Current?.MainWindow`.

### L-04: `HunkDebug` integer overflow in array indexing
- **File:** `CHDSharpTestGen/Program.cs:140`
- **Description:** `raw[h * hdr.Blocksize + i]` uses `uint * uint` which could overflow for very large files.
- **Fix:** Cast to `int` explicitly.

---

## Dead Code

### D-01: `FlacFrameEncoder` — entirely unused class (~334 lines)
- **File:** `CHDSharpEncoder/Flac/FlacFrameEncoder.cs`
- **Description:** Never referenced from any other file. `FlacCodec` and `CdflCodec` use `LibFlacEncoder` instead.
- **Action:** DELETE

### D-02: `FlacBitWriter` — only used by dead FlacFrameEncoder
- **File:** `CHDSharpEncoder/Flac/FlacBitWriter.cs`
- **Description:** Only referenced by `FlacFrameEncoder`, which is itself dead code.
- **Action:** DELETE

### D-03: `MapEntry.BuffOut` field unused outside parallel verification
- **File:** `CHDSharpLib/Models/MapEntry.cs:34`
- **Description:** Only used in `DecompressDataParallel`. Never used in normal `ReadHunk`/`Read` path.
- **Action:** Keep (used by parallel verification) — document purpose.

### D-04: `MapEntry.Processed` field unused outside parallel verification
- **File:** `CHDSharpLib/Models/MapEntry.cs:37`
- **Description:** Same as above — only used in hashing thread of `DecompressDataParallel`.
- **Action:** Keep (used by parallel verification) — document purpose.

### D-05: `ChdCodecState.FlacSettings` set but never read after assignment
- **File:** `CHDSharpLib/Models/ChdCodecState.cs:11`
- **Description:** Assigned in `ChdReaders.Flac()` but only used to construct `FlacAudioDecoder` immediately after. Dead state after initialization.
- **Action:** Keep (simplifies codec state management).

### D-06: `Crc` class instance fields unused in static methods
- **File:** `CHDSharpLib/Utils/CRC.cs:9-10`
- **Description:** `_crc` and `_totalBytesRead` are instance fields but `CalculateDigest` and `VerifyDigest` create a new instance each call. `_totalBytesRead` is incremented but never read externally.
- **Action:** Keep (API compatibility).

### D-07: `ChdHeaderInfo.IsAllZero` duplicates `Util.IsAllZeroArray`
- **File:** `CHDSharpLib/Models/ChdHeaderInfo.cs:87-99`
- **Description:** Private duplicate of `Util.IsAllZeroArray`.
- **Action:** Refactor to reuse utility method.

### D-08: `PlatformDetector.DetectSectorSize` dead code block
- **File:** `CHDSharpEncoder/PlatformDetector.cs:170-176`
- **Description:** Block checking for second CD sync pattern has empty body `{}`.
- **Action:** Remove dead block.

### D-09: `MapEntry` unused compression type constants
- **File:** `CHDSharpEncoder/Models/MapEntry.cs:10-19`
- **Description:** `CompressionType0` through `CompressionType3` defined but never used.
- **Action:** Remove unused constants.

### D-10: `CdTocFlags` unused flags
- **File:** `CHDSharpEncoder/Models/CdToc.cs:76,79`
- **Description:** `GdRomLe` and `MultiSession` flags defined but never set or checked.
- **Action:** Remove or document as reserved.

### D-11: `MetadataWriter` unused tag constants
- **File:** `CHDSharpEncoder/MetadataWriter.cs:44-48`
- **Description:** `PcmciaCisMetadataTag` and `KeyMetadataTag` defined but never used.
- **Action:** Remove unused constants.

### D-12: `TestDataGenerator.Mixed` final `Fill` call is a no-op
- **File:** `CHDSharpBattleTest/TestDataGenerator.cs:136`
- **Description:** Previous fills sum to exactly `size`, so `pos == size` and `Math.Min(count, size - pos) == 0`.
- **Action:** Remove dead call.

### D-13: Duplicate `NonSeekableStream` classes
- **File:** `CHDSharpTest/HeaderAndApiTests.cs:89-125` and `CHDSharpTest/ReadHeaderTests.cs:281-317`
- **Description:** Identical inner class defined in both files.
- **Action:** Extract to shared test helper.

### D-14: Duplicate `RunChdman`/`ResolveChdmanPath` helpers across 6+ test files
- **File:** `CHDSharpEncoderTest/` — multiple files
- **Description:** Copy-pasted identically across at least 6 test classes.
- **Action:** Extract to shared `ChdmanHelper` class.

### D-15: Duplicate `CompTypeConv`/`IsValidCodec` tests
- **File:** `CHDSharpTest/SecondCompressedTests.cs:708-775`
- **Description:** Duplicate of tests already in `ChdCommonTests.cs`.
- **Action:** Remove duplicates.

---

## Inconsistencies

### I-01: Mixed naming conventions for private fields
- **Files:** Multiple
- **Description:** Some use `_camelCase`, others use `PascalCase` for internal fields. `MapEntry` and `ChdHeader` use PascalCase for internal fields.

### I-02: Inconsistent error handling — exceptions vs error codes
- **Files:** `CHD.cs`, `CHDFile.cs`
- **Description:** `CheckFile` returns `ChdError` codes, while `ComputeHashes` throws `InvalidDataException`. `ExtractToDirectory` throws, `ExtractToDirectoryWithReporting` returns error codes.

### I-03: `GeneratedRegex` with `RegexOptions.Compiled` is redundant
- **File:** `CHDSharpLib/ChdTocParser.cs:320`
- **Description:** `[GeneratedRegex]` already generates optimized code. `RegexOptions.Compiled` has no effect.

### I-04: `CueParser` declared as abstract class with only static members
- **File:** `CHDSharpEncoder/CueParser.cs:12`
- **Description:** Should be `static class` since it cannot be instantiated or subclassed.

### I-05: Duplicate CD constants in ChdEncoder
- **File:** `CHDSharpEncoder/ChdEncoder.cs:1468-1475`
- **Description:** `CdMaxSectorData`, `CdMaxSubcodeData`, `CdFrameSize` duplicate `CdConstants.*`.

### I-06: `IsAllZero` and `ToHex` duplicated across CLI, Tester, and BattleTest
- **Files:** Multiple
- **Description:** Same utility functions with slightly different signatures in 3 projects.

### I-07: Typo in XML doc comments
- **File:** `CHDSharpTester/Services/ChdmanWrapper.cs:91,113`
- **Description:** "chmman" instead of "chdman".

---

## Code Smells

### S-01: `ChdFile` is a 3198-line god class
- **File:** `CHDSharpLib/CHDFile.cs`
- **Description:** Handles open/close, hunk reading, caching, metadata, tracks, CUE/GDI generation, extraction, memory mapping, and parent resolution.

### S-02: `ChdEncoder` is a 1532-line god class
- **File:** `CHDSharpEncoder/ChdEncoder.cs`
- **Description:** Contains encoding, extraction, CD reading, AVI reading, compression pipeline, and progress reporting.

### S-03: `HuffmanDecoderRle.DecodeOne` hides base method with `new`
- **File:** `CHDSharpLib/Utils/HuffmanDecoderRLE.cs:29`
- **Description:** If referenced via `HuffmanDecoder` variable, base method is called instead, silently skipping RLE handling.

### S-04: `BigEndian.Reverse()` mutates array in-place via extension
- **File:** `CHDSharpLib/Utils/BigEndian.cs:137-141`
- **Description:** Hidden side effect in what looks like a read operation.

### S-05: `ReadAheadManager.DecompressHunk` bare `catch` swallows all exceptions
- **File:** `CHDSharpLib/CHDFile.cs:3177-3180`
- **Description:** Catches all exceptions including `OutOfMemoryException`. Should at minimum catch `Exception` and log.

### S-06: `MainWindow.OnClosing` swallows all exceptions silently
- **File:** `CHDSharpTester/Views/MainWindow.xaml.cs:51-54`
- **Description:** `catch (Exception)` with `// ignore` masks real bugs.

### S-07: `ExportPdf` is `async void` with duplicate error dialogs
- **File:** `CHDSharpTester/ViewModels/MainViewModel.cs:520`
- **Description:** Outer and inner try/catch both show MessageBox on error.

### S-08: `ParallelEncode_IsFasterThanSingleThreaded` may be flaky on CI
- **File:** `CHDSharpEncoderTest/ParallelEncodeTests.cs:153-205`
- **Description:** Asserts `parallelTime * 1.5 < singleTime`. On CI machines with variable load, this can flake.

### S-09: Manual JSON construction in `HashTest`
- **File:** `CHDSharpCli/Program.cs:1675-1692`
- **Description:** Fragile manual JSON that doesn't handle escaping of special characters.

### S-10: `FileSize` property creates new `FileInfo` on every access
- **File:** `CHDSharpTester/Models/ChdFileEntry.cs:38-44`
- **Description:** In WPF data binding, this causes repeated disk I/O during layout passes.

---

## Summary

| Severity | Count |
|----------|-------|
| Critical | 4 |
| High | 7 |
| Medium | 16 |
| Low | 4 |
| **Total Bugs** | **31** |
| Dead Code | 15 |
| Inconsistencies | 7 |
| Code Smells | 10 |

### Top Priority Fixes
1. **C-01** — ECC parity computation incorrect (affects all mode-2 sectors)
2. **C-02** — CdflCodec wrong block size limit (breaks byte-identical cdfl output)
3. **H-02/H-03** — V3/V4 header Length field byte order wrong (corrupts map entries)
4. **H-01** — ChdImageStream.Seek ulong underflow
5. **M-01** — Crc16 null dereference in compressed V5 map
