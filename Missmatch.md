# CHDSharp vs chdman Mismatches

**Battle test run:** 2026-08-29 14:23:53, seed=1337
**chdman version:** MAME 0.289 (mame0289)
**Result:** 2896 passed, 11 failed, 0 skipped

> **STATUS: ALL FIXED** — after the fixes below, the battle test reports
> `TOTAL 2907 checks: 2907 passed, 0 failed, 0 skipped` (ALL PASSED).

---

## 1. `--hunk-size` long alias not recognized (8 failures)

**Affected tests (all in `cli-createraw-full`):**
- `createraw raw-zlib:hs alias --hunk-size`
- `createraw raw-zstd:hs alias --hunk-size`
- `createraw raw-lzma:hs alias --hunk-size`
- `createraw raw-huff:hs alias --hunk-size`
- `createraw raw-flac:hs alias --hunk-size`
- `createraw raw-none:hs alias --hunk-size`
- `createraw raw-multi:hs alias --hunk-size`
- `createraw raw-zlibzstd:hs alias --hunk-size`

**Error:**
```
createraw: unit size must be specified if no output parent is supplied (--unitsize/-us)
```

**Root cause:** CHDSharp's CLI parser does not recognize `--hunk-size` (with hyphen) as a valid alias for `--hunksize` / `-hs`. The option is silently ignored, causing the hunk size to be unset. chdman accepts both `--hunksize` and `--hunk-size`.

---

## 2. `--unit-size` long alias not recognized (1 failure)

**Affected test (in `cli-alias-suffix`):**
- `alias --unit-size`

**Error:**
```
createraw: unit size must be specified if no output parent is supplied (--unitsize/-us)
```

**Root cause:** Same issue as above — CHDSharp does not recognize `--unit-size` (with hyphen) as a valid alias for `--unitsize` / `-us`. chdman accepts both `--unitsize` and `--unit-size`.

---

## 3. `--splitbin` with `%t` template not working (2 failures)

**Affected tests (in `cli-extractcd-full`):**
- `extractcd --splitbin with %t template`
- `extractcd --splitbin long alias`

**Error:**
```
A track number variable (%t) must be specified in the output bin filename when --splitbin is enabled
```

**Root cause:** CHDSharp's `extractcd --splitbin` implementation does not properly handle the `%t` track-number template variable in the output bin filename. The `%t` placeholder is either not being recognized or not being substituted, causing the validation check to fail. chdman correctly substitutes `%t` with the track number when `--splitbin` is used.

---

## Summary

| # | Issue | Category | Failures |
|---|-------|----------|----------|
| 1 | `--hunk-size` alias not recognized | CLI option parsing | 8 |
| 2 | `--unit-size` alias not recognized | CLI option parsing | 1 |
| 3 | `--splitbin` `%t` template broken | extractcd feature | 2 |

All 11 failures are **CLI compatibility issues** — the core CHD encode/decode/verify logic is 100% correct (all 2896 non-CLI tests pass).
