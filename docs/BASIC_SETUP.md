# Use Case: Basic Device Setup and Sample Reading

## Objective
Demonstrate how to initialize an RTL-SDR device, configure basic parameters, and read samples asynchronously.

## Scenario
A developer wants to start capturing radio signals at a specific frequency (e.g., 1090 MHz for ADS-B aircraft tracking) with automatic gain control.

## Prerequisites
- RTL-SDR device connected via USB
- RtlSdrManager library installed

## Implementation

```csharp
using RtlSdrManager;

// Initialize the manager instance
var manager = RtlSdrDeviceManager.Instance;

// Check if any devices are available
var deviceCount = manager.Count;
if (deviceCount == 0)
{
    Console.WriteLine("No RTL-SDR devices found");
    return;
}

// Open the first device with a friendly name
manager.OpenManagedDevice(0, "my-rtl-sdr");

// Configure basic parameters
manager["my-rtl-sdr"].CenterFrequency = Frequency.FromMHz(1090);
manager["my-rtl-sdr"].SampleRate = Frequency.FromMHz(2);
manager["my-rtl-sdr"].TunerGainMode = TunerGainModes.AGC;
manager["my-rtl-sdr"].AGCMode = AGCModes.Enabled;
manager["my-rtl-sdr"].MaxAsyncBufferSize = 512 * 1024;
manager["my-rtl-sdr"].DropSamplesOnFullBuffer = true;

// Reset the device buffer before starting
manager["my-rtl-sdr"].ResetDeviceBuffer();

// Start asynchronous sample reading
manager["my-rtl-sdr"].StartReadSamplesAsync();

// Read samples from the buffer
while (true)
{
    if (manager["my-rtl-sdr"].AsyncBuffer.TryDequeue(out var data))
    {
        Console.WriteLine($"Received {data.Length} samples");
        // Process the IQ data here
    }
    else
    {
        Thread.Sleep(100);
    }
}
```

## Expected Results
- Device initializes successfully
- Samples are continuously read from the device
- Buffer manages data flow automatically
- AGC adjusts gain levels automatically

## Notes
- The `DropSamplesOnFullBuffer` setting prevents buffer overflow by dropping old samples
- Async reading runs in a background thread
- Sample rate of 2 MHz provides good coverage for most applications
