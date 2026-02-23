# Contributing to RTL-SDR Manager

Thank you for your interest in contributing to RTL-SDR Manager! This document provides guidelines and instructions for contributing to the project.

## Table of Contents

- [Code of Conduct](#code-of-conduct)
- [Getting Started](#getting-started)
- [Development Setup](#development-setup)
- [How to Contribute](#how-to-contribute)
- [Coding Standards](#coding-standards)
- [Pull Request Process](#pull-request-process)
- [Reporting Bugs](#reporting-bugs)
- [Suggesting Features](#suggesting-features)

## Code of Conduct

This project adheres to the [Contributor Covenant Code of Conduct](CODE_OF_CONDUCT.md). By participating, you are expected to uphold this code. Please report unacceptable behavior to the project maintainers.

## Getting Started

### Prerequisites

Before you begin, ensure you have the following installed:

- **.NET 10.0 SDK or later** — [Download here](https://dotnet.microsoft.com/download)
- **Git** — Version control system
- **librtlsdr** — Native RTL-SDR library for your platform
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
# Run all tests (when available)
dotnet test

# Run tests with detailed output
dotnet test --verbosity detailed

# Run tests with coverage (if configured)
dotnet test --collect:"XPlat Code Coverage"
```

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
- [ ] Code builds without warnings: `dotnet build`
- [ ] All tests pass (when tests exist)
- [ ] New code has XML documentation comments
- [ ] README.md is updated (if needed)
- [ ] CHANGELOG.md is updated with your changes
- [ ] Commit messages are clear and descriptive

### PR Title Format

Use a clear, descriptive title following conventional commits:

```
feat: Add support for async cancellation tokens
fix: Correct frequency overflow in calculations
docs: Improve README installation instructions
refactor: Simplify device manager initialization
test: Add unit tests for Frequency type
```

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
