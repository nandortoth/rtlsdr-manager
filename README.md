# RTL-SDR Manager Library for .NET

[![NuGet Version](https://img.shields.io/nuget/v/RtlSdrManager.svg)](https://www.nuget.org/packages/RtlSdrManager/)
[![NuGet Downloads](https://img.shields.io/nuget/dt/RtlSdrManager.svg)](https://www.nuget.org/packages/RtlSdrManager/)
[![License](https://img.shields.io/badge/license-GPL--3.0-blue.svg)](LICENSE.md)
[![.NET Version](https://img.shields.io/badge/.NET-9.0-purple.svg)](https://dotnet.microsoft.com/download)

A modern, high-performance .NET library for managing RTL-SDR devices with support for async operations, multiple tuner types, and advanced features like KerberosSDR.

## ✨ Features

- 🚀 **Async/Await Support** - Non-blocking sample reading with concurrent queue buffering
- 🎛️ **Multiple Tuner Support** - E4000, R820T/R828D, FC0012, FC0013, FC2580
- 🔧 **Advanced Configuration** - Gain control, frequency correction, direct sampling, bias tee
- 📡 **KerberosSDR Ready** - Frequency dithering and GPIO control for coherent SDR arrays
- 🔒 **Type-Safe API** - Strongly-typed frequency values with unit conversions
- 💾 **Cross-Platform** - Works on Windows, Linux, and macOS
- ⚡ **High Performance** - LibraryImport P/Invoke for optimal native interop
- 🛡️ **Production Ready** - Proper exception handling, disposal patterns, and null safety
- 🔇 **Console Output Control** - Suppress or capture native library diagnostic messages

## 📦 Installation

### Via NuGet Package Manager

```bash
# .NET CLI
dotnet add package RtlSdrManager

# Package Manager Console (Visual Studio)
Install-Package RtlSdrManager

# PackageReference (in .csproj)
<PackageReference Include="RtlSdrManager" Version="0.5.0" />
```

### Prerequisites

You must have the `librtlsdr` native library installed on your system:

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

## 🚀 Quick Start

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

By default, the library suppresses all console output from `librtlsdr` (like "Found Rafael Micro R820T tuner" and "[R82XX] PLL not locked!") by redirecting native stdout/stderr file descriptors to `/dev/null` (Unix/macOS) or `NUL` (Windows). You can control this behavior globally or per-device:

```csharp
// Global default for new devices (default is true)
RtlSdrDeviceManager.SuppressLibraryConsoleOutput = false;

// Open a device - it inherits the global setting
manager.OpenManagedDevice(0, "my-rtl-sdr");
var device = manager["my-rtl-sdr"];

// Control suppression per-device at runtime
device.SuppressLibraryConsoleOutput = false;  // Show messages for this device
device.SampleRate = Frequency.FromMHz(2);      // Will show librtlsdr output

device.SuppressLibraryConsoleOutput = true;   // Hide messages again
device.SampleRate = Frequency.FromMHz(2.4);    // Silent
```

**Note:** Each device has its own `SuppressLibraryConsoleOutput` property that is initialized from the global setting when opened, but can be changed independently. The suppression works at the native file descriptor level to catch all output from the native library.

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

## 📚 Documentation

For more detailed information and advanced usage scenarios:

- [**Basic Setup**](docs/BASIC_SETUP.md) - Getting started with device initialization
- [**Device Management**](docs/DEVICE_MANAGEMENT.md) - Managing multiple RTL-SDR devices
- [**Manual Gain Control**](docs/MANUAL_GAIN_CONTROL.md) - Configuring tuner gain settings
- [**Direct Sampling**](docs/DIRECT_SAMPLING.md) - Using direct sampling modes for HF
- [**Frequency Correction**](docs/FREQUENCY_CORRECTION.md) - PPM frequency correction
- [**Bias Tee**](docs/BIAS_TEE.md) - Enabling bias tee for powering external LNAs
- [**KerberosSDR**](docs/KERBEROS_SDR.md) - Advanced features for KerberosSDR arrays

### Sample Applications

Check out the [`samples/`](samples/) directory for complete working examples:

- **Demo1** - Event-based async sample reading
- **Demo2** - Manual polling from async buffer
- **Demo3** - Synchronous sample reading
- **Demo4** - Device information and configuration

## 🔧 Building from Source

### Requirements

- .NET 9.0 SDK or later
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
- **NuGet packages** → `artifacts/packages/`
- **Library binaries** → `artifacts/binaries/RtlSdrManager/`
- **Sample binaries** → `artifacts/binaries/Samples/`

### Running Samples

```bash
# Using the convenience script
./runsample.sh

# Or manually
dotnet run --project samples/RtlSdrManager.Samples
```

## 🏗️ Architecture

```
RtlSdrManager/
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

## 🤝 Contributing

Contributions are welcome! Please feel free to submit issues, fork the repository, and create pull requests.

1. Fork the repository
2. Create your feature branch (`git checkout -b feature/amazing-feature`)
3. Commit your changes (`git commit -m 'Add amazing feature'`)
4. Push to the branch (`git push origin feature/amazing-feature`)
5. Open a Pull Request

Please ensure your code:
- Follows the EditorConfig style guidelines
- Builds without warnings
- Includes XML documentation for public APIs
- Includes unit tests for new features (when applicable)

## 📋 System Requirements

- **.NET Runtime:** 9.0 or later
- **Operating System:** Windows, Linux, macOS
- **Hardware:** RTL-SDR compatible device (RTL2832U-based)
- **Native Library:** librtlsdr installed on the system

## 📝 Supported Devices

This library supports RTL-SDR devices with the following tuners:

| Tuner | Frequency Range | Notes |
|-------|----------------|-------|
| **Elonics E4000** | 52-1100 MHz, 1250-2200 MHz | No longer manufactured |
| **Rafael Micro R820T** | 24-1766 MHz | Most common, excellent performance |
| **Rafael Micro R828D** | 24-1766 MHz | Similar to R820T |
| **Fitipower FC0012** | 22-948.6 MHz | Basic performance |
| **Fitipower FC0013** | 22-1100 MHz | Basic performance |
| **FCI FC2580** | 146-308 MHz, 438-924 MHz | Good performance |

## 📜 License

This project is licensed under the **GNU General Public License v3.0 or later** - see the [LICENSE.md](LICENSE.md) file for details.

```
RTL-SDR Manager Library for .NET
Copyright (C) 2018-2025 Nandor Toth

This program is free software: you can redistribute it and/or modify
it under the terms of the GNU General Public License as published by
the Free Software Foundation, either version 3 of the License, or
(at your option) any later version.
```

## 🔗 Links

- **NuGet Package:** https://www.nuget.org/packages/RtlSdrManager/
- **GitHub Repository:** https://github.com/nandortoth/rtlsdr-manager
- **Issue Tracker:** https://github.com/nandortoth/rtlsdr-manager/issues
- **Changelog:** [CHANGELOG.md](CHANGELOG.md)
- **librtlsdr:** https://github.com/osmocom/rtl-sdr

## 🙏 Acknowledgments

- [Osmocom rtl-sdr project](https://osmocom.org/projects/rtl-sdr/wiki) for the excellent native library
- [KerberosSDR project](https://github.com/rtlsdrblog/rtl-sdr-kerberos) for coherent SDR extensions
- All contributors and users of this library

## 📊 Project Status

- ✅ **Stable** - Production ready
- ✅ **Actively Maintained** - Regular updates and bug fixes
- ✅ **.NET 9.0** - Modern .NET with latest features
- ✅ **Cross-Platform** - Windows, Linux, macOS support

---

**Made with ❤️ by [Nandor Toth](https://github.com/nandortoth)**
