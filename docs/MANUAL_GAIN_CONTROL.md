# Manual Gain Control

## Objective

Configure an RTL-SDR device with manual gain control for optimal signal reception in specific conditions.

## Scenario

A user needs precise control over the tuner gain to avoid saturation from strong signals or to maximize weak signal reception.

## Prerequisites

- RTL-SDR device connected
- Knowledge of expected signal strength in the target frequency range

## Implementation

```csharp
using RtlSdrManager;
using RtlSdrManager.Modes;

var manager = RtlSdrDeviceManager.Instance;
manager.OpenManagedDevice(0, "my-device");

// Disable automatic gain control
manager["my-device"].TunerGainMode = TunerGainModes.Manual;
manager["my-device"].AGCMode = AGCModes.Disabled;

// Get available gain values for this tuner (values are in dB)
var availableGains = manager["my-device"].SupportedTunerGains;
Console.WriteLine("Available gain values:");
foreach (var gain in availableGains)
{
    Console.WriteLine($"  {gain:F1} dB");
}

// Set a specific gain in dB. The value must be exactly one of SupportedTunerGains,
// so pick from that list rather than hardcoding a value the tuner may not support.
manager["my-device"].TunerGain = availableGains[availableGains.Count / 2];

// Convenience helpers set the extremes of the supported range:
//   manager["my-device"].SetMaximumTunerGain();
//   manager["my-device"].SetMinimumTunerGain();

// Configure other parameters
manager["my-device"].CenterFrequency = Frequency.FromMHz(100);
manager["my-device"].SampleRate = Frequency.FromMHz(2);

// Start receiving. Reading stays active for the gain sweep in the next section.
manager["my-device"].ResetDeviceBuffer();
manager["my-device"].StartReadSamplesAsync();
Console.WriteLine($"Device configured with manual gain: {manager["my-device"].TunerGain} dB");
```

## Testing Different Gain Values

```csharp
// Function to test different gain settings (gain is in dB)
void TestGainSetting(double gainValue)
{
    manager["my-device"].TunerGain = gainValue;
    Thread.Sleep(1000); // Let it stabilize

    // Read some samples and check signal strength
    var data = manager["my-device"].GetSamplesFromAsyncBuffer(4096);
    if (data.Count > 0)
    {
        // Analyze signal strength here
        Console.WriteLine($"Gain {gainValue:F1} dB - Sample count: {data.Count}");
    }
}

// Test each available gain setting
foreach (var gain in availableGains)
{
    TestGainSetting(gain);
}

// Stop reading and release the device once the sweep is complete.
// StopReadSamplesAsync may rethrow a captured async error; see Basic Setup for the
// full try/finally pattern.
manager["my-device"].StopReadSamplesAsync();
manager.CloseManagedDevice("my-device");
```

## Expected Results

- Gain remains fixed at the specified value.
- No automatic adjustments occur.
- Signal quality depends on the chosen gain value.
- Different gain values affect the signal-to-noise ratio.

## Notes

- Gain values are in dB (e.g., 29.6 dB); `SupportedTunerGains` lists the exact values the tuner accepts, and setting an unsupported value throws.
- Too high a gain can cause saturation and distortion.
- Too low a gain reduces sensitivity to weak signals.
- Optimal gain depends on signal strength and interference levels.
- Use AGC initially to find an appropriate manual gain value for your setup.

## See Also

- [Basic Setup](BASIC_SETUP.md) — Device initialization and first sample acquisition
- [Frequency Correction](FREQUENCY_CORRECTION.md) — PPM calibration and frequency correction
- [Main README](../README.md) — Library overview and features
