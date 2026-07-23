# Multi-Device Management

## Objective

Demonstrate how to enumerate, identify, and manage multiple RTL-SDR devices simultaneously.

## Scenario

A system has multiple RTL-SDR devices connected and needs to identify, configure, and use them for different purposes (e.g., one for ADS-B reception and another for FM radio).

## Prerequisites

- Multiple RTL-SDR devices connected
- Each device potentially having different characteristics

## Implementation

### Device Enumeration

```csharp
using RtlSdrManager;
using RtlSdrManager.Modes;

var manager = RtlSdrDeviceManager.Instance;

// Get total number of connected devices
var deviceCount = manager.CountDevices;
Console.WriteLine($"Found {deviceCount} RTL-SDR device(s)");

if (deviceCount == 0)
{
    Console.WriteLine("No devices found. Please connect an RTL-SDR device.");
    return;
}

// Enumerate all devices and show their information.
// Devices is a Dictionary<uint, DeviceInfo> keyed by device index.
foreach (var (index, deviceInfo) in manager.Devices)
{
    Console.WriteLine($"\nDevice {index}:");
    Console.WriteLine($"  Manufacturer: {deviceInfo.Manufacturer}");
    Console.WriteLine($"  Product: {deviceInfo.ProductType}");
    Console.WriteLine($"  Serial: {deviceInfo.Serial}");
}
```

### Opening Multiple Devices

```csharp
// Open multiple devices with descriptive names
if (deviceCount >= 2)
{
    manager.OpenManagedDevice(0, "adsb-receiver");
    manager.OpenManagedDevice(1, "fm-radio");

    Console.WriteLine("Opened 2 devices with friendly names");
}

// The manager is enumerable over its managed (open) devices.
Console.WriteLine($"\nOpen devices: {manager.CountManagedDevices}");
foreach (var device in manager)
{
    Console.WriteLine($"  - {device.DeviceInfo.Name} (serial {device.DeviceInfo.Serial})");
}
```

### Device-Specific Configuration

```csharp
// Configure first device for ADS-B reception
var adsbDevice = manager["adsb-receiver"];
adsbDevice.CenterFrequency = Frequency.FromMHz(1090);
adsbDevice.SampleRate = Frequency.FromMHz(2);
adsbDevice.TunerGainMode = TunerGainModes.AGC;
adsbDevice.AGCMode = AGCModes.Enabled;

// Configure second device for FM radio
var fmDevice = manager["fm-radio"];
fmDevice.CenterFrequency = Frequency.FromMHz(100);
fmDevice.SampleRate = Frequency.FromMHz(1.2);
fmDevice.TunerGainMode = TunerGainModes.Manual;
var fmGains = fmDevice.SupportedTunerGains;
fmDevice.TunerGain = fmGains[fmGains.Count / 2]; // dB, chosen from the supported list

Console.WriteLine("Both devices configured for different applications");
```

### Checking Device Capabilities

```csharp
void DisplayDeviceCapabilities(RtlSdrManagedDevice device)
{
    // SupportedTunerGains is already expressed in dB.
    Console.WriteLine($"\n{device.DeviceInfo.Name} Capabilities:");
    Console.WriteLine($"  Tuner Type: {device.TunerType}");
    Console.WriteLine($"  Available Gains: {string.Join(", ", device.SupportedTunerGains.Select(g => $"{g:F1} dB"))}");
    Console.WriteLine($"  Current Frequency: {device.CenterFrequency}");
    Console.WriteLine($"  Current Sample Rate: {device.SampleRate}");
}

foreach (var device in manager)
{
    DisplayDeviceCapabilities(device);
}
```

### Closing Devices

```csharp
// Close a specific device
manager.CloseManagedDevice("adsb-receiver");
Console.WriteLine("Closed adsb-receiver device");

// Close all devices
manager.CloseAllManagedDevice();
Console.WriteLine("All devices closed");
```

### Device Identification by Serial Number

```csharp
// Find device with specific serial number
uint? FindDeviceBySerial(string serial)
{
    foreach (var (index, info) in manager.Devices)
    {
        if (info.Serial == serial)
        {
            return index;
        }
    }
    return null;
}

var targetSerial = "00000001";
var deviceIndex = FindDeviceBySerial(targetSerial);

if (deviceIndex.HasValue)
{
    manager.OpenManagedDevice(deviceIndex.Value, "target-device");
    Console.WriteLine($"Found and opened device with serial {targetSerial}");
}
else
{
    Console.WriteLine($"Device with serial {targetSerial} not found");
}
```

## Expected Results

- All connected devices are discovered.
- Device information is accurately retrieved.
- Multiple devices can be operated simultaneously with independent configurations.
- Devices can be identified by serial number for persistent identification across reconnects.

## Notes

- Device indices are 0-based.
- Friendly names make device management more intuitive than using numeric indices.
- `DeviceInfo` provides the device index, name, manufacturer, product type, and serial number.
- Each device operates independently with its own buffer.
- Closing devices properly releases hardware resources.

## See Also

- [Basic Setup](BASIC_SETUP.md) — Device initialization and first sample acquisition
- [Manual Gain Control](MANUAL_GAIN_CONTROL.md) — Configuring tuner gain settings
- [Main README](../README.md) — Library overview and features
