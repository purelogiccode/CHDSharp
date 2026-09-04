---
layout: default
---

# Logging

CHDSharp is **silent by default**. It integrates with `Microsoft.Extensions.Logging` so you can route internal diagnostics to any compatible provider.

---

## Enabling logging

Set the static `Chd.LoggerFactory` **before** performing library operations:

```csharp
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Extensions.Logging;

var serilogLogger = new LoggerConfiguration()
    .MinimumLevel.Debug()
    .WriteTo.Console()
    .CreateLogger();

Chd.LoggerFactory = new SerilogLoggerFactory(serilogLogger);

// All subsequent Chd/ChdFile operations log through Serilog
var result = Chd.CheckFile(File.OpenRead("game.chd"), "game.chd", deepCheck: true);
```

Any `ILoggerFactory`-compatible provider works:

- [Serilog](https://serilog.net/) (`Serilog.Extensions.Logging`)
- [NLog](https://nlog-project.org/) (`NLog.Extensions.Logging`)
- `Microsoft.Extensions.Logging.Console`
- your own `ILoggerFactory` implementation

---

## What gets logged

| Area | Level | Examples |
|------|-------|----------|
| Verification | Information / Debug | progress percentages, array-pool statistics, compression-type statistics per CHD |
| Metadata | Debug | tag + length + ASCII payload of every metadata entry |
| Errors | Warning / Error | failed metadata reads, precache failures, decompression exceptions (with the inner exception and hunk number) |
| Per-codec | Debug | block summaries, repeated-block counts |

Because every log call is a pre-compiled `LoggerMessage.Define` delegate, the overhead is negligible when logging is disabled.

---

## Reset

To disable logging again (e.g. in tests):

```csharp
Chd.LoggerFactory = null;
```

---

## Example: log to a file with Serilog

```csharp
Chd.LoggerFactory = new SerilogLoggerFactory(
    new LoggerConfiguration()
        .MinimumLevel.Information()
        .WriteTo.File("chdsharp.log", rollingInterval: RollingInterval.Day)
        .CreateLogger());
```

---

## Applications: CLI and Tester

The library stays silent unless you opt in, but the two front ends (`CHDSharpCli`, `CHDSharpTester`) always configure Serilog at startup and route **all** logging through it.

### Pipeline

Both apps build the pipeline the same way:

- `MinimumLevel.Debug()` — everything is captured; what the user sees vs. what is stored depends on the sink.
- `Chd.LoggerFactory = new SerilogLoggerFactory(Log.Logger)` — library diagnostics flow into the same pipeline.
- Logger is flushed on exit (`Log.CloseAndFlush()` in the CLI `finally` block, `App.OnExit` in the Tester).

| App | Sinks |
|-----|-------|
| `CHDSharpCli` (`Program.Main`) | Console (`{Message}{NewLine}{Exception}`) + File (rolling day) + `BugReportSink` |
| `CHDSharpTester` (`App.OnStartup`) | Debug + File (rolling day) + `BugReportSink` |

### Log files

Rolling-day files under `%LocalAppData%`:

- CLI: `%LocalAppData%\CHDSharp\logs\chdsharp-<date>.log`
- Tester: `%LocalAppData%\CHDSharpTester\logs\chdsharp-tester-<date>.log`

File template: `{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}`, `InvariantCulture`.

### Conventions

- No direct `Console`/`Debug` logging for diagnostics. The only `Console` writes left in the CLI are `chdman`-parity report output (e.g. `info`) and the double-click "Press any key to exit" pause; the only `Debug.WriteLine` calls left in the Tester are fallbacks for failures thrown before the logger exists (`OnStartup`) or while it is being flushed (`OnExit`). The Tester's on-screen log (`MainViewModel.AddLog`) also forwards each entry to `Log.Information`, so the file log mirrors the UI.
- Top-level and public entry points are wrapped in try/catch + `Log`: CLI `Main` (`Log.Fatal`, exit code 3), Tester `OnStartup`/`OnExit`, `RunAsync`, `ExportPdfAsync`, `RelayCommand.Execute`, and the public `ChdmanWrapper` methods (`GetInfo`, `Verify`, `ExtractRaw`, `Copy`, `CopyVerbose` return safe fallbacks on failure).
- Existing `catch` blocks log with the exception attached where one is available (`Log.Warning(ex, …)` / `Log.Error(ex, …)`). Expected-failure probes log at `Debug` so they stay out of bug reports: codec-default detection, source-header reads, file-size stats, `GetHunkCodecName` probes, parent IDENT/GDDD reads, temp-file cleanup, and cancellation (`OperationCanceledException` is `Information`/`Debug`, never an error).

### Automatic bug reports (`BugReportSink`)

Both apps attach a `BugReportSink` (`CHDSharpCli/BugReportSink.cs`, `CHDSharpTester/Services/BugReportSink.cs`) that POSTs every log event at `Warning` or above to `https://www.purelogiccode.com/bugreport/api/send-bug-report` (`X-API-KEY` header, JSON body).

- Fire-and-forget (`Task.Run`, 10 s `HttpClient` timeout): delivery never blocks or throws into the logging pipeline.
- `OperationCanceledException` / `TaskCanceledException` events are skipped (user cancellation is not a bug).
- Server throttling (`HTTP 429`) and any transport failure are silently tolerated.

Each report embeds the same sections:

- `=== Environment Details ===` — Date, Application Name, Application Version, OS Version, Architecture, Bitness, Windows Version, Processor Count, Base Directory, Temp Path (plus Runtime/Session/Elevated context in the CLI sink).
- `=== Error Details ===` — the rendered log message.
- `=== Exception Details ===` — Type, Message, Source, StackTrace (plus inner exceptions; `(none)` placeholders when the event carries no exception, e.g. a usage warning).
- The JSON payload also carries `applicationName`, `version`, `environment`, and `stackTrace` per the Bug Report API.

---

## Notes

- `LoggerFactory` is read lazily per operation, so you can swap providers at runtime; for predictable behavior, set it once at startup.
- The logging package (`Microsoft.Extensions.Logging.Abstractions`) is the library's only non-Zstd dependency and is marked optional in the sense that the library functions perfectly with it never set.
