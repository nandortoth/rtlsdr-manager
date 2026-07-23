# KerberosSDR Direction Finding

## Objective

Configure RTL-SDR devices in KerberosSDR mode for coherent multichannel reception and direction finding applications.

## Scenario

A user has a KerberosSDR (4 coherent RTL-SDR receivers) and wants to perform direction finding or beamforming operations using synchronized sample acquisition.

## Prerequisites

- KerberosSDR hardware (4 synchronized RTL-SDR receivers)
- Understanding of coherent reception requirements
- Appropriate antenna array setup

## Implementation

### Basic KerberosSDR Setup

```csharp
using RtlSdrManager;
using RtlSdrManager.Exceptions;
using RtlSdrManager.Modes;

var manager = RtlSdrDeviceManager.Instance;

// Open all 4 KerberosSDR channels
for (uint i = 0; i < 4; i++)
{
    manager.OpenManagedDevice(i, $"kerberos-ch{i}");
}

// All channels must use an identical gain. Pick one supported value up front
// (the tuners are identical, so the first channel's list applies to all).
var supportedGains = manager["kerberos-ch0"].SupportedTunerGains;
double coherentGain = supportedGains[supportedGains.Count / 2];

// Enable KerberosSDR mode on all channels
for (int i = 0; i < 4; i++)
{
    var device = manager[$"kerberos-ch{i}"];

    // Enable KerberosSDR mode for coherent operation
    device.KerberosSDRMode = KerberosSDRModes.Enabled;

    // Configure identical parameters for all channels
    device.CenterFrequency = Frequency.FromMHz(433);
    device.SampleRate = Frequency.FromMHz(2.4);
    device.TunerGainMode = TunerGainModes.Manual;
    device.TunerGain = coherentGain; // same supported gain on every channel
    device.AGCMode = AGCModes.Disabled;

    // Configure buffer settings
    device.MaxAsyncBufferSize = 512 * 1024;
    device.DropSamplesOnFullBuffer = true;

    device.ResetDeviceBuffer();
}

Console.WriteLine($"All 4 KerberosSDR channels configured at {coherentGain} dB");
```

### Enable Frequency Dithering

```csharp
// Enable frequency dithering to reduce DC spike
for (int i = 0; i < 4; i++)
{
    var device = manager[$"kerberos-ch{i}"];
    device.FrequencyDitheringMode = FrequencyDitheringModes.Enabled;
}

Console.WriteLine("Frequency dithering enabled for improved DC offset handling");
```

### GPIO Control for External Hardware

```csharp
// Control GPIO pins for external switching or control
var mainDevice = manager["kerberos-ch0"];

// Enable GPIO pin 1 (e.g., for antenna switching)
mainDevice.SetGPIO(1, GPIOModes.Enabled);
Console.WriteLine("GPIO 1 enabled");

// Disable GPIO pin 1
mainDevice.SetGPIO(1, GPIOModes.Disabled);
Console.WriteLine("GPIO 1 disabled");
```

### Synchronous Sample Acquisition

```csharp
// Start all channels for coherent reception.
for (int i = 0; i < 4; i++)
{
    manager[$"kerberos-ch{i}"].StartReadSamplesAsync();
}

// Direction finding needs aligned blocks of samples, not single samples, so pull an
// equal-sized block from every channel before processing.
const int blockSize = 16 * 1024;

try
{
    var stopAt = DateTime.UtcNow.AddSeconds(5);
    while (DateTime.UtcNow < stopAt)
    {
        var channelBlocks = new List<IQData>[4];
        bool allChannelsReady = true;

        for (int i = 0; i < 4; i++)
        {
            channelBlocks[i] = manager[$"kerberos-ch{i}"].GetSamplesFromAsyncBuffer(blockSize);
            if (channelBlocks[i].Count < blockSize)
            {
                allChannelsReady = false;
                break;
            }
        }

        if (allChannelsReady)
        {
            // Process coherent data from all 4 channels
            ProcessCoherentSamples(channelBlocks);
        }
        else
        {
            Thread.Sleep(10);
        }
    }
}
finally
{
    // Stop every channel, then release all devices.
    for (int i = 0; i < 4; i++)
    {
        try
        {
            manager[$"kerberos-ch{i}"].StopReadSamplesAsync();
        }
        catch (RtlSdrManagedDeviceException e)
        {
            Console.WriteLine($"Channel {i} stopped with an error: {e.InnerException?.Message}");
        }
    }

    manager.CloseAllManagedDevice();
}

void ProcessCoherentSamples(List<IQData>[] channels)
{
    // Implement direction finding or beamforming across the aligned blocks here.
    Console.WriteLine($"Processing {channels[0].Count} samples across {channels.Length} channels");
}
```

## Expected Results

- All 4 channels operate coherently with synchronized sampling.
- Frequency dithering reduces DC offset artifacts.
- GPIO control enables external hardware integration.
- Samples can be phase-compared for direction finding.

## Notes

- KerberosSDR mode ensures clock synchronization between channels.
- All channels must use identical configuration (frequency, sample rate, gain).
- Frequency dithering slightly varies the center frequency to reduce the DC spike.
- GPIO control can be used for antenna switching or bias tee control.
- Sample alignment is critical for direction finding accuracy.
- Use manual gain to ensure consistent signal levels across all channels.

## See Also

- [Basic Setup](BASIC_SETUP.md) — Device initialization and first sample acquisition
- [Device Management](DEVICE_MANAGEMENT.md) — Managing multiple RTL-SDR devices
- [Bias Tee](BIAS_TEE.md) — Powering external LNAs via bias tee
- [Main README](../README.md) — Library overview and features
