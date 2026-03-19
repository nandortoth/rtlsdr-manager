# RTL-SDR Manager Library for .NET

[![NuGet Version](https://img.shields.io/nuget/v/RtlSdrManager.svg)](https://www.nuget.org/packages/RtlSdrManager/)
[![NuGet Downloads](https://img.shields.io/nuget/dt/RtlSdrManager.svg)](https://www.nuget.org/packages/RtlSdrManager/)
[![License: GPL v3](https://img.shields.io/badge/License-GPLv3-blue.svg)](LICENSE.md)
[![.NET](https://img.shields.io/badge/.NET-10.0-512bd4)](https://dotnet.microsoft.com/download)

**A modern .NET library for managing RTL-SDR devices**

RTL-SDR Manager provides a high-level, type-safe API for controlling RTL2832U-based software-defined radio devices from .NET applications. The library handles device lifecycle, tuner configuration, and sample acquisition with support for synchronous and asynchronous operations, multiple simultaneous devices, and advanced features such as KerberosSDR coherent arrays.

## Features

- **Async/Await Support** — Non-blocking sample reading with concurrent queue buffering for real-time signal processing.

- **Multiple Tuner Support** — Works with E4000, R820T/R828D, FC0012, FC0013, and FC2580 tuner chips.

- **Advanced Configuration** — Gain control, frequency correction (PPM), direct sampling for HF reception, and bias tee power control.

- **KerberosSDR Ready** — Frequency dithering and GPIO control for coherent SDR arrays used in direction finding.

- **Type-Safe Frequency API** — Strongly-typed `Frequency` values with factory methods (`FromHz`, `FromKHz`, `FromMHz`, `FromGHz`) and arithmetic operations.

- **Cross-Platform** — Runs on Windows, Linux, and macOS via platform-specific native interop.

- **High Performance** — Uses `LibraryImport` source-generated P/Invoke for optimal native library calls.

- **Production Ready** — Proper exception handling, `IDisposable` patterns, null safety, and scoped console output suppression.

## Installation

### Via NuGet Package Manager

```bash
# .NET CLI
dotnet add package RtlSdrManager

# Package Manager Console (Visual Studio)
Install-Package RtlSdrManager

# PackageReference (in .csproj)
<PackageReference Include="RtlSdrManager" Version="0.6.1" />
```

### Prerequisites

The `librtlsdr` native library must be installed on the system:

**Windows:**
```powershell
# Using Chocolatey
choco install rtl-sdr

# Or download from: https://github.com/osmocom/rtl-sdr/releases
```

**Linux (Ubuntu/Debian):**
```bash
sudo apt-get install librtlsdr-dev
```

**macOS:**
```bash
brew install librtlsdr
```

## Quick Start

### Basic Usage

```csharp
using RtlSdrManager;
using RtlSdrManager.Modes;

// Get the singleton device manager
var manager = RtlSdrDeviceManager.Instance;

// Check available devices
Console.WriteLine($"Found {manager.CountDevices} RTL-SDR device(s)");

// Open the first device with a friendly name
manager.OpenManagedDevice(0, "my-rtl-sdr");

// Configure the device
var device = manager["my-rtl-sdr"];
device.CenterFrequency = Frequency.FromMHz(1090);  // ADS-B frequency
device.SampleRate = Frequency.FromMHz(2);
device.TunerGainMode = TunerGainModes.AGC;
device.AGCMode = AGCModes.Enabled;
device.ResetDeviceBuffer();

Console.WriteLine($"Tuner: {device.TunerType}");
Console.WriteLine($"Center Frequency: {device.CenterFrequency.MHz} MHz");
```

### Console Output Suppression

By default, `librtlsdr` diagnostic messages (such as `"Found Rafael Micro R820T tuner"` and `"[R82XX] PLL not locked!"`) are shown on stdout/stderr. The library provides a global static property to suppress these messages during device operations:

```csharp
// Suppress librtlsdr diagnostic output globally
RtlSdrDeviceManager.SuppressLibraryConsoleOutput = true;

// Device operations are now silent
manager.OpenManagedDevice(0, "my-rtl-sdr");
var device = manager["my-rtl-sdr"];
device.SampleRate = Frequency.FromMHz(2.4);

// Re-enable output
RtlSdrDeviceManager.SuppressLibraryConsoleOutput = false;
device.CenterFrequency = Frequency.FromMHz(1090);  // Shows librtlsdr messages
```

Suppression is scoped to individual device operations using reference-counted file descriptor redirection. Stdout and stderr are redirected to `/dev/null` (Unix/macOS) or `NUL` (Windows) only for the duration of each native call, then restored. This prevents interference with console applications and avoids file descriptor corruption when multiple devices operate concurrently.

For detailed documentation, see [Console Output Suppression](docs/CONSOLE_OUTPUT_SUPPRESSION.md).

### Synchronous Sample Reading

```csharp
// Read samples synchronously (blocking)
var samples = device.ReadSamples(256 * 1024);

foreach (var sample in samples)
{
    // Process I/Q samples
    Console.WriteLine($"I: {sample.I}, Q: {sample.Q}");
}
```

### Asynchronous Sample Reading

```csharp
// Configure async buffer
device.MaxAsyncBufferSize = 512 * 1024;
device.DropSamplesOnFullBuffer = true;

// Start async reading in background
device.StartReadSamplesAsync();

// Option 1: Event-based (recommended for real-time processing)
device.SamplesAvailable += (sender, args) =>
{
    var samples = device.GetSamplesFromAsyncBuffer(args.SampleCount);
    // Process samples in real-time
};

// Option 2: Manual polling (for custom processing logic)
while (running)
{
    if (device.AsyncBuffer.TryDequeue(out var data))
    {
        // Process data
        Console.WriteLine($"Received {data.Samples.Length} samples");
    }
    else
    {
        await Task.Delay(100);
    }
}

// Stop reading when done
device.StopReadSamplesAsync();

// Clean up
manager.CloseManagedDevice("my-rtl-sdr");
```

### Raw Buffer Mode (Zero-Copy)

For high-throughput applications, raw buffer mode eliminates per-sample object allocation by
delivering raw byte buffers directly from the native callback via `ArrayPool<byte>`:

```csharp
// Enable raw buffer mode before starting async reading
device.UseRawBufferMode = true;
device.MaxAsyncBufferSize = 512 * 1024;
device.DropSamplesOnFullBuffer = true;

device.StartReadSamplesAsync(requestedSamples: 131072);

device.SamplesAvailable += (sender, args) =>
{
    var buffer = device.GetRawSamplesFromAsyncBuffer();
    if (buffer == null) return;

    try
    {
        // Access raw interleaved I/Q bytes: [I0, Q0, I1, Q1, ...]
        ReadOnlySpan<byte> raw = buffer.Data.AsSpan(0, buffer.ByteLength);

        for (int i = 0; i < buffer.ByteLength; i += 2)
        {
            byte iSample = raw[i];
            byte qSample = raw[i + 1];
            // Process sample pair...
        }
    }
    finally
    {
        buffer.Return(); // Return pooled buffer — must be called exactly once
    }
};

// Stop reading when done
device.StopReadSamplesAsync();
```

### Manual Gain Control

```csharp
// Switch to manual gain mode
device.TunerGainMode = TunerGainModes.Manual;

// Get supported gain values
var gains = device.SupportedTunerGains;
Console.WriteLine($"Supported gains: {string.Join(", ", gains)} dB");

// Set specific gain
device.TunerGain = 42.1; // dB

// Or use convenience methods
device.SetMaximumTunerGain();
device.SetMinimumTunerGain();
```

### Frequency Operations

```csharp
// Create frequencies with different units
var freq1 = Frequency.FromHz(1090_000_000);
var freq2 = Frequency.FromKHz(1090_000);
var freq3 = Frequency.FromMHz(1090);
var freq4 = Frequency.FromGHz(1.09);

// Convert between units
Console.WriteLine($"{freq1.Hz} Hz");
Console.WriteLine($"{freq1.KHz} KHz");
Console.WriteLine($"{freq1.MHz} MHz");
Console.WriteLine($"{freq1.GHz} GHz");

// Arithmetic operations
var shifted = freq1 + Frequency.FromKHz(100); // Add 100 KHz offset
var doubled = freq1 * 2;

// Comparison
if (freq1 > Frequency.FromMHz(100))
{
    Console.WriteLine("Above 100 MHz");
}
```

### Advanced Features

#### Bias Tee (for powering external LNAs)

```csharp
// Enable bias tee on GPIO 0 (most common)
device.SetBiasTee(BiasTeeModes.Enabled);

// For R820T tuners, specify GPIO pin
device.SetBiasTeeGPIO(gpio: 0, BiasTeeModes.Enabled);
```

#### Direct Sampling (HF reception)

```csharp
// Enable direct sampling on I-ADC
device.DirectSamplingMode = DirectSamplingModes.I_ADC;

// Or on Q-ADC
device.DirectSamplingMode = DirectSamplingModes.Q_ADC;

// Disable
device.DirectSamplingMode = DirectSamplingModes.Disabled;
```

#### Frequency Correction (PPM)

```csharp
// Set frequency correction in PPM
device.FrequencyCorrection = 10; // +10 PPM
```

#### KerberosSDR Support

```csharp
// Enable KerberosSDR mode (required for these features)
device.KerberosSDRMode = KerberosSDRModes.Enabled;

// Enable frequency dithering (for R820T only)
device.FrequencyDitheringMode = FrequencyDitheringModes.Enabled;

// Control GPIO pins directly
device.SetGPIO(gpio: 1, GPIOModes.Enabled);
```

## Documentation

Detailed guides for specific use cases:

- [Basic Setup](docs/BASIC_SETUP.md) — Device initialization and first sample acquisition
- [Device Management](docs/DEVICE_MANAGEMENT.md) — Managing multiple RTL-SDR devices simultaneously
- [Manual Gain Control](docs/MANUAL_GAIN_CONTROL.md) — Configuring tuner gain settings
- [Direct Sampling](docs/DIRECT_SAMPLING.md) — Using direct sampling modes for HF reception
- [Frequency Correction](docs/FREQUENCY_CORRECTION.md) — PPM calibration and frequency correction
- [Bias Tee](docs/BIAS_TEE.md) — Powering external LNAs via bias tee
- [KerberosSDR](docs/KERBEROS_SDR.md) — Coherent SDR array features
- [Console Output Suppression](docs/CONSOLE_OUTPUT_SUPPRESSION.md) — Controlling native library diagnostic output

### Sample Applications

The [`samples/`](samples/) directory contains complete working examples:

- **Demo1** — Event-based async sample reading
- **Demo2** — Manual polling from async buffer
- **Demo3** — Synchronous sample reading
- **Demo4** — Device information and configuration
- **Demo5** — Raw buffer mode for zero-copy sample processing

## Building from Source

### Requirements

- .NET 10.0 SDK or later
- librtlsdr native library

### Build Commands

```bash
# Clone the repository
git clone https://github.com/nandortoth/rtlsdr-manager.git
cd rtlsdr-manager

# Build the entire solution
dotnet build

# Run tests (if available)
dotnet test

# Create NuGet packages
dotnet pack --configuration Release

# Or use the convenience script
./build.sh
```

### Build Output

The build process creates:

- **NuGet packages** — `artifacts/packages/`
- **Library binaries** — `artifacts/binaries/RtlSdrManager/`
- **Sample binaries** — `artifacts/binaries/Samples/`

### Running Samples

```bash
# Using the convenience script
./runsample.sh

# Or manually
dotnet run --project samples/RtlSdrManager.Samples
```

## Architecture

```
rtlsdr-manager/
├── src/
│   └── RtlSdrManager/           # Main library
│       ├── Exceptions/          # Custom exception types
│       ├── Hardware/            # Hardware type definitions
│       ├── Interop/             # P/Invoke wrappers
│       └── Modes/               # Enumeration types
├── samples/
│   └── RtlSdrManager.Samples/   # Example applications
└── docs/                        # Documentation
```

## System Requirements

- **.NET Runtime** — 10.0 or later
- **Operating System** — Windows, Linux, macOS
- **Hardware** — RTL-SDR compatible device (RTL2832U-based)
- **Native Library** — librtlsdr installed on the system

## Supported Devices

This library supports RTL-SDR devices with the following tuners:

| Tuner | Frequency Range | Notes |
|---|---|---|
| Elonics E4000 | 52 -- 1100 MHz, 1250 -- 2200 MHz | No longer manufactured |
| Rafael Micro R820T | 24 -- 1766 MHz | Most common, excellent performance |
| Rafael Micro R828D | 24 -- 1766 MHz | Similar to R820T |
| Fitipower FC0012 | 22 -- 948.6 MHz | Basic performance |
| Fitipower FC0013 | 22 -- 1100 MHz | Basic performance |
| FCI FC2580 | 146 -- 308 MHz, 438 -- 924 MHz | Good performance |

## Contributing

Contributions are welcome. Please read the [Contributing Guide](CONTRIBUTING.md) for development setup, coding standards, and the pull request process. This project follows the [Contributor Covenant Code of Conduct](CODE_OF_CONDUCT.md).

## License

RTL-SDR Manager Library for .NET is free software, released under the [GNU General Public License v3.0 or later](LICENSE.md).

## Links

- **NuGet Package:** https://www.nuget.org/packages/RtlSdrManager/
- **GitHub Repository:** https://github.com/nandortoth/rtlsdr-manager
- **Issue Tracker:** https://github.com/nandortoth/rtlsdr-manager/issues
- **Changelog:** [CHANGELOG.md](CHANGELOG.md)
- **librtlsdr:** https://github.com/osmocom/rtl-sdr

## Acknowledgments

- **[Osmocom rtl-sdr project](https://osmocom.org/projects/rtl-sdr/wiki)** — The native `librtlsdr` library that this project wraps.
- **[KerberosSDR project](https://github.com/rtlsdrblog/rtl-sdr-kerberos)** — Coherent SDR extensions for direction finding and passive radar.

## Contact

- **Author:** Nandor Toth
- **Email:** dev@nandortoth.com
- **Issues:** [github.com/nandortoth/rtlsdr-manager/issues](https://github.com/nandortoth/rtlsdr-manager/issues)
