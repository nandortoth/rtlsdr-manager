# Console Output Suppression

## Overview

The RTL-SDR Manager library provides control over console output from the native `librtlsdr` library. By default, diagnostic messages are shown. Applications can suppress these messages globally by setting a single static property.

## Why Suppress Console Output?

The native `librtlsdr` library writes diagnostic messages directly to stdout and stderr, including:

- `"Found Rafael Micro R820T tuner"`
- `"[R82XX] PLL not locked!"`
- Tuner initialization messages
- Hardware state changes

While these messages are useful during development and debugging, they can clutter application logs and confuse end users in production environments.

## How It Works

The suppression mechanism works at the native file descriptor level using POSIX `dup()`/`dup2()` system calls on Unix/Linux/macOS and Windows C runtime equivalents on Windows:

1. **Save original descriptors** — Duplicates stdout (FD 1) and stderr (FD 2).
2. **Redirect to null device** — Redirects both to `/dev/null` (Unix) or `NUL` (Windows).
3. **Execute operation** — Runs the `librtlsdr` function with output suppressed.
4. **Restore descriptors** — Restores original stdout/stderr when done.

Suppression is scoped to individual device operations. The library uses a reference-counted global singleton pattern so that concurrent operations on multiple devices share a single suppressor instance without file descriptor corruption.

## API Reference

```csharp
// Static property on RtlSdrDeviceManager
public static bool SuppressLibraryConsoleOutput { get; set; }
```

- **Default** — `false` (diagnostic messages are shown)
- **Scope** — Global; affects all device operations immediately
- **Thread safety** — Protected by `System.Threading.Lock`

## Usage Examples

### Suppressing Output

```csharp
using RtlSdrManager;

// Enable suppression before opening devices
RtlSdrDeviceManager.SuppressLibraryConsoleOutput = true;

var manager = RtlSdrDeviceManager.Instance;
manager.OpenManagedDevice(0, "my-device");
var device = manager["my-device"];

// All operations are silent — no "Found Rafael Micro R820T tuner" messages
device.CenterFrequency = Frequency.FromMHz(1090);
device.SampleRate = Frequency.FromMHz(2);
```

### Enabling Output for Debugging

```csharp
using RtlSdrManager;

// Default is false — messages are shown
var manager = RtlSdrDeviceManager.Instance;
manager.OpenManagedDevice(0, "debug-device");
var device = manager["debug-device"];

// Will show: "Found Rafael Micro R820T tuner"
// Will show: "[R82XX] PLL not locked!" (if applicable)
device.CenterFrequency = Frequency.FromMHz(1090);
```

### Runtime Toggle

Toggle suppression dynamically during operation:

```csharp
using RtlSdrManager;

var manager = RtlSdrDeviceManager.Instance;

// Start with suppression enabled
RtlSdrDeviceManager.SuppressLibraryConsoleOutput = true;
manager.OpenManagedDevice(0, "my-device");
var device = manager["my-device"];

device.SampleRate = Frequency.FromMHz(2);           // Silent
device.CenterFrequency = Frequency.FromMHz(1090);   // Silent

// Disable suppression for troubleshooting
RtlSdrDeviceManager.SuppressLibraryConsoleOutput = false;
device.SampleRate = Frequency.FromMHz(2.4);          // Shows librtlsdr output
device.TunerGain = 40.0;                             // Shows librtlsdr output

// Re-enable suppression
RtlSdrDeviceManager.SuppressLibraryConsoleOutput = true;
device.CenterFrequency = Frequency.FromMHz(1095);   // Silent again
```

### Conditional Debugging

Enable diagnostics based on configuration or command-line flags:

```csharp
using RtlSdrManager;

public class RtlSdrApplication
{
    public RtlSdrApplication(bool debugMode)
    {
        // Suppress output unless running in debug mode
        RtlSdrDeviceManager.SuppressLibraryConsoleOutput = !debugMode;
    }

    public void Run()
    {
        var manager = RtlSdrDeviceManager.Instance;
        manager.OpenManagedDevice(0, "main");
        var device = manager["main"];

        // Output controlled by the debug flag
        device.CenterFrequency = Frequency.FromMHz(1090);
        device.SampleRate = Frequency.FromMHz(2);
    }
}

// Usage
static void Main(string[] args)
{
    bool debugMode = args.Contains("--debug");
    var app = new RtlSdrApplication(debugMode);
    app.Run();
}
```

## Technical Details

### Platform-Specific Implementation

**Unix/Linux/macOS:**
- Uses POSIX `dup()`, `dup2()`, `open()`, `close()` functions from `libc`
- Redirects to `/dev/null` device
- Includes `fflush()` calls to ensure buffer synchronization

**Windows:**
- Uses C runtime `_dup()`, `_dup2()`, `_open()`, `_close()` functions from `msvcrt.dll`
- Redirects to `NUL` device
- Includes `fflush()` calls to ensure buffer synchronization

### Scope of Suppression

Operations affected by suppression:

- Device opening and initialization
- Frequency changes (center frequency, sample rate)
- Gain control operations (AGC, manual gain)
- Tuner configuration (bandwidth, gain mode)
- Advanced features (direct sampling, offset tuning, bias tee)
- All `librtlsdr` diagnostic messages

Operations not affected by suppression:

- .NET `Console.WriteLine()` calls in application code
- Logging from application code
- Exception messages from the library

### Performance Impact

The suppression mechanism has minimal overhead:

- File descriptor operations complete in microseconds
- Suppression is active only during `librtlsdr` function calls
- No buffering or string processing is required
- Zero impact when suppression is disabled

## Best Practices

### Use Default Output in Development

During development, leave suppression disabled (the default) to see `librtlsdr` diagnostics:

```csharp
// Development — default is false, messages are shown
var manager = RtlSdrDeviceManager.Instance;
manager.OpenManagedDevice(0, "dev-device");
```

### Enable Suppression in Production

Suppress output in production to keep logs clean:

```csharp
RtlSdrDeviceManager.SuppressLibraryConsoleOutput = true;
var manager = RtlSdrDeviceManager.Instance;
manager.OpenManagedDevice(0, "production-device");
```

### Use Preprocessor Directives

Toggle suppression based on build configuration:

```csharp
#if !DEBUG
    RtlSdrDeviceManager.SuppressLibraryConsoleOutput = true;
#endif
```

## See Also

- [Basic Setup](BASIC_SETUP.md) — Getting started with RTL-SDR Manager
- [Device Management](DEVICE_MANAGEMENT.md) — Managing multiple devices
- [Main README](../README.md) — Library overview and features
