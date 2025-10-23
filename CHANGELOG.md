# Changelog

All notable changes to this project will be documented in this file.
The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.5.0] - 2025-10-23

### Added
- Windows support to native library resolver
- IComparable support to `Frequency` type
- Improved frequency arithmetic with overflow protection
- `build.sh` script for easy building and NuGet package creation
- `runsample.sh` script for running sample applications
- Modern .NET project structure with `src/` and `samples/` folders
- Source Link support for debugging NuGet packages
- Comprehensive XML documentation
- `Frequency` type with strongly-typed unit conversions (Hz, KHz, MHz, GHz)
- `DeviceInfo` as modern record type
- `CrystalFrequency` type for RTL2832 and tuner crystal settings
- Comprehensive `.editorconfig` with 60+ code quality rules
- `.gitattributes` for consistent cross-platform line endings
- [Console output suppression](docs/CONSOLE_OUTPUT_SUPPRESSION.md) for librtlsdr diagnostic messages
- Performance analyzers (CA1827, CA1829, CA1841, CA1851)
- Security analyzers for P/Invoke (CA2101, CA3075, CA5350, CA5351)
- Nullable reference type warnings (CS8600-CS8604, CS8618, CS8625)
- Standard exception constructors for all custom exception types
- Input validation with proper exception types (`ArgumentNullException`, `ArgumentException`)
- Improved [README.md](README.md) with comprehensive documentation and examples
- Cross-platform build support (LF line endings)

### Changed
- **BREAKING**: Migrated from .NET Core 3.1 to .NET 9.0
- **BREAKING**: Namespace reorganization (Modes/, Hardware/, Interop/)
- **BREAKING**: Project restructured into `src/` and `samples/` directories
- Replaced legacy `DllImport` with source-generated `LibraryImport` for better performance
- Split `RtlSdrLibraryWrapper` into `LibRtlSdr` and `LibResolver` for better separation
- Modernized value types (`DeviceInfo`, `Frequency`, `CrystalFrequency`)
- Improved build system with artifact generation
- Exception handling: `IndexOutOfRangeException` replaced with appropriate types
- `OpenManagedDevice`: Now validates device index and friendly name
- `CloseManagedDevice`: Added input validation for friendly name
- `CloseAllManagedDevice`: Changed exception type to `InvalidOperationException`
- Indexer `[string friendlyName]`: Added input validation and proper exception types
- `RtlSdrManagedDevice.Dispose()`: Implemented proper dispose pattern with `Dispose(bool disposing)`
- EditorConfig: Changed line endings from CRLF to LF for cross-platform compatibility
- EditorConfig: Adjusted brace enforcement from warning to suggestion

### Fixed
- Dispose pattern now properly separates managed and unmanaged resource cleanup
- Finalizer no longer accesses potentially disposed managed resources
- `CancellationTokenSource` is now properly disposed in async samples
- Device opening now provides clear error messages for invalid indices
- Demo2: Fixed `CancellationTokenSource` disposal using `using var` declaration

### Removed
- .NET Core 3.1 support (now requires .NET 9.0+)
- Old NuGet package creation scripts (replaced by `build.sh`)
- Legacy Visual Studio project files

### Performance
- LibraryImport provides better P/Invoke performance
- AOT compilation compatibility
- Compile-time safety improvements

## [0.2.1] - 2020-01-10

### Added
- KerberosSDR support for coherent SDR arrays
  - Frequency dithering control for R820T tuners
  - Direct GPIO control for synchronization
- Bias Tee control methods
  - `SetBiasTee()` for GPIO 0
  - `SetBiasTeeGPIO()` for specific GPIO pins on R820T
- `KerberosSDRModes` enumeration
- `GPIOModes` enumeration for GPIO control
- `BiasTeeModes` enumeration

### Changed
- Extended `RtlSdrManagedDevice` with KerberosSDR-specific features
- Improved documentation for advanced features

### Fixed
- Minor documentation issues and typos

## [0.2.0] - 2018-06-10

### Added
- Singleton pattern for `RtlSdrDeviceManager`
- Thread-safe device manager instance

### Fixed
- Critical bug in `DeviceInfo` handling that could cause incorrect device identification
- Improved device enumeration reliability

### Changed
- `RtlSdrDeviceManager` now uses lazy singleton initialization
- Better memory management for device instances

## [0.1.3] - 2018-06-09

### Added
- First public release on NuGet.org
- NuGet package metadata and icon
- Package publishing workflow

### Changed
- Improved package description and tags
- Added proper licensing information to NuGet package

## [0.1.2] - 2018-06-03

### Added
- Crystal frequency control for RTL2832 and tuner IC
  - `CrystalFrequency` property with get/set support
  - Validation for crystal frequency ranges (max 28.8 MHz)
- Tuner bandwidth selection
  - Automatic bandwidth selection mode
  - Manual bandwidth control
  - `TunerBandwidthSelectionModes` enumeration
- Direct sampling support for HF reception
  - I-ADC direct sampling mode
  - Q-ADC direct sampling mode
  - `DirectSamplingModes` enumeration
- Offset tuning mode for zero-IF tuners
  - Improved DC offset handling
  - `OffsetTuningModes` enumeration

### Changed
- Enhanced `RtlSdrManagedDevice` with advanced tuner controls
- Improved frequency handling and validation
- Better support for HF reception scenarios

## [0.1.1] - 2018-05-12

### Added
- Initial public release of RTL-SDR Manager
- Core device management functionality
  - Device enumeration and information retrieval
  - Device opening and closing
  - Multiple device support with friendly names
- Basic device configuration
  - Center frequency control with validation
  - Sample rate configuration
  - Gain control (AGC and manual modes)
  - Frequency correction (PPM)
- Sample reading capabilities
  - Synchronous sample reading
  - Asynchronous sample reading with event support
  - Configurable buffer management
- Tuner support
  - Elonics E4000
  - Rafael Micro R820T/R828D
  - Fitipower FC0012/FC0013
  - FCI FC2580
- Exception handling
  - `RtlSdrDeviceException`
  - `RtlSdrLibraryExecutionException`
  - `RtlSdrManagedDeviceException`
- Comprehensive demo applications
  - Event-based async reading (Demo1)
  - Manual buffer polling (Demo2)
  - Synchronous reading (Demo3)
  - Device information display (Demo4)

### Dependencies
- .NET Core 3.1
- librtlsdr native library

## [0.1.0] - 2018-05-01

### Added
- Initial development version
- Basic P/Invoke wrappers for librtlsdr
- Core architecture design
- Project structure and build system

---

## Version History Summary

| Version | Date       | Key Changes |
|---------|------------|-------------|
| **0.5.0** | 2025-10-23 | .NET 9.0 migration, modern architecture, Source Link |
| **0.2.1** | 2020-01-10 | KerberosSDR support, Bias Tee control |
| **0.2.0** | 2018-06-10 | Singleton pattern, bug fixes |
| **0.1.3** | 2018-06-09 | First NuGet release |
| **0.1.2** | 2018-06-03 | Crystal frequency, direct sampling, offset tuning |
| **0.1.1** | 2018-05-12 | Initial public release |
| **0.1.0** | 2018-05-01 | Development version |

---

## Upgrade Notes

### Upgrading to 0.5.0 from 0.2.x

**Breaking Changes:**

1. **Framework Requirement**
   ```xml
   <!-- Old -->
   <TargetFramework>netcoreapp3.1</TargetFramework>

   <!-- New -->
   <TargetFramework>net9.0</TargetFramework>
   ```

2. **Namespace Changes**
   ```csharp
   // Old
   using RtlSdrManager.Types;

   // New
   using RtlSdrManager.Modes;  // For enumerations
   using RtlSdrManager.Hardware;  // For hardware types
   ```

3. **Project Structure**
   - Source files moved from `RtlSdrManager/` to `src/RtlSdrManager/`
   - Samples moved from `RtlSdrManagerDemo/` to `samples/RtlSdrManager.Samples/`

4. **Type Changes**
   - `Frequency` is now an immutable record struct
   - Use factory methods: `Frequency.FromMHz()`, `FromKHz()`, etc.

**Migration Example:**

```csharp
// Old (0.2.x)
using RtlSdrManager.Types;

var freq = new Frequency(1090000000);  // Hz
device.CenterFrequency = freq;

// New (0.5.0)
using RtlSdrManager;

var freq = Frequency.FromMHz(1090);  // Clearer intent
device.CenterFrequency = freq;

// Can also use arithmetic
var shifted = freq + Frequency.FromKHz(100);
```

**New Feature - Console Output Suppression:**

By default, librtlsdr diagnostic messages are now suppressed. You can control this per-device:

```csharp
// Suppress by default (enabled by default in 0.5.0)
manager.OpenManagedDevice(0, "my-device");
var device = manager["my-device"];

// Toggle suppression at runtime for specific device
device.SuppressLibraryConsoleOutput = false;  // Show messages
device.SampleRate = Frequency.FromMHz(2);      // Will show librtlsdr output

// Or set global default for new devices
RtlSdrDeviceManager.SuppressLibraryConsoleOutput = false;
```

### Upgrading to 0.2.0 from 0.1.x

- No breaking changes, fully backward compatible
- Recommended to use singleton pattern: `RtlSdrDeviceManager.Instance`
- Legacy instantiation still works but is deprecated

---

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md) for contribution guidelines.

## License

This project is licensed under the GNU General Public License v3.0 or later.
See [LICENSE.md](LICENSE.md) for details.

---

[0.5.0]: https://github.com/nandortoth/rtlsdr-manager/releases/tag/v0.5.0
[0.2.1]: https://github.com/nandortoth/rtlsdr-manager/releases/tag/v0.2.1
[0.2.0]: https://github.com/nandortoth/rtlsdr-manager/releases/tag/v0.2.0
[0.1.3]: https://github.com/nandortoth/rtlsdr-manager/releases/tag/v0.1.3
[0.1.2]: https://github.com/nandortoth/rtlsdr-manager/releases/tag/v0.1.2
[0.1.1]: https://github.com/nandortoth/rtlsdr-manager/releases/tag/v0.1.1
[0.1.0]: https://github.com/nandortoth/rtlsdr-manager/releases/tag/v0.1.0
