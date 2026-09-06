# Basic Device Setup and Sample Reading

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
using RtlSdrManager.Exceptions;
using RtlSdrManager.Modes;

// Initialize the manager instance
var manager = RtlSdrDeviceManager.Instance;

// Check if any devices are available
var deviceCount = manager.CountDevices;
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

// Read samples for a bounded period, then always stop and release the device.
try
{
    var stopAt = DateTime.UtcNow.AddSeconds(5);
    while (DateTime.UtcNow < stopAt)
    {
        // Dequeue a batch of I/Q samples (returns an empty list when none are ready)
        var samples = manager["my-rtl-sdr"].GetSamplesFromAsyncBuffer(16 * 1024);
        if (samples.Count > 0)
        {
            Console.WriteLine($"Received {samples.Count} samples");
            // Process the I/Q data here (each sample exposes .I and .Q)
        }
        else
        {
            Thread.Sleep(100);
        }
    }
}
finally
{
    // Since v0.7.0, StopReadSamplesAsync rethrows any error that stopped the reading
    // (for example a device failure). Always close the device regardless.
    try
    {
        manager["my-rtl-sdr"].StopReadSamplesAsync();
    }
    catch (RtlSdrManagedDeviceException e)
    {
        Console.WriteLine($"Asynchronous reading stopped with an error: {e.InnerException?.Message}");
    }
    finally
    {
        manager.CloseManagedDevice("my-rtl-sdr");
    }
}
```

## Expected Results

- Device initializes successfully.
- Samples are read from the device for the bounded period, then reading stops cleanly.
- Buffer manages data flow automatically.
- AGC adjusts gain levels automatically.

## Notes

- The `DropSamplesOnFullBuffer` setting prevents buffer overflow by dropping old samples when the buffer is full.
- `TransferBufferCount` controls how many buffers the device fills in rotation, and is the main lever if samples are being dropped or latency matters. Raising it absorbs longer pauses in the code consuming samples; lowering it cuts worst-case delay. The default suits most applications, and it must be set before `StartReadSamplesAsync()`. Note that this is separate from `MaxAsyncBufferSize`, which sizes the queue your code reads from rather than the device's own buffers.
- Async reading runs in a background thread, so always stop it with `StopReadSamplesAsync()` and release the device with `CloseManagedDevice(...)` (or `Dispose()`) when finished.
- **Take one reading per process.** Ending an asynchronous reading can terminate the process:
  the native library releases its transfer buffers before every canceled transfer has finished
  reporting, and a late one then writes into released memory. Start once, retune
  `CenterFrequency` while the reading runs, and stop once at the end. Do not stop and restart,
  and do not close and reopen the device between readings; both reach the defect. `ReadSamples()`
  is unaffected. See [Known Limitations](../README.md#known-limitations) for the detail and a
  worked retuning example.
- `StopReadSamplesAsync()` rethrows any error that stopped the reading; wrap it so cleanup still runs.
- A sample rate of 2 MHz provides good coverage for most applications.
- This example uses the default per-sample delivery mode (`IQData` via `GetSamplesFromAsyncBuffer`).
  Processing at high sample rates? See [Raw Buffer Mode](../README.md#raw-buffer-mode-zero-copy)
  to avoid per-sample overhead by consuming pooled byte buffers directly.

## See Also

- [Raw Buffer Mode](../README.md#raw-buffer-mode-zero-copy) — Zero-copy delivery for high-throughput reading
- [Device Management](DEVICE_MANAGEMENT.md) — Managing multiple RTL-SDR devices
- [Manual Gain Control](MANUAL_GAIN_CONTROL.md) — Configuring tuner gain settings
- [Main README](../README.md) — Library overview and features
