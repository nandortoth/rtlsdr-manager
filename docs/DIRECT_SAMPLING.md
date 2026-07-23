# Direct Sampling Mode

## Objective

Enable direct sampling mode for receiving HF frequencies (below 30 MHz) without an upconverter.

## Scenario

A user wants to receive shortwave radio, amateur radio, or other HF signals directly using the RTL-SDR's direct sampling capability, bypassing the tuner chip.

## Prerequisites

- RTL-SDR device with direct sampling support
- Appropriate antenna for HF frequencies
- Understanding of I-ADC vs Q-ADC sampling modes

## Implementation

### Using I-ADC Direct Sampling

```csharp
using RtlSdrManager;
using RtlSdrManager.Modes;

var manager = RtlSdrDeviceManager.Instance;
manager.OpenManagedDevice(0, "hf-receiver");

// Enable direct sampling on I-ADC input
manager["hf-receiver"].DirectSamplingMode = DirectSamplingModes.InPhaseADCInputEnabled;

// Configure for HF reception
manager["hf-receiver"].CenterFrequency = Frequency.FromMHz(14.2); // 20m amateur band
manager["hf-receiver"].SampleRate = Frequency.FromMHz(2.4);
manager["hf-receiver"].TunerGainMode = TunerGainModes.AGC;
manager["hf-receiver"].AGCMode = AGCModes.Enabled;

// Start receiving. Reading stays active for the mode changes shown below.
manager["hf-receiver"].ResetDeviceBuffer();
manager["hf-receiver"].StartReadSamplesAsync();
Console.WriteLine("Direct sampling enabled for HF reception");
```

### Using Q-ADC Direct Sampling

```csharp
// Alternative: Use Q-ADC input
manager["hf-receiver"].DirectSamplingMode = DirectSamplingModes.QuadratureADCInputEnabled;

// Q-ADC typically provides better performance on some devices
Console.WriteLine("Using Q-ADC direct sampling");
```

### Switching Between Modes

```csharp
// Disable direct sampling to return to normal tuner mode
manager["hf-receiver"].DirectSamplingMode = DirectSamplingModes.Disabled;
Console.WriteLine("Direct sampling disabled - using normal tuner");

// Now can tune VHF/UHF frequencies normally
manager["hf-receiver"].CenterFrequency = Frequency.FromMHz(145); // 2m band
```

### Frequency Coverage

```csharp
// Direct sampling mode typically covers:
// - Long Wave: 30 kHz - 300 kHz
// - Medium Wave: 300 kHz - 3 MHz
// - Short Wave: 3 MHz - 30 MHz

// Example: Receiving AM broadcast band
manager["hf-receiver"].DirectSamplingMode = DirectSamplingModes.QuadratureADCInputEnabled;
manager["hf-receiver"].CenterFrequency = Frequency.FromKHz(1000); // 1 MHz MW
manager["hf-receiver"].SampleRate = Frequency.FromMHz(2);
```

### Stopping and Cleanup

```csharp
// Stop reading and release the device when finished.
// StopReadSamplesAsync may rethrow a captured async error; see Basic Setup for the
// full try/finally pattern.
manager["hf-receiver"].StopReadSamplesAsync();
manager.CloseManagedDevice("hf-receiver");
```

## Expected Results

- Device receives HF frequencies without an external upconverter.
- Frequency range extends down to DC (0 Hz).
- Tuner chip is bypassed.
- Sampling uses the ADC directly from the antenna input.

## Notes

- **Center frequency validation:** The `CenterFrequency` setter currently validates the value against the tuner's normal operating range (e.g. 24–1766 MHz for the R820T) regardless of the direct sampling mode. As a result, setting an HF frequency below that range (such as the 14.2 MHz and 1 MHz values above) throws an `ArgumentOutOfRangeException` on those tuners. The examples above show the intended workflow; until the range check accounts for direct sampling, choose a center frequency within the tuner's supported range or adjust the library's validation accordingly.
- Direct sampling bypasses the tuner chip entirely.
- I-ADC and Q-ADC inputs may have different performance characteristics depending on the device.
- Sample rate and gain settings still apply.
- Not all RTL-SDR devices support direct sampling equally well.
- Hardware modifications may improve HF reception (bias tee removal, better filtering).
- Antenna design is critical for HF reception quality.

## See Also

- [Basic Setup](BASIC_SETUP.md) — Device initialization and first sample acquisition
- [Frequency Correction](FREQUENCY_CORRECTION.md) — PPM calibration and frequency correction
- [Main README](../README.md) — Library overview and features
