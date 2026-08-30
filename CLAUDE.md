# RtlSdrManager

RTL-SDR Manager Library for .NET — a managed wrapper around the native `librtlsdr` library
for controlling RTL2832U-based software-defined radio devices.
License: GPLv3, Copyright 2018-2026 Nandor Toth.

Published on NuGet as [`RtlSdrManager`](https://www.nuget.org/packages/RtlSdrManager/).

## Build and Test

```bash
dotnet build                                        # Build entire solution
dotnet test                                         # Run all tests (hardware-independent)
dotnet run --project samples/RtlSdrManager.Samples  # Run the demo application
dotnet run --project tools/HwVerify                 # Verify device behavior (dongle required)
dotnet pack --configuration Release                 # Create NuGet packages
```

### Scripts

```bash
./build.sh       # Release build: NuGet packages + library and sample binaries
./runsample.sh   # Interactive helper to build and launch the samples
./publish.sh     # Push the packed .nupkg/.snupkg to NuGet.org (--help for options)
```

Build output goes to `artifacts/packages/` (NuGet packages) and
`artifacts/binaries/{RtlSdrManager,Samples}/`. The `artifacts/` directory is gitignored.

The test suite requires **no RTL-SDR hardware and no `librtlsdr` installation** — it covers
only hardware-independent components.

Anything touching a device is verified instead by `tools/HwVerify`, a console harness that
opens device 0 and asserts the device-dependent behavior, printing `PASS`/`FAIL`/`SKIP` and
exiting nonzero on any failure. **Run it with a dongle attached before cutting a release**,
and extend it whenever a fix depends on hardware. Checks the attached hardware cannot prove
report `SKIP` with the reason, so an incomplete run is never mistaken for a clean one.

The harness restores what it changes: the tuner gain mode is captured on entry and put back
on exit, and the bias tee is only written with `Disabled`, on pin 0 only, unless
`--biastee-on` is passed. **Any new check must leave the device as it found it.** Note that
`SetBiasTeeGPIO` also switches the pin to output mode, and nothing clears that on close, so
touching a pin the bias tee does not already use cannot be undone short of a replug.

## Tech Stack

- **.NET 10** (`net10.0` only), C# with nullable reference types and `AllowUnsafeBlocks`
- **`LibraryImport`** — source-generated P/Invoke to `librtlsdr` (no `DllImport`)
- **`SafeHandle`** — device handle lifetime (`SafeRtlSdrHandle`)
- **`System.Threading.Channels`** + **`ArrayPool<byte>`** — raw buffer (zero-copy) mode
- **`Microsoft.SourceLink.GitHub`** — source-linked debugging in the published package
- **xUnit** — testing

The only runtime dependency is the native `librtlsdr` (2.x; **2.0.3 or later recommended**),
which must be installed on the system by the user. `LibResolver` handles cross-platform
discovery, including an `RTLSDR_LIBRARY_PATH` environment-variable override.

## Project Structure

```
src/RtlSdrManager/
  RtlSdrDeviceManager.cs          Singleton device manager, console-suppression scopes
  RtlSdrManagedDevice.cs          Device properties, configuration, IDisposable
  RtlSdrManagedDevice.Async.cs    Asynchronous sample reading (both delivery modes)
  RtlSdrManagedDevice.Sync.cs     Synchronous sample reading
  Frequency.cs, IQData.cs, ...    Value types
  Interop/                        P/Invoke, native resolver, console suppressor
  Modes/                          Configuration enums
  Hardware/                       Tuner type definitions
  Exceptions/                     Custom exception types
tests/RtlSdrManager.Tests/        xUnit suite (hardware-independent only)
tools/HwVerify/                   Hardware verification harness (dongle required)
samples/RtlSdrManager.Samples/    Demo1-Demo5 example applications
docs/                             Per-feature usage guides
design/icon/                      Package icon sources
```

`RtlSdrManagedDevice` is a `partial` class split across three files by concern. Internals are
exposed to the test project via `InternalsVisibleTo` in the csproj.

## Native Interop Rules

These are correctness invariants, not style preferences. Several past bugs came from
violating them:

1. **No managed exception may cross the native callback boundary.** An exception escaping
   `SamplesAvailableCallback` terminates the process. Errors are captured via
   `FailAsyncRead` / `RecordAsyncError` instead.
2. **`SamplesAvailable` is raised on the native USB callback thread.** Slow handlers stall
   the transfer pipeline and cause sample drops.
3. **Never free the device handle or the callback `GCHandle` while the worker may still be
   running.** `Dispose` deliberately leaks both if the bounded join in
   `StopReadSamplesAsync` times out — freeing them under a live callback would crash.
4. **A nonzero return from `rtlsdr_cancel_async` is not necessarily an error.** librtlsdr
   reports the last internal transfer code, often `-5` on a normal cancel. The
   `_stopRequested` flag distinguishes intent.
5. **librtlsdr error codes are documented per function** — check the upstream source before
   assuming what a return value means. Several are non-obvious (`-2` from
   `rtlsdr_set_freq_correction` means "value unchanged", not an error).

The upstream C source is checked out at `../rtl-sdr` (tag `v2.0.3`). Consult it directly
rather than guessing at native behavior.

## Code and Documentation Conventions

These apply to code comments, XML documentation, commit messages, `CHANGELOG.md`, and the
`docs/` guides.

### American English

Always use American English spelling: *behavior*, *synchronization*, *initialize*,
*recognize*, *optimize*, *canceled*, *center*, *analyze*, *license* (noun and verb).
Not *behavior*, *synchronization*, *initialize*, *center*, *licence*.

### Punctuation between connected clauses

When two clauses are closely connected, join them with a semicolon or a colon rather than a
dash. A colon introduces an explanation or a list; a semicolon links two independent but
related statements.

❌ `// Rounding avoids truncation — a plain cast would turn 49.6 into 495`
✅ `// Rounding avoids truncation: a plain cast would turn 49.6 into 495`

❌ `// The buffer is pooled — the caller must return it exactly once`
✅ `// The buffer is pooled; the caller must return it exactly once`

Dashes are still fine for genuine parenthetical asides and for ranges.

### Do not expose `librtlsdr` in customer-facing documentation

**The point of this library is to hide the native layer.** A consumer should be able to use
the public API without knowing that `librtlsdr` exists, how it encodes values, or what its
return codes mean. So in **public** XML documentation (`public` and `protected` members, and
public type summaries), describe behavior in terms of *the device* and *this API*:

❌ `librtlsdr expresses gain in tenths of a dB; this property converts.`
✅ `Gain is expressed in dB. Only the steps listed by SupportedTunerGains are accepted.`

❌ `librtlsdr returns its cached value, which is 0 until a gain is written.`
✅ `Reading the gain before one has been set throws; set TunerGain first.`

**Two deliberate exceptions**, because they are user-actionable rather than implementation
detail:

1. **Installation prerequisites** — the consumer must install the native library, so the
   README, `docs/`, and any "device could not be opened" guidance may name it.
2. **The KerberosSDR fork requirement** — `FrequencyDitheringMode` and `SetGPIO` only work
   against `rtl-sdr-kerberos`. A consumer who does not know this cannot diagnose the
   failure, so those members keep their note and link.

**`internal` and `private` members are exempt** and *should* name native functions, error
codes, and upstream quirks: that is precisely where the reasoning belongs, and it is not
shipped to consumers. Keep the detail; just move it inward.

### Do not cite external source, or name symbols that do not exist here

The previous rule says internal comments *should* explain upstream behavior. This one bounds
how. Two things never belong in a comment, at any visibility:

**Source locations in the upstream C.** They are pinned to a tag, and they rot silently the
moment `../rtl-sdr` moves.

❌ `// librtlsdr bounds neither (librtlsdr.c:1891-1899)`
✅ `// The native layer bounds neither the size nor the count`

**Identifiers that cannot be found in this codebase.** The test is not whether a name looks
foreign, it is whether a reader who greps for it finds anything.

❌ `// passing 0 makes librtlsdr substitute its own DEFAULT_BUF_NUMBER`
❌ `// allocates buf_len bytes buf_num times`
✅ `// zero means "use your own default" to the native layer`

Native **function** names are the opposite case and stay welcome on internal members: every
one this library binds is a `LibraryImport` entry point in `Interop/LibRtlSdr.cs`, so
`rtlsdr_set_center_freq` resolves for anyone who looks. `buf_len` does not, because it lives
only inside the native library. The previous rule already asks for that detail inward; this
one only says point at things that exist here.

Two accepted exceptions: **command-line tools a user runs** (`rtl_test`, `rtl_eeprom`) are
user-actionable rather than implementation detail, so naming them in prose is fine and a
literal command line in `docs/` is the point; and **provenance for verbatim-copied data**,
such as the tuner gain tables in the tests, may cite the upstream **version** so the fixture
can be re-checked, but never a file or line.

Severity: **Warning**. A comment naming something that does not exist is worse than no
comment, because it sends the reader looking.

## Git Rules

**Do not perform any git commands.** The user handles all git operations (commit, branch,
push, stash, etc.) manually. Only read commands (`git diff`, `git status`, `git log`) are
allowed when explicitly needed by a command or skill.

The default branch is **`master`** (not `main`).

## Workflow

Always present a plan before modifying code, even for small changes. Get user approval
before editing files.

## Versioning

The single source of truth is `src/RtlSdrManager/RtlSdrManager.csproj`. A version bump
touches five places — see the `version-bump` skill:

1. `src/RtlSdrManager/RtlSdrManager.csproj`: `<Version>`, `<FileVersion>` and
   `<AssemblyVersion>` (the latter two carry a `.0` fourth component, e.g. `0.8.0.0`).
2. `src/RtlSdrManager/RtlSdrManager.csproj`: `<PackageReleaseNotes>`, trimmed to the current
   version only, with its release date.
3. `samples/RtlSdrManager.Samples/RtlSdrManager.Samples.csproj`: `<Version>`,
   `<FileVersion>`, `<AssemblyVersion>` and `<ProductVersion>`. The samples version tracks
   the library rather than moving independently.
4. `README.md`: the `<PackageReference … Version="…" />` install example.
5. `CHANGELOG.md`: a new dated section, a row in the Version History Summary table, and a
   release-tag footnote link at the bottom.

`tools/HwVerify` carries no version and needs no update.

This project dates every changelog section on release; it does **not** use an `Unreleased`
heading. It follows [Semantic Versioning](https://semver.org/) and
[Keep a Changelog](https://keepachangelog.com/).

Beware historical version references in prose (e.g. *"Since v0.7.1 the default mode stores
each `IQData` as two bytes"* in `README.md`). Those record when a behavior was introduced
and must **not** be bumped.

## Domain Terminology

| Term | Meaning |
|------|---------|
| I/Q | In-phase and Quadrature — the complex baseband sample pair the device delivers |
| Offset binary | The device's 8-bit sample encoding, centered near 127 (subtract before use) |
| RTL2832U | The demodulator/USB chip common to all supported dongles; owns the GPO (GPIO) register |
| Tuner | The RF front-end chip (E4000, FC0012, FC0013, FC2580, R820T, R828D) |
| AGC | Automatic Gain Control — the RTL2832U's internal digital AGC, distinct from tuner gain mode |
| Tuner gain | RF gain, expressed in dB in this API but in **tenths of a dB** by librtlsdr |
| PPM | Parts per million — crystal frequency correction |
| Direct sampling | Bypasses the tuner to receive HF (0–28.8 MHz) via the I- or Q-ADC |
| Offset tuning | Shifts the LO away from DC on zero-IF tuners to dodge the DC spike |
| Bias tee | DC power injected on the antenna feed via an RTL2832U GPIO, for external LNAs |
| LNA | Low-Noise Amplifier — typically mast-mounted, powered by the bias tee |
| KerberosSDR | Four-dongle coherent array (frequency dithering + GPIO); needs the rtl-sdr-kerberos fork |
| Dithering | R820T PLL frequency dithering; must be disabled for coherent operation |
| Raw buffer mode | Zero-copy delivery of pooled `byte[]` buffers instead of per-sample `IQData` |
| MSPS | Mega-samples per second — sample rate (valid: 225001–300000 and 900001–3200000 Hz) |
| SDR | Software Defined Radio |
