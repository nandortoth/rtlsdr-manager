# Frequency Correction and Crystal Tuning

## Objective

Calibrate and correct frequency offset caused by crystal oscillator inaccuracies in RTL-SDR devices.

## Scenario

A user notices that received frequencies are slightly off from expected values and needs to calibrate the device's frequency accuracy using a known reference signal.

## Prerequisites

- RTL-SDR device
- Known reference signal (FM station, GSM tower, or calibration signal)
- Method to measure frequency offset (spectrum analyzer, SDR software)

## Implementation

### Setting Frequency Correction (PPM)

```csharp
using RtlSdrManager;
using RtlSdrManager.Modes;

var manager = RtlSdrDeviceManager.Instance;
manager.OpenManagedDevice(0, "my-device");

// Set frequency correction in parts per million (PPM)
// Positive values increase frequency, negative values decrease it
manager["my-device"].FrequencyCorrection = 52; // Example: +52 PPM

Console.WriteLine($"Frequency correction set to {manager["my-device"].FrequencyCorrection} PPM");

// Configure device
manager["my-device"].CenterFrequency = Frequency.FromMHz(100);
manager["my-device"].SampleRate = Frequency.FromMHz(2);
```

### Calibration Process Using Known Signal

```csharp
void CalibrateDevice(string deviceName, double knownFrequencyMHz)
{
    var device = manager[deviceName];

    Console.WriteLine($"Calibrating device using signal at {knownFrequencyMHz} MHz");

    // Start with no correction
    device.FrequencyCorrection = 0;
    device.CenterFrequency = Frequency.FromMHz(knownFrequencyMHz);
    device.SampleRate = Frequency.FromMHz(2);
    device.TunerGainMode = TunerGainModes.AGC;
    device.AGCMode = AGCModes.Enabled;

    device.ResetDeviceBuffer();
    device.StartReadSamplesAsync();

    // Reading stays active so you can observe the signal. Call
    // device.StopReadSamplesAsync() and manager.CloseManagedDevice(deviceName)
    // once you have finished measuring.
    Console.WriteLine("Listen to the signal and measure the frequency offset");
    Console.WriteLine("Then calculate PPM = (measured_freq - actual_freq) / actual_freq * 1,000,000");
    Console.WriteLine("\nExample: If 100.0 MHz signal appears at 100.005 MHz:");
    Console.WriteLine("PPM = (100.005 - 100.0) / 100.0 * 1,000,000 = 50 PPM");
}

// Example: Calibrate using FM radio station
CalibrateDevice("my-device", 100.0); // Known FM station at 100.0 MHz
```

### Applying Calculated PPM Correction

```csharp
void ApplyCalibration(string deviceName, double measuredFreqMHz, double actualFreqMHz)
{
    var device = manager[deviceName];

    // Calculate PPM offset
    double ppmOffset = ((measuredFreqMHz - actualFreqMHz) / actualFreqMHz) * 1_000_000;

    // Round to nearest integer
    int ppmCorrection = (int)Math.Round(ppmOffset);

    // Apply correction
    device.FrequencyCorrection = ppmCorrection;

    Console.WriteLine($"Measured: {measuredFreqMHz} MHz");
    Console.WriteLine($"Actual: {actualFreqMHz} MHz");
    Console.WriteLine($"Calculated PPM: {ppmOffset:F2}");
    Console.WriteLine($"Applied correction: {ppmCorrection} PPM");
}

// Example: If 100.0 MHz FM station appears at 100.005 MHz
ApplyCalibration("my-device", 100.005, 100.0);
```

### Testing Calibration Accuracy

```csharp
void VerifyCalibration(string deviceName, params double[] knownFrequencies)
{
    var device = manager[deviceName];

    Console.WriteLine($"\nVerifying calibration with {device.FrequencyCorrection} PPM correction:");

    foreach (var freq in knownFrequencies)
    {
        device.CenterFrequency = Frequency.FromMHz(freq);
        Thread.Sleep(500); // Let it stabilize

        Console.WriteLine($"  Tuned to: {freq} MHz - Check if signal is centered");
    }
}

// Test with multiple known frequencies
VerifyCalibration("my-device", 100.0, 145.0, 433.0, 1090.0);
```

### Temperature Compensation

Crystal drift is temperature dependent, so the ideal PPM value changes as the device warms up or the ambient temperature shifts. The library exposes this through the single `FrequencyCorrection` property — reapply an appropriate value as conditions change. Maintaining a temperature-to-PPM table and choosing the closest entry is straightforward application logic on top of that property.

### Crystal Frequency Information

```csharp
// Get crystal frequencies (RTL2832 and tuner IC).
// CrystalFrequency exposes both clocks as a single record.
var device = manager["my-device"];
var crystal = device.CrystalFrequency;

Console.WriteLine($"RTL2832 Crystal Frequency: {crystal.Rtl2832Frequency.Hz} Hz");
Console.WriteLine($"Tuner Crystal Frequency: {crystal.TunerFrequency.Hz} Hz");

// Note: These are nominal values and do not reflect actual accuracy
```

## Expected Results

- Received signals appear at correct frequencies.
- Frequency accuracy improves significantly after calibration.
- Calibration remains stable over time (temperature dependent).
- PPM correction applies to all tuned frequencies.

## Calibration Tips

1. **Use multiple reference frequencies** — Calibrate using signals across the spectrum for best results.
2. **Account for temperature** — PPM offset varies with ambient temperature.
3. **Common PPM ranges** — Most RTL-SDR devices have -100 to +100 PPM offset.
4. **Stable reference signals** — FM radio stations, GSM base stations, ADS-B signals (1090 MHz), and amateur radio beacons work well.

## Notes

- PPM correction is device-specific and should be saved per device.
- Temperature changes affect crystal frequency.
- Inexpensive RTL-SDR devices may have larger PPM offsets.
- Calibration improves with device warm-up time.
- Narrow-band applications are more sensitive to frequency errors than wideband ones.
- The PPM value is applied as: `actual_freq = tuned_freq * (1 + PPM / 1,000,000)`.

## See Also

- [Manual Gain Control](MANUAL_GAIN_CONTROL.md) — Configuring tuner gain settings
- [Direct Sampling](DIRECT_SAMPLING.md) — Using direct sampling modes for HF reception
- [Main README](../README.md) — Library overview and features
