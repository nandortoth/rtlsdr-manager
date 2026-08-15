# Bias Tee Control

## Objective

Enable and control the bias tee to power active antennas or LNAs directly from the RTL-SDR device.

## Scenario

A user has an active antenna or LNA that requires DC power and wants to supply it through the coaxial cable using the RTL-SDR's bias tee feature.

## Prerequisites

- RTL-SDR device with bias tee support (RTL-SDR Blog V3, etc.)
- Active antenna or LNA that accepts bias tee power
- Understanding of voltage/current requirements of the connected equipment

Bias tee support is a question of how the dongle is wired, not of which tuner it uses: the
control pins belong to the demodulator, which is the same chip on every supported device.
A dongle without the bias tee circuitry will accept the call and simply do nothing useful.

## Implementation

### Enabling Bias Tee

```csharp
using RtlSdrManager;
using RtlSdrManager.Modes;

var manager = RtlSdrDeviceManager.Instance;
manager.OpenManagedDevice(0, "my-device");

// Enable bias tee to power active antenna (GPIO 0)
manager["my-device"].SetBiasTee(BiasTeeModes.Enabled);

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
manager["my-device"].SetBiasTee(BiasTeeModes.Disabled);

Console.WriteLine("Bias tee disabled - antenna power removed");
```

### Using a Different GPIO Pin

Most dongles wire the bias tee to GPIO pin 0, which is what `SetBiasTee` uses. Hardware that
routes it elsewhere, or a design that switches something else over a spare pin, can target
any pin from 0 to 7:

```csharp
// Equivalent to SetBiasTee(BiasTeeModes.Enabled)
manager["my-device"].SetBiasTeeGPIO(gpio: 0, BiasTeeModes.Enabled);

// Drive a different pin
manager["my-device"].SetBiasTeeGPIO(gpio: 1, BiasTeeModes.Enabled);
```

This works on every supported tuner. A pin outside 0 to 7 throws
`ArgumentOutOfRangeException`.

**Two pins are reserved.** Pin 4 is pulsed to reset the tuner while the device is being
opened, and pin 6 selects the band filter on FC0012 tuners, where it is rewritten on every
retune. Neither is blocked, matching the reference tooling, but driving them as a bias tee
control produces confusing results: on an FC0012, pin 6 will fight the tuner every time the
center frequency changes.

### Safe Bias Tee Usage Pattern

Closing the device does **not** turn the bias tee off. The pin keeps its state, so power
stays on the antenna feed after `CloseManagedDevice`, after `Dispose`, and after the process
exits; it survives until something clears the pin or the dongle is unplugged. The
`finally` block below is therefore not just tidiness, it is the only thing that removes power
on an error path:

```csharp
void UseBiasTee(Action receiveAction)
{
    var device = manager["my-device"];

    try
    {
        // Enable bias tee
        device.SetBiasTee(BiasTeeModes.Enabled);
        Console.WriteLine("Bias tee ON");

        // Wait for LNA to stabilize
        Thread.Sleep(500);

        // Perform reception
        receiveAction();
    }
    finally
    {
        // Always disable bias tee when done
        device.SetBiasTee(BiasTeeModes.Disabled);
        Console.WriteLine("Bias tee OFF");
    }
}

// Usage
UseBiasTee(() =>
{
    manager["my-device"].StartReadSamplesAsync();
    Thread.Sleep(10000); // Receive for 10 seconds
    manager["my-device"].StopReadSamplesAsync();
});
```

### Application-Specific Configuration

```csharp
// For ADS-B reception with powered LNA
void ConfigureADSBWithLNA()
{
    var device = manager["adsb-device"];

    // Enable bias tee for LNA
    device.SetBiasTee(BiasTeeModes.Enabled);

    // Reduce gain since the LNA provides amplification. Pick a low value from the
    // tuner's supported gains rather than hardcoding a dB value it may not accept.
    device.TunerGainMode = TunerGainModes.Manual;
    var gains = device.SupportedTunerGains;
    device.TunerGain = gains[gains.Count / 4]; // a lower-end supported gain

    device.CenterFrequency = Frequency.FromMHz(1090);
    device.SampleRate = Frequency.FromMHz(2);

    Console.WriteLine("ADS-B receiver configured with powered LNA");
}

// For satellite reception with powered antenna
void ConfigureSatelliteReception()
{
    var device = manager["sat-device"];

    // Enable bias tee for active antenna
    device.SetBiasTee(BiasTeeModes.Enabled);

    device.CenterFrequency = Frequency.FromMHz(137.5); // NOAA satellites
    device.SampleRate = Frequency.FromMHz(2.4);
    device.TunerGainMode = TunerGainModes.AGC;
    device.AGCMode = AGCModes.Enabled;

    Console.WriteLine("Satellite receiver configured with powered antenna");
}
```

## Expected Results

- Bias tee supplies DC power through the coaxial cable.
- Active antenna or LNA receives power and operates.
- Signal quality improves due to active amplification.
- Gain settings may need adjustment to account for LNA gain.

## Safety Warnings

**Warning:** Incorrect use of the bias tee can damage equipment.

- **Never** enable bias tee with passive antennas or unamplified connections.
- **Never** enable bias tee when the antenna port is connected to another receiver.
- **Always** check equipment specifications before enabling bias tee.
- **Always** disable bias tee before disconnecting antennas.
- **Never assume closing the application removes power.** The bias tee stays on after the
  device is closed and after the process exits. Disable it explicitly, from a `finally`
  block, before releasing the device. If a program crashed with the bias tee on, the feed is
  still live: run it again and disable the pin, or unplug the dongle.

## Notes

- Typical bias tee voltage: 4.5 V -- 5 V DC.
- Current capacity varies by device (typically 50 -- 100 mA).
- Check LNA current requirements before use.
- Some devices are not wired for bias tee; the call succeeds but nothing is powered.
- Bias tee affects all frequencies on that receiver.
- Use lower gain settings with powered LNAs to avoid saturation.
- Always disable bias tee when switching to passive antennas.
- The control pins belong to the demodulator, so `SetBiasTeeGPIO` works on every tuner.
  Pins 4 and 6 are reserved for the tuner reset and the FC0012 band filter.

## See Also

- [Basic Setup](BASIC_SETUP.md) — Device initialization and first sample acquisition
- [KerberosSDR](KERBEROS_SDR.md) — Coherent SDR array features
- [Main README](../README.md) — Library overview and features
