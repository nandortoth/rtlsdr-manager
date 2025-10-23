# Use Case: Multi-Device Management

## Objective
Demonstrate how to enumerate, identify, and manage multiple RTL-SDR devices simultaneously.

## Scenario
A system has multiple RTL-SDR devices connected and needs to identify, configure, and use them for different purposes.

## Prerequisites
- Multiple RTL-SDR devices connected
- Each device potentially having different characteristics

## Implementation

### Device Enumeration

```csharp
using RtlSdrManager;

var manager = RtlSdrDeviceManager.Instance;

// Get total number of connected devices
var deviceCount = manager.Count;
Console.WriteLine($"Found {deviceCount} RTL-SDR device(s)");

if (deviceCount == 0)
{
    Console.WriteLine("No devices found. Please connect an RTL-SDR device.");
    return;
}

// Enumerate all devices and show their information
for (uint i = 0; i < deviceCount; i++)
{
    var deviceInfo = manager.GetDeviceInfo(i);

    Console.WriteLine($"\nDevice {i}:");
    Console.WriteLine($"  Manufacturer: {deviceInfo.Manufacturer}");
    Console.WriteLine($"  Product: {deviceInfo.Product}");
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

// List all open devices
var openDevices = manager.GetManagedDeviceNames();
Console.WriteLine("\nOpen devices:");
foreach (var name in openDevices)
{
    Console.WriteLine($"  - {name}");
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
fmDevice.TunerGain = 200; // 20.0 dB

Console.WriteLine("Both devices configured for different applications");
```

### Checking Device Capabilities

```csharp
void DisplayDeviceCapabilities(string deviceName)
{
    var device = manager[deviceName];

    Console.WriteLine($"\n{deviceName} Capabilities:");
    Console.WriteLine($"  Tuner Type: {device.TunerType}");
    Console.WriteLine($"  Available Gains: {string.Join(", ", device.TunerGains.Select(g => $"{g * 0.1:F1} dB"))}");
    Console.WriteLine($"  Current Frequency: {device.CenterFrequency}");
    Console.WriteLine($"  Current Sample Rate: {device.SampleRate}");
}

foreach (var deviceName in manager.GetManagedDeviceNames())
{
    DisplayDeviceCapabilities(deviceName);
}
```

### Closing Devices

```csharp
// Close a specific device
manager.CloseManagedDevice("adsb-receiver");
Console.WriteLine("Closed adsb-receiver device");

// Close all devices
manager.CloseAllManagedDevices();
Console.WriteLine("All devices closed");
```

### Device Identification by Serial Number

```csharp
// Find device with specific serial number
uint? FindDeviceBySerial(string serial)
{
    for (uint i = 0; i < manager.Count; i++)
    {
        var info = manager.GetDeviceInfo(i);
        if (info.Serial == serial)
        {
            return i;
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
- All connected devices are discovered
- Device information is accurately retrieved
- Multiple devices can be operated simultaneously
- Each device can have independent configuration
- Devices can be identified by serial number

## Notes
- Device indices are 0-based
- Friendly names make device management more intuitive
- DeviceInfo provides manufacturer, product, and serial number
- Each device operates independently with its own buffer
- Closing devices properly releases hardware resources
- Serial numbers can be used for persistent device identification across reconnects
