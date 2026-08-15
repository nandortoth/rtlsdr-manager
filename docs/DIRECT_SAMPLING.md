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

Direct sampling reaches **0 Hz up to half the RTL2832U's crystal frequency**, which is
14.4 MHz on the usual 28.8 MHz crystal. That covers:

- Long Wave: 30 kHz -- 300 kHz
- Medium Wave: 300 kHz -- 3 MHz
- Short Wave: 3 MHz -- 14.4 MHz, including the 160 m, 80 m, 60 m, 40 m, 30 m and 20 m
  amateur bands

Ask the device rather than assuming, since the crystal is adjustable and frequency
correction is applied to it:

```csharp
var device = manager["hf-receiver"];
device.DirectSamplingMode = DirectSamplingModes.QuadratureADCInputEnabled;

// Reports the ADC's range while direct sampling is active, the tuner's otherwise
Console.WriteLine($"Reachable: {string.Join(" and ", device.SupportedFrequencyRanges)}");

// Example: receiving the AM broadcast band
device.CenterFrequency = Frequency.FromKHz(1000); // 1 MHz MW
device.SampleRate = Frequency.FromMHz(2);
```

### Above the limit: aliasing

The ADC samples at the crystal frequency, so 14.4 MHz is its first Nyquist zone. Signals
above it are still receivable, but they fold down rather than being tuned to directly:
subtract the wanted frequency from the crystal frequency.

```csharp
// Receiving 21.2 MHz (15 m band) on a 28.8 MHz crystal: 28.8 - 21.2 = 7.6 MHz
device.CenterFrequency = Frequency.FromMHz(7.6);
```

Asking for 21.2 MHz directly throws `ArgumentOutOfRangeException`. That check matters: the
value is written into a 22-bit register, so without it the frequency would be truncated and
the device would quietly receive something else with no error anywhere.

Expect aliased reception to be weaker, and note that signals from both zones land on top of
each other unless the antenna or an external filter separates them.

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

- Direct sampling bypasses the tuner chip entirely.
- **Enable direct sampling before setting the center frequency.** Switching mode re-applies the current frequency through the path being entered, and a VHF or UHF frequency is meaningless to the ADC. Turning direct sampling on therefore resets the center frequency to 0 Hz whenever the previous one is out of the ADC's reach.
- Turning direct sampling back off re-applies the current frequency to the tuner, which fails if it is an HF frequency only the ADC could reach. The mode still changes; set a frequency the tuner supports afterwards.
- I-ADC and Q-ADC inputs may have different performance characteristics depending on the device.
- Sample rate and gain settings still apply.
- Not all RTL-SDR devices support direct sampling equally well.
- Hardware modifications may improve HF reception (bias tee removal, better filtering).
- Antenna design is critical for HF reception quality.

## See Also

- [Basic Setup](BASIC_SETUP.md) — Device initialization and first sample acquisition
- [Frequency Correction](FREQUENCY_CORRECTION.md) — PPM calibration and frequency correction
- [Main README](../README.md) — Library overview and features
