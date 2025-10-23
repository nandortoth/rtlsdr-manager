# Console Output Suppression

## Overview

The RTL-SDR Manager library provides comprehensive control over console output from the native `librtlsdr` library. By default, all diagnostic messages are suppressed to provide a cleaner user experience in production applications.

## Why Suppress Console Output?

The native `librtlsdr` library writes diagnostic messages directly to stdout and stderr, including:

- `"Found Rafael Micro R820T tuner"`
- `"[R82XX] PLL not locked!"`
- Tuner initialization messages
- Hardware state changes
- Debug information

While these messages are useful during development and debugging, they can clutter application logs and confuse end users in production environments.

## How It Works

The suppression mechanism works at the **native file descriptor level** using POSIX `dup()`/`dup2()` system calls on Unix/Linux/macOS and Windows C runtime equivalents on Windows:

1. **Save Original Descriptors**: Duplicates stdout (FD 1) and stderr (FD 2)
2. **Redirect to Null Device**: Redirects both to `/dev/null` (Unix) or `NUL` (Windows)
3. **Execute Operation**: Runs the librtlsdr function with output suppressed
4. **Restore Descriptors**: Restores original stdout/stderr when done

This approach catches **all** output from the native library, regardless of whether it uses stdout, stderr, or direct file descriptor writes.

## Usage Examples

### Basic Usage (Default Behavior)

By default, all console output is suppressed:

```csharp
using RtlSdrManager;

var manager = RtlSdrDeviceManager.Instance;

// Open device - no "Found Rafael Micro R820T tuner" message
manager.OpenManagedDevice(0, "my-device");
var device = manager["my-device"];

// Configure device - no "[R82XX] PLL not locked!" messages
device.CenterFrequency = Frequency.FromMHz(1090);
device.SampleRate = Frequency.FromMHz(2);
device.TunerGainMode = TunerGainModes.Manual;
device.TunerGain = 42.1;

// All operations are silent by default
```

### Enabling Output for Debugging

Enable output globally for all new devices:

```csharp
using RtlSdrManager;

var manager = RtlSdrDeviceManager.Instance;

// Disable suppression globally
RtlSdrDeviceManager.SuppressLibraryConsoleOutput = false;

// New devices will show all librtlsdr messages
manager.OpenManagedDevice(0, "debug-device");
var device = manager["debug-device"];

// Will show: "Found Rafael Micro R820T tuner"
// Will show: "[R82XX] PLL not locked!" (if applicable)
device.CenterFrequency = Frequency.FromMHz(1090);
```

### Per-Device Control

Control suppression independently for each device:

```csharp
using RtlSdrManager;

var manager = RtlSdrDeviceManager.Instance;

// Open two devices with default suppression
manager.OpenManagedDevice(0, "device1");
manager.OpenManagedDevice(1, "device2");

var device1 = manager["device1"];
var device2 = manager["device2"];

// Enable output only for device1
device1.SuppressLibraryConsoleOutput = false;

// device1 shows output, device2 is silent
device1.SampleRate = Frequency.FromMHz(2);    // Shows librtlsdr messages
device2.SampleRate = Frequency.FromMHz(2);    // Silent
```

### Runtime Toggle

Toggle suppression dynamically during operation:

```csharp
using RtlSdrManager;

var manager = RtlSdrDeviceManager.Instance;
manager.OpenManagedDevice(0, "my-device");
var device = manager["my-device"];

// Start with suppression enabled (default)
Console.WriteLine("=== Silent Mode ===");
device.SampleRate = Frequency.FromMHz(2.0);
device.CenterFrequency = Frequency.FromMHz(1090);

// Enable output for troubleshooting
device.SuppressLibraryConsoleOutput = false;
Console.WriteLine("\n=== Debug Mode ===");
device.SampleRate = Frequency.FromMHz(2.4);   // Shows output
device.TunerGain = 40.0;                      // Shows output

// Disable output again
device.SuppressLibraryConsoleOutput = true;
Console.WriteLine("\n=== Silent Mode Restored ===");
device.CenterFrequency = Frequency.FromMHz(1095);  // Silent again
```

### Mixed Device Configuration

Use different settings for different devices in the same application:

```csharp
using RtlSdrManager;

var manager = RtlSdrDeviceManager.Instance;

// Production device - keep suppression enabled
manager.OpenManagedDevice(0, "production");
var prodDevice = manager["production"];
prodDevice.SuppressLibraryConsoleOutput = true;

// Development device - enable diagnostic output
manager.OpenManagedDevice(1, "development");
var devDevice = manager["development"];
devDevice.SuppressLibraryConsoleOutput = false;

// Production device runs silently
prodDevice.CenterFrequency = Frequency.FromMHz(1090);
prodDevice.SampleRate = Frequency.FromMHz(2);

// Development device shows all diagnostics
devDevice.CenterFrequency = Frequency.FromMHz(1090);
devDevice.SampleRate = Frequency.FromMHz(2);
```

## Use Cases

### 1. Production Applications

**Scenario**: End-user facing application where clean logs are important.

```csharp
// Keep suppression enabled (default)
var manager = RtlSdrDeviceManager.Instance;
manager.OpenManagedDevice(0, "main");
var device = manager["main"];

// All operations are silent - users see only application messages
Logger.Info("Configuring RTL-SDR receiver...");
device.CenterFrequency = Frequency.FromMHz(1090);
device.SampleRate = Frequency.FromMHz(2);
Logger.Info("Receiver configured successfully");
```

### 2. Development and Testing

**Scenario**: Debugging tuner issues during development.

```csharp
// Enable output to see hardware diagnostics
RtlSdrDeviceManager.SuppressLibraryConsoleOutput = false;

var manager = RtlSdrDeviceManager.Instance;
manager.OpenManagedDevice(0, "test");
var device = manager["test"];

// See tuner initialization messages
Console.WriteLine("Testing tuner configuration...");
device.CenterFrequency = Frequency.FromMHz(1090);
device.TunerGainMode = TunerGainModes.Manual;

// Observe PLL lock status
device.TunerGain = 42.1;
```

### 3. Logging and Diagnostics

**Scenario**: Capture librtlsdr messages for analysis without displaying to console.

```csharp
using System.IO;

// Redirect console output to capture librtlsdr messages
var originalOut = Console.Out;
var logWriter = new StringWriter();
Console.SetOut(logWriter);

// Enable librtlsdr output
var manager = RtlSdrDeviceManager.Instance;
RtlSdrDeviceManager.SuppressLibraryConsoleOutput = false;

manager.OpenManagedDevice(0, "logging-test");
var device = manager["logging-test"];
device.CenterFrequency = Frequency.FromMHz(1090);

// Restore and analyze captured output
Console.SetOut(originalOut);
string capturedOutput = logWriter.ToString();
Logger.Debug($"LibRtlSdr output: {capturedOutput}");
```

### 4. Multi-Device Setup

**Scenario**: Managing multiple RTL-SDR devices with different logging requirements.

```csharp
var manager = RtlSdrDeviceManager.Instance;

// Primary device - silent operation
manager.OpenManagedDevice(0, "primary");
var primary = manager["primary"];
primary.SuppressLibraryConsoleOutput = true;

// Secondary device - diagnostic mode
manager.OpenManagedDevice(1, "secondary");
var secondary = manager["secondary"];
secondary.SuppressLibraryConsoleOutput = false;

// Reference device - silent operation
manager.OpenManagedDevice(2, "reference");
var reference = manager["reference"];
reference.SuppressLibraryConsoleOutput = true;

// Only secondary device shows output
ConfigureAllDevices(primary, secondary, reference);
```

### 5. Conditional Debugging

**Scenario**: Enable diagnostics based on configuration or command-line flags.

```csharp
using RtlSdrManager;

public class RtlSdrApplication
{
    private readonly bool _debugMode;

    public RtlSdrApplication(bool debugMode)
    {
        _debugMode = debugMode;

        // Set global default based on debug mode
        RtlSdrDeviceManager.SuppressLibraryConsoleOutput = !debugMode;
    }

    public void Run()
    {
        var manager = RtlSdrDeviceManager.Instance;
        manager.OpenManagedDevice(0, "main");
        var device = manager["main"];

        if (_debugMode)
        {
            Console.WriteLine("=== Debug Mode Enabled ===");
            Console.WriteLine("LibRtlSdr diagnostics will be shown");
        }

        // Configure device - output controlled by debug mode
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

## API Reference

### Global Setting

```csharp
// Property on RtlSdrDeviceManager
public static bool SuppressLibraryConsoleOutput { get; set; }
```

- **Default**: `true` (suppression enabled)
- **Purpose**: Sets the default for newly opened devices
- **Scope**: Global - affects all devices opened after the value is changed

### Per-Device Setting

```csharp
// Property on RtlSdrManagedDevice
public bool SuppressLibraryConsoleOutput { get; set; }
```

- **Default**: Inherited from `RtlSdrDeviceManager.SuppressLibraryConsoleOutput` at device open time
- **Purpose**: Controls suppression for a specific device instance
- **Scope**: Instance - can be changed at runtime without affecting other devices

## Technical Details

### Platform-Specific Implementation

**Unix/Linux/macOS**:
- Uses POSIX `dup()`, `dup2()`, `open()`, `close()` functions from `libc`
- Redirects to `/dev/null` device
- Includes `fflush()` calls to ensure buffer synchronization

**Windows**:
- Uses C runtime `_dup()`, `_dup2()`, `_open()`, `_close()` functions from `msvcrt.dll`
- Redirects to `NUL` device
- Includes `fflush()` calls to ensure buffer synchronization

### Scope of Suppression

The suppression affects:
- ✅ Device opening and initialization
- ✅ Frequency changes (center frequency, sample rate)
- ✅ Gain control operations (AGC, manual gain)
- ✅ Tuner configuration (bandwidth, gain mode)
- ✅ Advanced features (direct sampling, offset tuning, bias tee)
- ✅ All librtlsdr diagnostic messages

The suppression does NOT affect:
- ❌ .NET Console.WriteLine() calls in your application
- ❌ Logging from your application code
- ❌ Exception messages from the library

### Performance Impact

The suppression mechanism has minimal performance overhead:
- File descriptor operations are very fast (microseconds)
- Only active during librtlsdr function calls
- No buffering or string processing required
- Zero impact when suppression is disabled

## Best Practices

### 1. Use Default Suppression in Production

```csharp
// Production code - rely on defaults
var manager = RtlSdrDeviceManager.Instance;
manager.OpenManagedDevice(0, "production-device");
// Suppression is enabled by default
```

### 2. Enable Diagnostics During Development

```csharp
#if DEBUG
    RtlSdrDeviceManager.SuppressLibraryConsoleOutput = false;
#endif
```

### 3. Per-Device Control for Mixed Scenarios

```csharp
// Multiple devices with different requirements
var mainDevice = manager["main"];
mainDevice.SuppressLibraryConsoleOutput = true;  // Production device

var testDevice = manager["test"];
testDevice.SuppressLibraryConsoleOutput = false; // Test device
```

### 4. Temporary Diagnostics

```csharp
// Temporarily enable for troubleshooting
var originalSetting = device.SuppressLibraryConsoleOutput;
try
{
    device.SuppressLibraryConsoleOutput = false;
    // Diagnose issue
    device.CenterFrequency = Frequency.FromMHz(1090);
}
finally
{
    device.SuppressLibraryConsoleOutput = originalSetting;
}
```

## Troubleshooting

### Messages Still Appearing

If you still see console output:

1. **Check the setting is enabled**:
   ```csharp
   Console.WriteLine($"Suppression enabled: {device.SuppressLibraryConsoleOutput}");
   ```

2. **Verify timing** - The setting must be applied before the operation:
   ```csharp
   device.SuppressLibraryConsoleOutput = true;  // Set first
   device.SampleRate = Frequency.FromMHz(2);     // Then perform operation
   ```

3. **Check for .NET console writes** - Make sure the output is from librtlsdr, not your code

### Platform-Specific Issues

If suppression doesn't work on a specific platform:

- The implementation gracefully falls back to no suppression if the native APIs fail
- Check that the platform supports the required POSIX/Win32 functions
- Look for any platform-specific restrictions on file descriptor manipulation

## See Also

- [Basic Setup](BASIC_SETUP.md) - Getting started with RTL-SDR Manager
- [Device Management](DEVICE_MANAGEMENT.md) - Managing multiple devices
- [Manual Gain Control](MANUAL_GAIN_CONTROL.md) - Advanced tuner configuration
- [Main README](../README.md) - Library overview and features
