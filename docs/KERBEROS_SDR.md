# Use Case: KerberosSDR Direction Finding

## Objective
Configure RTL-SDR devices in KerberosSDR mode for coherent multichannel reception and direction finding applications.

## Scenario
A user has a KerberosSDR (4 coherent RTL-SDR receivers) and wants to perform direction finding or beamforming operations.

## Prerequisites
- KerberosSDR hardware (4 synchronized RTL-SDR receivers)
- Understanding of coherent reception requirements
- Appropriate antenna array setup

## Implementation

### Basic KerberosSDR Setup

```csharp
using RtlSdrManager;

var manager = RtlSdrDeviceManager.Instance;

// Open all 4 KerberosSDR channels
for (uint i = 0; i < 4; i++)
{
    manager.OpenManagedDevice(i, $"kerberos-ch{i}");
}

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
    device.TunerGain = 296; // 29.6 dB
    device.AGCMode = AGCModes.Disabled;

    // Configure buffer settings
    device.MaxAsyncBufferSize = 512 * 1024;
    device.DropSamplesOnFullBuffer = true;

    device.ResetDeviceBuffer();
}

Console.WriteLine("All 4 KerberosSDR channels configured");
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
// Start all channels simultaneously for coherent reception
for (int i = 0; i < 4; i++)
{
    manager[$"kerberos-ch{i}"].StartReadSamplesAsync();
}

// Read samples from all channels
while (true)
{
    IQData[] channelData = new IQData[4];
    bool allChannelsReady = true;

    for (int i = 0; i < 4; i++)
    {
        if (!manager[$"kerberos-ch{i}"].AsyncBuffer.TryDequeue(out channelData[i]))
        {
            allChannelsReady = false;
            break;
        }
    }

    if (allChannelsReady)
    {
        // Process coherent data from all 4 channels
        ProcessCoherentSamples(channelData);
    }
    else
    {
        Thread.Sleep(10);
    }
}

void ProcessCoherentSamples(IQData[] samples)
{
    // Implement direction finding or beamforming algorithm here
    Console.WriteLine($"Processing {samples[0].Length} samples from 4 channels");
}
```

## Expected Results
- All 4 channels operate coherently with synchronized sampling
- Frequency dithering reduces DC offset artifacts
- GPIO control enables external hardware integration
- Samples can be phase-compared for direction finding

## Notes
- KerberosSDR mode ensures clock synchronization between channels
- All channels must use identical configuration (frequency, sample rate, gain)
- Frequency dithering slightly varies the center frequency to reduce DC spike
- GPIO control can be used for antenna switching or bias tee control
- Sample alignment is critical for direction finding accuracy
- Consider using manual gain to ensure consistent signal levels across channels
