# RTL-SDR Manager Library for .NET Core

## Installation

RTL-SDR Manager Library is available via NuGet package manager.
You can easily install it via the [usual ways](https://docs.microsoft.com/en-us/nuget/consume-packages/ways-to-install-a-package).

```bash
# dotnet CLI
dotnet add package RtlSdrManager

# nuget CLI
nuget install RtlSdrManager
```

## Usage

Handling RTL-SDR device:
```csharp
// Initialize the Manager instance.
var manager = RtlSdrDeviceManager.Instance;

// Open a managed device and set some parameters.
manager.OpenManagedDevice(0, "my-rtl-sdr");
manager["my-rtl-sdr"].CenterFrequency = new Frequency {MHz = 1090};
manager["my-rtl-sdr"].SampleRate = new Frequency {MHz = 2};
manager["my-rtl-sdr"].TunerGainMode = TunerGainModes.AGC;
manager["my-rtl-sdr"].AGCMode = AGCModes.Enabled;
manager["my-rtl-sdr"].MaxAsyncBufferSize = 512 * 1024;
manager["my-rtl-sdr"].DropSamplesOnFullBuffer = true;
manager["my-rtl-sdr"].ResetDeviceBuffer();
```

Asynchronous sample reading from the device:
```csharp
// Start asynchronous sample reading.
manager["my-rtl-sdr"].StartReadSamplesAsync();
```

Read samples from the buffer:
```csharp
// Dequeue from the buffer.
if (!manager["my-rtl-sdr"].AsyncBuffer.TryDequeue(out var data))
{
    Thread.Sleep(100);
    continue;
}

// Use the sample
Console.WriteLine(data);
```

## Release Notes

v0.2.0 - June 10, 2018

* Bugfix in DeviceInfo handling
* Implement singleton pattern for RtlSdrDeviceManager

v0.1.3 - June 9, 2018

* First public NuGet package

v0.1.2 - June 3, 2018

* Handling crystal frequencies (RTL2832, IC)
* Support tuner bandwith selection (Automatic, Manual)
* Support direct sampling (I-ADC, Q-ADC)
* Support offset tuning mode for zero-IF tuners

v0.1.1 - May 12, 2018

* First public release of the RTL-SDR Manager
* Supports most of the imporant functions of RTL-SDR device