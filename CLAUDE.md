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
only hardware-independent components. Anything touching a device must be verified manually
with a dongle attached.

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
rather than guessing at native behaviour.

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
touches **four** places in that file plus the docs — see the `version-bump` skill:

1. `<Version>`, `<FileVersion>` and `<AssemblyVersion>` (the latter two carry a `.0` fourth
   component, e.g. `0.7.1.0`).
2. `<PackageReleaseNotes>` — trimmed to the current version only, with its release date.
3. `README.md` — the `<PackageReference … Version="…" />` install example.
4. `CHANGELOG.md` — a new dated section, a row in the Version History Summary table, and a
   release-tag footnote link at the bottom.

This project dates every changelog section on release; it does **not** use an `Unreleased`
heading. It follows [Semantic Versioning](https://semver.org/) and
[Keep a Changelog](https://keepachangelog.com/).

Beware historical version references in prose (e.g. *"Since v0.7.1 the default mode stores
each `IQData` as two bytes"* in `README.md`). Those record when a behaviour was introduced
and must **not** be bumped.

## Domain Terminology

| Term | Meaning |
|------|---------|
| I/Q | In-phase and Quadrature — the complex baseband sample pair the device delivers |
| Offset binary | The device's 8-bit sample encoding, centred near 127 (subtract before use) |
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
