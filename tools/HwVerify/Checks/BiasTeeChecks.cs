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
using System.Threading;
using RtlSdrManager.Hardware;
using RtlSdrManager.Modes;

namespace RtlSdrManager.Tools.HwVerify.Checks;

/// <summary>
/// Verify bias tee GPIO behavior: pin validation, and that no tuner is refused.
/// </summary>
/// <remarks>
/// The bias tee control pins belong to the demodulator rather than the tuner, so these calls
/// should succeed on any device. That is the claim this check exists to test; note it cannot
/// be proven on an R820T, which the removed restriction already allowed.
/// <para>
/// Only pin 0 is ever driven, and only to <see cref="BiasTeeModes.Disabled"/>. Setting a pin
/// also switches it to output mode, and nothing resets that when the device is closed, so
/// probing an unrelated pin would leave it reconfigured with no way to undo it. Pin 0 is the
/// pin the bias tee already uses, and driving it low is its safe state. It is also sufficient:
/// the restriction this checks for rejected every pin on the wrong tuner, so one accepted call
/// disproves it.
/// </para>
/// </remarks>
internal sealed class BiasTeeChecks : IHardwareCheck
{
    /// <summary>
    /// Opt-in flag for the bias tee power-on check.
    /// </summary>
    public const string BiasTeeOnFlag = "--biastee-on";

    /// <summary>
    /// True when the power-on check should run.
    /// </summary>
    private readonly bool _powerOnRequested;

    /// <summary>
    /// Create the bias tee checks.
    /// </summary>
    /// <param name="powerOnRequested">
    /// When true, briefly enables the bias tee so the feed voltage can be metered. Only pass
    /// this with the antenna disconnected.
    /// </param>
    public BiasTeeChecks(bool powerOnRequested)
    {
        _powerOnRequested = powerOnRequested;
    }

    /// <inheritdoc />
    public string Title => "Bias tee GPIO";

    /// <inheritdoc />
    public void Run(RtlSdrManagedDevice device, VerificationReport report)
    {
        TunerTypes tuner = device.TunerType;

        // Both bounds are checked: an off-by-one at either end would let a bad pin number
        // reach the device, where it would silently address the wrong bit.
        report.Check("GPIO pin 8 throws ArgumentOutOfRangeException",
            () => VerificationReport.Throws<ArgumentOutOfRangeException>(
                () => device.SetBiasTeeGPIO(8, BiasTeeModes.Disabled)),
            "ArgumentOutOfRangeException; it was RtlSdrLibraryExecutionException before 0.8.0");

        report.Check("GPIO pin -1 throws ArgumentOutOfRangeException",
            () => VerificationReport.Throws<ArgumentOutOfRangeException>(
                () => device.SetBiasTeeGPIO(-1, BiasTeeModes.Disabled)),
            "ArgumentOutOfRangeException");

        // Pin 0 only: see the remarks above on why no other pin is probed.
        report.Check("SetBiasTeeGPIO(0, Disabled) succeeds",
            () =>
            {
                device.SetBiasTeeGPIO(0, BiasTeeModes.Disabled);
                return true;
            },
            "no exception; the removed tuner restriction rejected this on the wrong tuner");

        if (tuner is TunerTypes.R820T or TunerTypes.R828D)
        {
            report.Skip("the bias tee works on a tuner other than the R820T",
                $"this device is {tuner}, which the removed restriction already permitted. " +
                "Confirming the fix needs an FC0012, FC0013 or E4000 dongle");
        }

        if (_powerOnRequested)
        {
            report.Check("SetBiasTee(Enabled) followed by Disabled succeeds",
                () =>
                {
                    device.SetBiasTee(BiasTeeModes.Enabled);

                    // Long enough to get a meter on the feed, short enough that nothing is
                    // left powered by accident if the run is interrupted here.
                    Thread.Sleep(300);
                    device.SetBiasTee(BiasTeeModes.Disabled);
                    return true;
                },
                "no exception; meter the feed during the 300 ms window to confirm the voltage");
        }
        else
        {
            report.Skip("bias tee power-on test",
                $"pass {BiasTeeOnFlag} to run it, and only with the antenna disconnected");
        }

        // Closing the device does not clear the bias tee pin, so an interrupted or failed run
        // could otherwise leave the feed powered. This is the best effort by design: a failure
        // here must not mask the results above.
        try
        {
            device.SetBiasTee(BiasTeeModes.Disabled);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  WARN  could not turn the bias tee off on the way out: {ex.Message}");
        }
    }
}
