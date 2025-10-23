# Use Case: Frequency Correction and Crystal Tuning

## Objective
Calibrate and correct frequency offset caused by crystal oscillator inaccuracies in RTL-SDR devices.

## Scenario
A user notices that received frequencies are slightly off from expected values and needs to calibrate the device's frequency accuracy.

## Prerequisites
- RTL-SDR device
- Known reference signal (FM station, GSM tower, or calibration signal)
- Method to measure frequency offset (spectrum analyzer, SDR software)

## Implementation

### Setting Frequency Correction (PPM)

```csharp
using RtlSdrManager;

var manager = RtlSdrDeviceManager.Instance;
manager.OpenManagedDevice(0, "my-device");

// Set frequency correction in parts per million (PPM)
// Positive values increase frequency, negative values decrease it
manager["my-device"].FrequencyCorrection = 52; // Example: +52 PPM

Console.WriteLine($"Frequency correction set to {manager["my-device"].FrequencyCorrection} PPM");

// Configure device
manager["my-device"].CenterFrequency = Frequency.FromMHz(100);
manager["my-device"].SampleRate = Frequency.FromMHz(2);
```

### Calibration Process Using Known Signal

```csharp
void CalibrateDevice(string deviceName, double knownFrequencyMHz)
{
    var device = manager[deviceName];

    Console.WriteLine($"Calibrating device using signal at {knownFrequencyMHz} MHz");

    // Start with no correction
    device.FrequencyCorrection = 0;
    device.CenterFrequency = Frequency.FromMHz(knownFrequencyMHz);
    device.SampleRate = Frequency.FromMHz(2);
    device.TunerGainMode = TunerGainModes.AGC;
    device.AGCMode = AGCModes.Enabled;

    device.ResetDeviceBuffer();
    device.StartReadSamplesAsync();

    Console.WriteLine("Listen to the signal and measure the frequency offset");
    Console.WriteLine("Then calculate PPM = (measured_freq - actual_freq) / actual_freq * 1,000,000");
    Console.WriteLine("\nExample: If 100.0 MHz signal appears at 100.005 MHz:");
    Console.WriteLine("PPM = (100.005 - 100.0) / 100.0 * 1,000,000 = 50 PPM");
}

// Example: Calibrate using FM radio station
CalibrateDevice("my-device", 100.0); // Known FM station at 100.0 MHz
```

### Applying Calculated PPM Correction

```csharp
void ApplyCalibration(string deviceName, double measuredFreqMHz, double actualFreqMHz)
{
    var device = manager[deviceName];

    // Calculate PPM offset
    double ppmOffset = ((measuredFreqMHz - actualFreqMHz) / actualFreqMHz) * 1_000_000;

    // Round to nearest integer
    int ppmCorrection = (int)Math.Round(ppmOffset);

    // Apply correction
    device.FrequencyCorrection = ppmCorrection;

    Console.WriteLine($"Measured: {measuredFreqMHz} MHz");
    Console.WriteLine($"Actual: {actualFreqMHz} MHz");
    Console.WriteLine($"Calculated PPM: {ppmOffset:F2}");
    Console.WriteLine($"Applied correction: {ppmCorrection} PPM");
}

// Example: If 100.0 MHz FM station appears at 100.005 MHz
ApplyCalibration("my-device", 100.005, 100.0);
```

### Testing Calibration Accuracy

```csharp
void VerifyCalibration(string deviceName, params double[] knownFrequencies)
{
    var device = manager[deviceName];

    Console.WriteLine($"\nVerifying calibration with {device.FrequencyCorrection} PPM correction:");

    foreach (var freq in knownFrequencies)
    {
        device.CenterFrequency = Frequency.FromMHz(freq);
        Thread.Sleep(500); // Let it stabilize

        Console.WriteLine($"  Tuned to: {freq} MHz - Check if signal is centered");
    }
}

// Test with multiple known frequencies
VerifyCalibration("my-device", 100.0, 145.0, 433.0, 1090.0);
```

### Advanced: Temperature Compensation

```csharp
// Store PPM values at different temperatures
Dictionary<double, int> temperaturePPM = new Dictionary<double, int>
{
    { 20.0, 52 },  // Room temperature
    { 30.0, 48 },  // Warm
    { 15.0, 56 }   // Cool
};

void ApplyTemperatureCompensation(string deviceName, double currentTemp)
{
    var device = manager[deviceName];

    // Find closest temperature measurement
    var closestTemp = temperaturePPM.Keys
        .OrderBy(t => Math.Abs(t - currentTemp))
        .First();

    device.FrequencyCorrection = temperaturePPM[closestTemp];

    Console.WriteLine($"Applied PPM correction for {closestTemp}°C: {device.FrequencyCorrection} PPM");
}
```

### Crystal Frequency Information

```csharp
// Get crystal frequencies (RTL2832 and tuner IC)
var device = manager["my-device"];

var rtlFreq = device.CrystalFrequency;
Console.WriteLine($"RTL2832 Crystal Frequency: {rtlFreq.Hz} Hz");

var tunerFreq = device.TunerCrystalFrequency;
Console.WriteLine($"Tuner Crystal Frequency: {tunerFreq.Hz} Hz");

// Note: These are nominal values and don't reflect actual accuracy
```

## Expected Results
- Received signals appear at correct frequencies
- Frequency accuracy improves significantly
- Calibration remains stable over time (temperature dependent)
- PPM correction applies to all tuned frequencies

## Calibration Tips

1. **Use Multiple Reference Frequencies**: Calibrate using signals across the spectrum
2. **Account for Temperature**: PPM offset varies with temperature
3. **Common PPM Ranges**: Most RTL-SDR devices have -100 to +100 PPM offset
4. **Stable Reference Signals**:
   - FM radio stations
   - GSM base stations
   - ADS-B signals (1090 MHz)
   - Amateur radio beacons

## Notes
- PPM correction is device-specific and should be saved per device
- Temperature changes affect crystal frequency
- Cheap RTL-SDR devices may have larger PPM offsets
- Calibration improves with device warm-up time
- Some applications (narrow-band) are more sensitive to frequency errors
- PPM value is applied as: actual_freq = tuned_freq * (1 + PPM/1,000,000)
