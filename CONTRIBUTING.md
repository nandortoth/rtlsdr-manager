# Contributing to RTL-SDR Manager

Thank you for your interest in contributing to RTL-SDR Manager! This document provides guidelines and instructions for contributing to the project.

## Table of Contents

- [Code of Conduct](#code-of-conduct)
- [Getting Started](#getting-started)
- [Development Setup](#development-setup)
- [How to Contribute](#how-to-contribute)
- [Coding Standards](#coding-standards)
- [Pull Request Process](#pull-request-process)
- [Publishing Releases](#publishing-releases)
- [Reporting Bugs](#reporting-bugs)
- [Suggesting Features](#suggesting-features)

## Code of Conduct

This project adheres to the [Contributor Covenant Code of Conduct](CODE_OF_CONDUCT.md). By participating, you are expected to uphold this code. Please report unacceptable behavior to the project maintainers.

## Getting Started

### Prerequisites

Before you begin, ensure you have the following installed:

- **.NET 10.0 SDK or later** — [Download here](https://dotnet.microsoft.com/download)
- **Git** — Version control system
- **librtlsdr** — Native RTL-SDR library for your platform (2.x; 2.0.3 or later recommended)
  - **Windows:** `choco install rtl-sdr` or download from [releases](https://github.com/osmocom/rtl-sdr/releases)
  - **Linux:** `sudo apt-get install librtlsdr-dev`
  - **macOS:** `brew install librtlsdr`
- **IDE** (recommended):
  - JetBrains Rider
  - Visual Studio 2022
  - Visual Studio Code with the C# extension

### Fork and Clone

1. Fork the repository on GitHub.
2. Clone your fork locally:
   ```bash
   git clone https://github.com/YOUR-USERNAME/rtlsdr-manager.git
   cd rtlsdr-manager
   ```
3. Add the upstream repository:
   ```bash
   git remote add upstream https://github.com/nandortoth/rtlsdr-manager.git
   ```
4. Keep your fork up to date:
   ```bash
   git fetch upstream
   git checkout master
   git merge upstream/master
   ```

## Development Setup

### Building the Project

```bash
# Restore dependencies
dotnet restore

# Build the solution
dotnet build

# Build in Release mode
dotnet build --configuration Release

# Or use the convenience script
./build.sh
```

### Running Tests

```bash
# Run all tests
dotnet test

# Run tests with detailed output
dotnet test --verbosity detailed
```

The automated suite covers only hardware-independent components, so it needs no RTL-SDR
device and no `librtlsdr` installation. It runs anywhere.

### Hardware Verification

Behavior that depends on a real device cannot be covered by the test suite. Three tools in
`tools/` cover it instead, each answering a different question:

| Tool | Question it answers |
|------|---------------------|
| `HwVerify` | Does the library still behave correctly across every feature? |
| `HwHealth` | Is this dongle sustaining sample delivery *right now*? |
| `HwStress` | Does cycling the device hard produce a crash or a stall? |

The `tools/test-*.sh` scripts compose them into the procedures below, and every script
refuses to measure anything unless the device is delivering first. Attach a dongle and run
the harness before submitting a change that touches device behavior, and before a release:

```bash
# The harness, between two device health checks
tools/test-verify.sh

# Arguments pass through, so the bias tee can be exercised too
# (disconnect the antenna first)
tools/test-verify.sh --biastee-on
```

Each check prints `PASS`, `FAIL`, or `SKIP`, and the tool exits nonzero if anything failed.
A `SKIP` names the reason, usually that the attached tuner cannot exercise that path; some
checks need a specific tuner, so a clean run on one dongle does not always prove a fix.

#### Device health comes first

**A dongle that has been cycled a few hundred times stops sustaining a stream while still
enumerating and still opening.** `HwVerify` then fails with `Error code: -3`, and it looks
exactly like a regression you just introduced. It affects any build equally, so it is not a
library fault, and only physically replugging clears it.

This has cost real debugging time. Before trusting any hardware result, and before filing a
bug against a change:

```bash
dotnet run --project tools/HwHealth        # HEALTHY, or the reason it is not
```

The `test-*.sh` scripts run this for you and stop if the device is not delivering, which is
why they are the recommended way in. Two things worth knowing: readings cause the wear, not
opening and closing, so a tool that does not stream can be run freely; and the decline can be
abrupt rather than gradual, so a healthy reading a hundred cycles ago proves nothing now.

#### Proving a crash is fixed

**`HwVerify` is a regression gate, not a crash detector.** It performs few cancel cycles, so
a fault that fires with low probability passes it comfortably: unmodified `librtlsdr` 2.0.3
scores a clean run despite carrying a use-after-free. A green harness means "this change
broke nothing", never "the defect is gone".

Showing a crash is present, or gone, needs enough cycles to make a low-probability fault
near-certain:

```bash
tools/test-stress.sh --pattern reopen --cycles 100 --runs 10
```

Runs are classified as completed, crashed, or stalled, and those are never collapsed
together: a slow run and a stuck one are different findings.

**Expect this to fail against a stock `librtlsdr`.** Version 2.0.3 and earlier carry the
use-after-free described under Known Limitations in the README, so the `reopen` and `restart`
patterns crash within a few hundred cycles on an unpatched system library. That is the defect,
not your change. To separate the two, build a fixed library and compare, as below; a change of
your own shows up as a difference between the two builds rather than as a crash in both.

#### Comparing native library builds

When a fix lives in the native library rather than here, build both and alternate them.
Patches live in `patches/`, so this needs no checkout beyond this repository:

```bash
tools/build-native.sh --output /tmp/pristine.dylib
tools/build-native.sh --output /tmp/fixed.dylib \
    --patch patches/librtlsdr-async-cancel-fix.diff

tools/test-compare.sh --library /tmp/fixed.dylib --library /tmp/pristine.dylib
```

**Include a build known to be broken.** If it does not fail, the test cannot detect the
fault and a clean result from the other build means nothing. Running all of one build and
then all of the other does not work here: device state drifts by enough to reverse a
conclusion, which is why `test-compare.sh` interleaves them.

The harness restores what it changes. The tuner gain mode and the direct sampling mode are
each captured on entry and put back on exit, including when a check fails, so the device is
not left in manual mode at whatever gain the last check happened to set, or still sampling
directly. The bias tee is only ever written with `Disabled`, because turning power off is
always safe, and only on pin 0. The `--biastee-on` flag additionally enables it for a moment
so the feed voltage can be metered; do not use that flag with a passive antenna connected.

Two properties of the hardware shape those rules, and any check you add has to respect them:

- **The bias tee stays powered after the device is closed.** Nothing clears the pin on
  teardown, so the harness always disables it on the way out, on the failure path too.
- **Setting a GPIO pin also switches it to output mode, and that is not reversible from
  software.** So the harness only ever touches pin 0, the pin the bias tee already uses.
  Probing an unrelated pin would leave it reconfigured until the dongle is replugged.

**Leave the device as you found it.** A verification tool that quietly reconfigures hardware
is worse than no tool, because the next person cannot tell which state is real.

**Extend it when you fix something hardware-dependent.** A fix verified only by hand is a fix
nobody can re-verify later.

#### Adding a check

Each group of checks is an `IHardwareCheck` in `tools/HwVerify/Checks/`:

```csharp
internal sealed class MyChecks : IHardwareCheck
{
    public string Title => "My feature";

    public void Run(RtlSdrManagedDevice device, VerificationReport report)
    {
        report.Check("what this asserts",
            () => /* true when the behavior is correct */,
            "what should have happened, printed only on failure");
    }
}
```

Register it in `Program.BuildChecks`, which controls the order. Three conventions to follow:

- **Say what the behavior used to be** in the expectation string when a release changed it.
  A failure then reads as a regression rather than an unexplained mismatch.
- **Report `SKIP` with a reason** when the attached hardware cannot exercise a path, rather
  than passing silently. `VerificationReport.Skip` exists for that. Use
  `VerificationReport.Fail` for the different case of a group that could not run at all: an
  incomplete run has to exit nonzero, or it reads as a clean one.
- **Restore any device state you change**, in a `finally`, and prove it by running the
  harness twice: the second run must produce the same output as the first.

Order matters in one place. `DirectSamplingChecks` runs after `CenterFrequencyChecks` because
it relies on the device being tuned above the ADC's reach, which is the case worth exercising
when direct sampling is switched on.

### Running Samples

```bash
# Using the convenience script
./runsample.sh

# Or manually
dotnet run --project samples/RtlSdrManager.Samples

# Build and run in Release mode
dotnet run --project samples/RtlSdrManager.Samples --configuration Release
```

### Verify Code Style

The project uses `.editorconfig` for code style enforcement. Most IDEs apply these rules automatically.

```bash
# Format code according to .editorconfig
dotnet format

# Check formatting without making changes
dotnet format --verify-no-changes
```

## How to Contribute

### Types of Contributions

We welcome various types of contributions:

- **Bug fixes** — Fix issues and improve stability
- **New features** — Add new functionality
- **Documentation** — Improve or add documentation
- **Tests** — Add or improve test coverage
- **Code quality** — Refactoring and improvements
- **Examples** — Add sample applications
- **Tooling** — Improve build scripts and tools

### Contribution Workflow

1. **Check existing issues** — Look for existing issues or create a new one.
2. **Discuss major changes** — For significant changes, open an issue first to discuss the approach.
3. **Create a branch** — Use a descriptive branch name (see below).
4. **Make your changes** — Follow the coding standards.
5. **Write tests** — Add tests for new functionality.
6. **Update documentation** — Update relevant docs and XML comments.
7. **Commit your changes** — Use clear commit messages.
8. **Push to your fork** — Push your branch to GitHub.
9. **Open a Pull Request** — Submit your PR with a clear description.

### Branch Naming Convention

Use descriptive branch names following this pattern:

```
feature/description       # New features
bugfix/description        # Bug fixes
docs/description          # Documentation updates
refactor/description      # Code refactoring
test/description          # Test additions/improvements
```

Examples:

```bash
git checkout -b feature/add-async-cancellation
git checkout -b bugfix/fix-frequency-overflow
git checkout -b docs/improve-readme-examples
```

## Coding Standards

### Code Style

This project enforces code style through `.editorconfig`. Key rules:

#### Formatting

- **Indentation:** 4 spaces (no tabs)
- **Line endings:** LF (Unix-style)
- **Braces:** Allman style (opening brace on new line)
- **File-scoped namespaces:** Required for new code

```csharp
// Good — file-scoped namespace
namespace RtlSdrManager;

public class MyClass
{
    public void MyMethod()
    {
        // Method body
    }
}

// Bad — block-scoped namespace (legacy only)
namespace RtlSdrManager
{
    public class MyClass { }
}
```

#### Naming Conventions

| Element | Convention | Example |
|---|---|---|
| Classes, Methods, Properties | `PascalCase` | `DeviceManager`, `OpenDevice` |
| Private fields | `_camelCase` | `_deviceName`, `_deviceCount` |
| Parameters, Local variables | `camelCase` | `friendlyName`, `deviceIndex` |
| Constants | `PascalCase` | `MaxDevices` |
| Interfaces | `IPascalCase` | `IDisposable` |

#### var Usage

- **Do not use** for built-in types: `int`, `string`, `bool`, etc.
- **Use** when the type is obvious from the right-hand side: `new ClassName()`
- **Do not use** when the type is unclear: method return values

```csharp
// Good
int count = 5;
string name = "test";
var manager = new RtlSdrDeviceManager();
var frequency = new Frequency(1000);

// Bad
var count = 5;                    // Use explicit type for primitives
var result = GetSomething();      // Type not obvious
```

### Writing Conventions

These apply to code comments, XML documentation, commit messages, `CHANGELOG.md`, and the
`docs/` guides.

**American English.** *behavior*, *synchronization*, *initialize*, *center*, *canceled*,
*analyze* — not *behaviour*, *synchronisation*, *initialise*, *centre*, *cancelled*.

**Join connected clauses with a semicolon or a colon, not a dash.** A colon introduces an
explanation or a list; a semicolon links two related independent statements. Dashes are still
right for genuine parenthetical asides and for ranges.

```csharp
// Good
// Rounding avoids truncation: a plain cast would turn 49.6 into 495

// Avoid
// Rounding avoids truncation — a plain cast would turn 49.6 into 495
```

**Do not name `librtlsdr` in public XML documentation.** The point of this library is to hide
the native layer, and the documentation ships inside the NuGet package, so a leaked
implementation detail reaches consumers' IntelliSense. Describe behavior in terms of *the
device* and *this API*:

```csharp
// Good
/// Gain is expressed in dB; only the steps listed by SupportedTunerGains are accepted.

// Avoid
/// librtlsdr expresses gain in tenths of a dB; this property converts.
```

Two deliberate exceptions, because they are user-actionable rather than implementation
detail: **installation prerequisites** (the consumer must install the native library) and
**the KerberosSDR fork requirement** on `FrequencyDitheringMode` and `SetGPIO`.

`internal` and `private` members are exempt, and *should* name native functions, error codes,
and upstream quirks. That is where the reasoning belongs, and it is not shipped. When a public
comment needs native detail, move it to an internal member rather than deleting it.

### XML Documentation

All public APIs must have XML documentation:

```csharp
/// <summary>
/// Opens an RTL-SDR device for management.
/// </summary>
/// <param name="index">The device index (0-based).</param>
/// <param name="friendlyName">A friendly name to reference the device.</param>
/// <exception cref="ArgumentNullException">Thrown when friendlyName is null.</exception>
/// <exception cref="ArgumentException">Thrown when friendlyName is empty or a device with that name already exists.</exception>
/// <exception cref="RtlSdrDeviceException">Thrown when the device cannot be opened.</exception>
public void OpenManagedDevice(uint index, string friendlyName)
{
    // Implementation
}
```

### Exception Handling

Use appropriate exception types:

```csharp
// Good — proper exception types
if (friendlyName == null)
    throw new ArgumentNullException(nameof(friendlyName));

if (string.IsNullOrWhiteSpace(friendlyName))
    throw new ArgumentException("Cannot be empty", nameof(friendlyName));

if (!deviceExists)
    throw new RtlSdrDeviceException($"Device {index} not found");

// Bad — wrong exception types
if (friendlyName == null)
    throw new Exception("Name is null");  // Too generic

if (!deviceExists)
    throw new IndexOutOfRangeException();  // Wrong type
```

### Dispose Pattern

For classes managing unmanaged resources:

```csharp
public class MyResource : IDisposable
{
    private bool _disposed;

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    private void Dispose(bool disposing)
    {
        if (_disposed)
            return;

        if (disposing)
        {
            // Dispose managed resources
        }

        // Release unmanaged resources

        _disposed = true;
    }

    ~MyResource()
    {
        Dispose(disposing: false);
    }
}
```

### Async/Await

```csharp
// Good — proper async/await with cancellation
public async Task<IQData> ReadDataAsync(CancellationToken cancellationToken = default)
{
    await Task.Delay(100, cancellationToken);
    return new IQData();
}

// Good — dispose IDisposable in async methods
public async Task ProcessDataAsync()
{
    using var cts = new CancellationTokenSource();
    await ReadDataAsync(cts.Token);
}

// Bad — async void (only for event handlers)
public async void ProcessData()
{
    await Task.Delay(100);
}
```

## Pull Request Process

### Before Submitting

Ensure your PR meets these requirements:

- [ ] Code follows the project's style guidelines (`.editorconfig`)
- [ ] Code builds without warnings: `dotnet build --no-incremental` (a plain `dotnet build`
      does not re-report warnings for projects it considers up to date, so it can print zero
      while warnings exist)
- [ ] All tests pass: `dotnet test`
- [ ] `tools/test-verify.sh` passes, if the change touches device behavior
- [ ] New code has XML documentation comments
- [ ] README.md is updated (if needed)
- [ ] CHANGELOG.md is updated with your changes
- [ ] Commit messages are clear and descriptive

### PR Title Format

Use the same style as a commit subject: one line, imperative mood, no trailing period, and
**no conventional-commits prefix** such as `feat:` or `fix:`. Name the API surface affected,
since consumers read the history to understand an upgrade.

```
Add support for async cancellation tokens
Fix frequency overflow in Frequency arithmetic
Improve the README installation instructions
Simplify device manager initialization
Add unit tests for the Frequency type
```

Keep it under 80 characters where you can; up to about 100 is acceptable for a change that
genuinely needs it. Breaking changes are recorded in `CHANGELOG.md`, marked `**BREAKING**`,
rather than flagged in the title.

### PR Description

Include in your PR description:

- **What** the PR does and **why**.
- How you **tested** the changes.
- Any **breaking changes** or **migration steps** required.

### Review Process

1. A maintainer will review your PR.
2. Feedback may be provided — please address review comments.
3. Once approved, a maintainer will merge your PR.
4. Your contribution will be included in the next release.

## Publishing Releases

> **Note:** Publishing to [NuGet.org](https://www.nuget.org/packages/RtlSdrManager) is a maintainer task and requires a NuGet API key with push rights for the `RtlSdrManager` package.

Before any of this, run the release gate with a dongle attached. It checks the version, both
build configurations, the tests, the vendored patches and the hardware harness, and it refuses
to report success if a step was skipped:

```bash
tools/test-release.sh
```

It builds its own corrected `librtlsdr` for the hardware step. Until upstream ships a fix, a
stock native library kills the harness at random through the use-after-free described under
Known Limitations in the README, which would make a release pass or fail by luck. So the gate
verifies this library against a corrected native layer — the right question for a wrapper, and
not the same as validating what a user with a stock library will experience.

### Release Steps

1. **Bump the version.** It appears in more places than the one project file, and they all have to move together:
   - `src/RtlSdrManager/RtlSdrManager.csproj` — `<Version>`, plus `<FileVersion>` and `<AssemblyVersion>`, which carry a fourth component (`0.8.0.0`).
   - `src/RtlSdrManager/RtlSdrManager.csproj` — `<PackageReleaseNotes>`, trimmed to the current version only, with its release date.
   - `samples/RtlSdrManager.Samples/RtlSdrManager.Samples.csproj` — `<Version>`, `<FileVersion>`, `<AssemblyVersion>` and `<ProductVersion>`. The samples version tracks the library rather than moving independently.
   - `README.md` — the `<PackageReference … Version="…" />` install example.

   Leave historical references alone: prose such as *"Since v0.7.1 the default mode stores each `IQData` as two bytes"* records when a behavior was introduced and stays put.
2. **Update `CHANGELOG.md`** — add a dated section, a row in the Version History Summary table, and a release-tag footnote link at the bottom. Mark incompatible changes `**BREAKING**`.
3. **Build the packages** — this cleans `artifacts/` and produces the `.nupkg` and `.snupkg`:
   ```bash
   ./build.sh
   ```
4. **Publish to NuGet.org:**
   ```bash
   ./publish.sh
   ```
   The script lists the packages found in `artifacts/packages/`, asks for confirmation, then pushes. Pushing the `.nupkg` automatically publishes the paired `.snupkg` symbol package — you do not push it separately.
5. **Create a [GitHub release](https://github.com/nandortoth/rtlsdr-manager/releases)** for the new version — publishing the release creates the git tag.

### API Key Resolution

`publish.sh` resolves the NuGet API key in this order:

1. `--api-key <key>` — an explicit key for that run only.
2. **macOS Keychain** — service `nuget.org`, account `rtlsdr-manager-publish` (macOS only).
3. `NUGET_API_KEY` — environment variable.

If none is found, the script prompts for the key and offers to save it to the macOS Keychain (recommended) or use it just for the current run. Create a key at [nuget.org/account/apikeys](https://www.nuget.org/account/apikeys); scope it to the `RtlSdrManager` package with push permission.

```bash
# Publish with an explicit key (e.g. in a non-interactive shell)
./publish.sh --api-key <key>

# Show all options
./publish.sh --help
```

## Reporting Bugs

### Before Reporting

- Check if the bug has already been reported in [Issues](https://github.com/nandortoth/rtlsdr-manager/issues).
- Ensure you are using the latest version.
- Verify the issue is reproducible.

### Bug Report Contents

When reporting bugs, include:

- **Description** — Clear description of the bug.
- **Steps to reproduce** — Minimal steps to trigger the issue.
- **Expected vs. actual behavior** — What you expected and what happened.
- **Environment** — OS, .NET version, RtlSdrManager version, RTL-SDR device model, librtlsdr version.
- **Logs** — Relevant log output or exception stack traces.

## Suggesting Features

When suggesting features:

1. **Check existing issues** — See if it has already been suggested.
2. **Describe the use case** — Why is this feature needed? What problem does it solve?
3. **Provide examples** — How would the API look?
4. **Consider alternatives** — Are there other approaches?

## License

By contributing to RTL-SDR Manager, you agree that your contributions will be licensed under the [GNU General Public License v3.0 or later](LICENSE.md).

---

Thank you for contributing to RTL-SDR Manager! Your efforts help make this project better for the SDR community.
