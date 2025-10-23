# Use Case: Bias Tee Control

## Objective
Enable and control the bias tee to power active antennas or LNAs directly from the RTL-SDR device.

## Scenario
A user has an active antenna or LNA that requires DC power and wants to supply it through the coaxial cable using the RTL-SDR's bias tee feature.

## Prerequisites
- RTL-SDR device with bias tee support (RTL-SDR Blog V3, etc.)
- Active antenna or LNA that accepts bias tee power
- Understanding of voltage/current requirements

## Implementation

### Enabling Bias Tee

```csharp
using RtlSdrManager;

var manager = RtlSdrDeviceManager.Instance;
manager.OpenManagedDevice(0, "my-device");

// Enable bias tee to power active antenna
manager["my-device"].BiasTeeMode = BiasTeeModes.Enabled;

Console.WriteLine("Bias tee enabled - antenna is now powered");

// Configure device normally
manager["my-device"].CenterFrequency = Frequency.FromMHz(1090);
manager["my-device"].SampleRate = Frequency.FromMHz(2);
manager["my-device"].TunerGainMode = TunerGainModes.AGC;
manager["my-device"].AGCMode = AGCModes.Enabled;

// Start receiving
manager["my-device"].ResetDeviceBuffer();
manager["my-device"].StartReadSamplesAsync();
```

### Disabling Bias Tee

```csharp
// Disable bias tee when done or when changing antennas
manager["my-device"].BiasTeeMode = BiasTeeModes.Disabled;

Console.WriteLine("Bias tee disabled - antenna power removed");
```

### Safe Bias Tee Usage Pattern

```csharp
void UseBiasTee(Action receiveAction)
{
    var device = manager["my-device"];

    try
    {
        // Enable bias tee
        device.BiasTeeMode = BiasTeeModes.Enabled;
        Console.WriteLine("Bias tee ON");

        // Wait for LNA to stabilize
        Thread.Sleep(500);

        // Perform reception
        receiveAction();
    }
    finally
    {
        // Always disable bias tee when done
        device.BiasTeeMode = BiasTeeModes.Disabled;
        Console.WriteLine("Bias tee OFF");
    }
}

// Usage
UseBiasTee(() =>
{
    // Your reception code here
    device.StartReadSamplesAsync();
    Thread.Sleep(10000); // Receive for 10 seconds
    device.StopReadSamplesAsync();
});
```

### Application-Specific Configuration

```csharp
// For ADS-B reception with powered LNA
void ConfigureADSBWithLNA()
{
    var device = manager["adsb-device"];

    // Enable bias tee for LNA
    device.BiasTeeMode = BiasTeeModes.Enabled;

    // Reduce gain since LNA provides amplification
    device.TunerGainMode = TunerGainModes.Manual;
    device.TunerGain = 150; // 15.0 dB (lower than without LNA)

    device.CenterFrequency = Frequency.FromMHz(1090);
    device.SampleRate = Frequency.FromMHz(2);

    Console.WriteLine("ADS-B receiver configured with powered LNA");
}

// For satellite reception with powered antenna
void ConfigureSatelliteReception()
{
    var device = manager["sat-device"];

    // Enable bias tee for active antenna
    device.BiasTeeMode = BiasTeeModes.Enabled;

    device.CenterFrequency = Frequency.FromMHz(137.5); // NOAA satellites
    device.SampleRate = Frequency.FromMHz(2.4);
    device.TunerGainMode = TunerGainModes.AGC;
    device.AGCMode = AGCModes.Enabled;

    Console.WriteLine("Satellite receiver configured with powered antenna");
}
```

## Expected Results
- Bias tee supplies DC power through the coaxial cable
- Active antenna or LNA receives power and operates
- Signal quality improves due to active amplification
- Gain settings may need adjustment due to LNA gain

## Safety Warnings

⚠️ **IMPORTANT SAFETY INFORMATION:**

- **NEVER** enable bias tee with passive antennas or unamplified connections
- **NEVER** enable bias tee when the antenna port is connected to another receiver
- **ALWAYS** check your equipment specifications before enabling bias tee
- **ALWAYS** disable bias tee before disconnecting antennas
- Incorrect use can damage equipment

## Notes
- Typical bias tee voltage: 4.5V - 5V DC
- Current capacity varies by device (typically 50-100 mA)
- Check LNA current requirements before use
- Some devices may not support bias tee
- Bias tee affects all frequencies on that receiver
- Use lower gain settings with powered LNAs to avoid saturation
- Always disable bias tee when switching to passive antennas
