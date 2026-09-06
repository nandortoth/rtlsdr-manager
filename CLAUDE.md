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
dotnet run --project tools/HwHealth                 # Is the dongle sustaining delivery right now?
dotnet run --project tools/HwStress                 # Cycle a device hard and report what broke
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

### No build warnings

**Zero warnings is the standard, in every project including the tools.** `.editorconfig`
raises 38 analyzer rules to warning severity on purpose, so a warning is a deliberate signal
rather than noise to be tolerated. Fix the cause; suppress a rule only with a comment saying
why it does not apply here.

**A repeated `dotnet build` does not re-report warnings** for projects it already considers up
to date, so the second run prints `0 Warning(s)` whether or not any exist. That makes an
incremental build worthless as evidence, and it has been reported here as a clean build when
it was not. Confirm with a build that actually recompiles:

```bash
dotnet build --no-incremental      # or: dotnet clean && dotnet build
```

**A pipeline hides the exit code you care about.** `$?` after `cmd | grep …` reports *grep*,
not `cmd`, so a failing command reads as success. This has produced wrong results here, on
runs whose output was filtered for readability. Capture the status before filtering:

```bash
cmd > /tmp/out 2>&1; status=$?      # then inspect /tmp/out
```

Check `Release` as well when a change could behave differently there. Note also that widening
a type's visibility can introduce warnings that did not apply before: rules such as `CA1062`
only fire on externally visible members, so moving a helper into its own assembly can surface
them where extracting it changed nothing else.

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

### Working with hardware

Hardware results mislead in ways that unit tests do not, and every one of these rules was
learned by getting it wrong.

**The dongles degrade with use.** After a few hundred cycles of opening, reading and closing,
they enter a state where they still enumerate and still open, and a single short read may still
succeed, but they cannot sustain a stream. `HwVerify` then fails with `Error code: -3`. It
affects any build equally, patched or not, so it is not a library fault; only physically
replugging clears it. A degraded dongle looks exactly like a software regression, and has been
mistaken for one.

Two specifics, both measured rather than assumed:

- **Readings cause it, not open and close churn.** 200 cycles of `HwStress --pattern openclose`,
  which takes no readings, left a device healthy on both a patched and an unpatched library. A
  tool that opens and closes without streaming can be run freely.
- **The decline can be abrupt.** One device held 100% of the requested rate through 500
  read-bearing cycles and delivered nothing at 600, with no reading in between. Cancel latency
  several times normal has also been seen, so a gradual regime exists too; do not rely on
  either shape as an early warning.

**A USB bus reset is not a replug, and makes things worse.** `libusb_reset_device` looks like a
software replug and is not one: the dongle re-enumerates and streams at the full rate, but the
RTL2832U's test-mode counter stops working and does not come back when the device is reopened.
Measured directly — after a reset the harness scores 64 of 70 with **all six failures being
counter checks**, and a physical replug restores it to 70. Both `HwVerify` and `HwHealth` assert
data integrity through that counter, so resetting a device destroys the tooling's ability to
check the data path while leaving everything else looking fine.

There is no software equivalent of unplugging on macOS. A hub with per-port power switching,
driven by `uhubctl`, is the only real substitute, and that route was considered and not taken:
it needs the dongles on a hub whose switching genuinely works, and a hub that advertises the
capability does not always honor it. **Replugging is the accepted method.** It is rarely
needed in ordinary development, because sustained reading cycles are what wear a device and
`tools/test-degrade.sh` needs several hundred to do it deliberately.

**`tools/HwHealth` is how you tell the difference.** It streams for several seconds and
measures throughput against the requested sample rate, which is the measurement that separates
a working device from a degraded one. Run it **before and after** any hardware measurement,
and treat a run whose health is unknown as a run with no result. It exits `0` healthy, `1`
unhealthy, `2` no device, so it composes into scripts. `RTLSDR_LIBRARY_PATH` lets it probe a
specific native build.

A check that only opens a device and reads once is **not** sufficient: that is exactly what a
degraded dongle still passes.

**What that probe has and has not been shown to catch.** `tools/test-degrade.sh` drove a
dongle from 100% throughput to delivering nothing at all, and the probe caught it: healthy
through 500 reopen cycles, then `no samples arrived within 5 s` at 600. So the tool works
against total failure, which is the state observed most often.

The **throughput threshold itself is not proven against real hardware.** That device went from
99.9% to zero without passing through the 50% mark, so the fraction has only ever been
exercised by deliberately mis-setting it. A partial-degradation regime does exist — cancel
latency several times normal, while still streaming — and no run has yet caught a device in
it. Read an `UNHEALTHY` verdict citing throughput as a plausible reading rather than a
calibrated one, and if a device is ever caught mid-decline, record the fraction here.

**`HwVerify` is a regression gate, not a crash detector.** It performs few cancel cycles, so a
probabilistic native fault can pass it: unmodified `librtlsdr` 2.0.3 scores 70/0/3 despite
carrying a use-after-free. A clean harness run means "these changes broke nothing", never
"the defect is gone". Proving a crash fixed needs enough cycles to make a low-probability fault
near-certain, which is `tools/HwStress` and `tools/test-stress.sh`.

**Comparing two builds requires interleaved same-state controls.** Run them alternately —
A, B, A, B — in one device state, with the unpatched control in the same session. If the
control does not fail, the test cannot detect the defect and a clean result from the other
build means nothing. Replug before anything decisive, and say how many runs a claim rests on.
A single run of one build establishes nothing.

**A bad `RTLSDR_LIBRARY_PATH` is silent.** If the path does not exist, or is not a loadable
library, resolution falls through to the installed copy and nothing is reported. A run you
believe exercised a patched build may have exercised the system one, and it will look
perfectly healthy. Both cases were confirmed: a missing file and a text file each produced a
clean run against the system library.

The `--library` flag on the `test-*.sh` scripts checks the file exists before running, so the
scripted paths are safe; a hand-written `RTLSDR_LIBRARY_PATH=` is not. When the distinction
matters, do not trust the variable — confirm the build behaves differently from the one you
are comparing it against. That is the only check that cannot quietly pass.

**A fixed time budget is not a stall detector.** At the degraded rate a run that is merely slow
looks identical to one that hung. Classify a run as passed, crashed or stalled from what it
did, not from how long it took. `HwStress` judges this from progress, resetting its budget on
every completed cycle, so a slow run is never reported as a stuck one.

### The procedures

Each script composes the tools above into one of the procedures these rules describe, so the
correct method is the easy one rather than something to remember and hand-roll.

```bash
tools/test-release.sh                      # everything that must hold before tagging
tools/test-verify.sh                       # HwHealth, HwVerify, HwHealth
tools/test-stress.sh --pattern reopen      # cycle hard, bracketed by health checks
tools/test-compare.sh --library A --library B   # alternate builds in one device state
tools/test-degrade.sh --library PATCHED    # degrade a dongle and confirm HwHealth notices
tools/test-patches.sh                      # do the vendored patches still apply and compile?
tools/test-version.sh                      # does the version agree everywhere it is written?
```

**`test-release.sh` is the gate**: version consistency, warning-free Debug and Release builds,
the unit tests, the vendored patches, and the harness between health checks. It fails on the
first problem, and a run that skipped a step reports `INCOMPLETE` and exits 3 rather than 0, so
`test-release.sh && git tag …` cannot tag a build whose hardware was never verified. Run it with
no arguments.

It **builds its own corrected `librtlsdr`** from `patches/` for the hardware step. That is
deliberate: the harness closes and reopens the device, which reaches the native use-after-free
on a stock library and kills the process at random, so testing against one would make a release
pass or fail by luck rather than by whether this library is sound. The limitation is worth
knowing — the gate verifies this library against a *corrected* native layer, not against the
one users currently have, which is the right question for a wrapper but is not end-to-end
validation. `--library` overrides the built one when you want to check against something else.

**`test-version.sh` checks the eight places a version is written.** A bump touches four files,
and the list in *Versioning* below tells you where; it does not tell you whether you finished.
The easiest to miss is the release-tag link at the foot of `CHANGELOG.md`. It needs no build,
hardware or network, so run it straight after a bump rather than waiting for the gate. The
CHANGELOG *date* is deliberately not required, since that is set at tag time.

**`test-patches.sh` guards against patch rot.** A vendored patch stops applying silently when
upstream moves or the branch it came from is rebased, and nothing reveals it until someone
needs a patched build. Each patch names its target ref in a leading comment that `git apply`
ignores, and the check applies *and compiles* it. Needs network, needs no hardware.

The other four accept `--library PATH` to point the tools at a specific native build; `test-compare.sh`
takes it more than once, one per build. All stop before measuring anything if the device is not
delivering. `test-degrade.sh` wants a **patched** library, because the point is to wear the
device out rather than to crash the process, and it leaves the dongle degraded: replug after
it.

**`tools/build-native.sh` produces those builds.** It clones `steve-m/librtlsdr` at a chosen
ref, optionally applies patches, and compiles a library the tools can be pointed at, so
"patch, rebuild, measure" is a command rather than a reconstruction:

```bash
tools/build-native.sh --output /tmp/pristine.dylib
tools/build-native.sh --output /tmp/fixed.dylib \
    --patch patches/librtlsdr-async-cancel-fix.diff
tools/test-compare.sh --library /tmp/fixed.dylib --library /tmp/pristine.dylib
```

Patches live in `patches/`, so this needs no checkout beyond this repository. See the
README there for what each one is and how to regenerate it. `--patch` may be repeated, and
`--source DIR` builds an existing checkout instead of cloning.

To produce a patch from a working fork, use a plain diff against the fork's master; `git apply`
does not want the mail-formatted output of `git format-patch`, and a fork clone carries no tags,
so naming a release tag does not resolve:

```bash
git -C ../librtlsdr diff origin/master..HEAD > patches/some-fix.diff
```

Include a build known to be broken in any comparison. If it does not fail, the test cannot
detect the fault, and a clean result from the other build means nothing.

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
tools/HwHealth/                   Sustained-delivery health probe (dongle required)
tools/HwStress/                   Cycling stress tool: crashes and stalls (dongle required)
tools/HwCommon/                   Helpers shared by the hardware tools
tools/build-native.sh             Build a native librtlsdr to test against
tools/test-*.sh                   Procedures composing the Hw* tools
patches/                          Patches against the native library, with provenance
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

Consult the upstream C source directly rather than guessing at native behavior. Nothing in
this repository assumes a checkout of it, so fetch one when needed:

```bash
git clone --depth 1 --branch v2.0.3 https://github.com/steve-m/librtlsdr.git
```

`tools/build-native.sh --keep` leaves its own clone behind and prints the path, which is the
same tree at the same ref if one is already being built.

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
moment upstream moves.

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
touches five places — see the `version-bump` skill. **Run `tools/test-version.sh` after a
bump**: it checks all of them and fails on a mismatch, which the list below cannot do.

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
