// RTL-SDR Manager Library for .NET
// Copyright (C) 2018-2026 Nandor Toth <dev@nandortoth.com>
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see http://www.gnu.org/licenses.

using System;
using RtlSdrManager.Exceptions;
using RtlSdrManager.Modes;

namespace RtlSdrManager.Tools.HwVerify.Checks;

/// <summary>
/// Verify that direct sampling reaches frequencies the tuner cannot.
/// </summary>
/// <remarks>
/// This is the check that needs hardware most. The demodulator accepts an out of range
/// frequency without complaint and quietly receives something else, so nothing but a real
/// device can confirm that the limit is enforced in the right place.
/// <para>
/// Direct sampling is volatile register state, not a stored setting: turning it off restores
/// every register the enable path touched, and unplugging the device would clear it
/// regardless. The original mode is captured and restored here anyway.
/// </para>
/// </remarks>
internal sealed class DirectSamplingChecks : IHardwareCheck
{
    /// <inheritdoc />
    public string Title => "Direct sampling";

    /// <inheritdoc />
    public void Run(RtlSdrManagedDevice device, VerificationReport report)
    {
        DirectSamplingModes originalMode = device.DirectSamplingMode;

        try
        {
            // Entered from whatever frequency the previous checks left behind, which is well
            // above the ADC's reach. That is the interesting case: the frequency has to be
            // normalized rather than silently truncated.
            device.DirectSamplingMode = DirectSamplingModes.InPhaseADCInputEnabled;

            report.Check("enabling direct sampling leaves a frequency the ADC can actually reach",
                () => device.SupportedFrequencyRanges[0].Contains(device.CenterFrequency),
                "the center frequency was reset to 0 Hz, because the previous one was out of reach");

            report.Check("SupportedFrequencyRanges switches to the ADC range",
                () => device.SupportedFrequencyRanges is [{ Minimum.Hz: 0 } _],
                "a single range starting at 0 Hz, instead of the bypassed tuner's range");

            Console.WriteLine($"        SupportedFrequencyRanges = " +
                              $"{string.Join(" and ", device.SupportedFrequencyRanges)}");

            report.Check("the 40 m amateur band is reachable (7.1 MHz)",
                () =>
                {
                    device.CenterFrequency = Frequency.FromMHz(7.1);
                    return device.CenterFrequency.Hz == 7_100_000;
                },
                "7.1 MHz set and read back; this threw ArgumentOutOfRangeException before 0.8.0");

            report.Check("0 Hz is a valid frequency and reads back",
                () =>
                {
                    device.CenterFrequency = Frequency.FromHz(0u);
                    return device.CenterFrequency.Hz == 0;
                },
                "0 Hz accepted and returned; reading it threw before 0.8.0");

            report.Check("a frequency beyond the ADC's reach is refused rather than truncated",
                () => VerificationReport.Throws<ArgumentOutOfRangeException>(
                    () => device.CenterFrequency = Frequency.FromMHz(20)),
                "ArgumentOutOfRangeException; the device would otherwise receive a different " +
                "frequency without reporting anything");
        }
        finally
        {
            RestoreDirectSamplingMode(device, originalMode);
        }
    }

    /// <summary>
    /// Put the device back into the sampling mode it was using before the checks ran.
    /// </summary>
    /// <param name="device">The device under test.</param>
    /// <param name="originalMode">The mode captured before the checks ran.</param>
    /// <remarks>
    /// Best effort, like the other restorations: leaving direct sampling on would be
    /// surprising, but failing to report the check results would be worse. Turning it off
    /// re-applies the current frequency to the tuner, which the checks have deliberately left
    /// at a value only the ADC can reach, so that failure is expected and handled here.
    /// </remarks>
    private static void RestoreDirectSamplingMode(RtlSdrManagedDevice device,
        DirectSamplingModes originalMode)
    {
        try
        {
            device.DirectSamplingMode = originalMode;
        }
        catch (RtlSdrLibraryExecutionException)
        {
            // Expected: the mode did change, only the re-tune failed. Give the tuner a
            // frequency it can reach so the device is left usable.
            try
            {
                device.CenterFrequency = device.SupportedFrequencyRanges[0].Minimum;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  WARN  could not restore a tunable center frequency: {ex.Message}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  WARN  could not restore the direct sampling mode to " +
                              $"{originalMode}: {ex.Message}");
        }
    }
}
