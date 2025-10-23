# Use Case: Manual Gain Control

## Objective
Configure RTL-SDR device with manual gain control for optimal signal reception in specific conditions.

## Scenario
A user needs precise control over the tuner gain to avoid saturation from strong signals or to maximize weak signal reception.

## Prerequisites
- RTL-SDR device connected
- Knowledge of expected signal strength in the target frequency range

## Implementation

```csharp
using RtlSdrManager;

var manager = RtlSdrDeviceManager.Instance;
manager.OpenManagedDevice(0, "my-device");

// Disable automatic gain control
manager["my-device"].TunerGainMode = TunerGainModes.Manual;
manager["my-device"].AGCMode = AGCModes.Disabled;

// Get available gain values for this tuner
var availableGains = manager["my-device"].TunerGains;
Console.WriteLine("Available gain values:");
foreach (var gain in availableGains)
{
    Console.WriteLine($"  {gain} (units of 0.1 dB)");
}

// Set specific gain value (e.g., 296 = 29.6 dB)
manager["my-device"].TunerGain = 296;

// Configure other parameters
manager["my-device"].CenterFrequency = Frequency.FromMHz(100);
manager["my-device"].SampleRate = Frequency.FromMHz(2);

// Start receiving
manager["my-device"].ResetDeviceBuffer();
manager["my-device"].StartReadSamplesAsync();

Console.WriteLine($"Device configured with manual gain: {manager["my-device"].TunerGain * 0.1} dB");
```

## Testing Different Gain Values

```csharp
// Function to test different gain settings
void TestGainSetting(int gainValue)
{
    manager["my-device"].TunerGain = gainValue;
    Thread.Sleep(1000); // Let it stabilize

    // Read some samples and check signal strength
    if (manager["my-device"].AsyncBuffer.TryDequeue(out var data))
    {
        // Analyze signal strength here
        Console.WriteLine($"Gain {gainValue * 0.1} dB - Sample count: {data.Length}");
    }
}

// Test each available gain setting
foreach (var gain in availableGains)
{
    TestGainSetting(gain);
}
```

## Expected Results
- Gain remains fixed at the specified value
- No automatic adjustments occur
- Signal quality depends on chosen gain value
- Different gain values affect signal-to-noise ratio

## Notes
- Gain values are in tenths of dB (e.g., 296 = 29.6 dB)
- Too high gain can cause saturation and distortion
- Too low gain reduces sensitivity to weak signals
- Optimal gain depends on signal strength and interference levels
- Use AGC initially to find appropriate manual gain values
