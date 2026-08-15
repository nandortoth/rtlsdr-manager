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
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using RtlSdrManager.Hardware;
using RtlSdrManager.Modes;

namespace RtlSdrManager.Tools.HwVerify;

/// <summary>
/// Hardware verification harness for RtlSdrManager.
/// </summary>
/// <remarks>
/// The unit test suite deliberately covers only hardware-independent components, so behavior
/// that depends on a real device has no automated coverage. This harness fills that gap:
/// attach a dongle, run it before cutting a release, and extend it whenever a fix depends on
/// hardware. It exits nonzero on any failure, so it also works as a release gate.
/// <para>
/// The harness restores what it changes. The tuner gain mode is captured on entry and put
/// back on exit, including on the failure path. The bias tee is only ever written with
/// <see cref="BiasTeeModes.Disabled"/>, and only on pin 0, because setting a pin also
/// switches it to output mode and nothing clears that when the device is closed. Passing
/// <see cref="BiasTeeOnFlag"/> additionally tests enabling the bias tee, which must not be
/// done with a passive antenna connected.
/// </para>
/// <para>
/// Two things are still worth knowing. Device state does not survive a replug, so unplugging
/// the dongle resets anything the harness touched. And the harness intentionally leaves the
/// bias tee off rather than restoring its previous value: off is the safe state, and reading
/// the pin back is not possible.
/// </para>
/// </remarks>
internal static class Program
{
    /// <summary>
    /// Exit code reported when every check that ran passed.
    /// </summary>
    private const int ExitSuccess = 0;

    /// <summary>
    /// Exit code reported when at least one check failed.
    /// </summary>
    private const int ExitFailure = 1;

    /// <summary>
    /// Exit code reported when no device is attached, so nothing could be verified.
    /// Distinct from <see cref="ExitFailure"/>: an empty run is inconclusive, not a failure.
    /// </summary>
    private const int ExitNoDevice = 2;

    /// <summary>
    /// Opt-in flag for the bias tee power-on check.
    /// </summary>
    private const string BiasTeeOnFlag = "--biastee-on";

    /// <summary>
    /// Friendly name the device under test is opened under.
    /// </summary>
    private const string DeviceName = "dut";

    /// <summary>
    /// Tolerance for comparing gains in dB. The device works in tenths of a dB, so anything
    /// well below 0.1 separates two adjacent steps without tripping on float representation.
    /// </summary>
    private const double GainTolerance = 0.001;

    /// <summary>
    /// Entry point. Opens the first device and runs every applicable check against it.
    /// </summary>
    /// <param name="args">Command line arguments; see <see cref="BiasTeeOnFlag"/>.</param>
    /// <returns>
    /// <see cref="ExitSuccess"/>, <see cref="ExitFailure"/>, or <see cref="ExitNoDevice"/>.
    /// </returns>
    public static int Main(string[] args)
    {
        bool biasTeeOn = args.Contains(BiasTeeOnFlag);

        // The device chatter would interleave with the check results and make them hard to
        // read, and it is not what this tool is verifying.
        RtlSdrDeviceManager.SuppressLibraryConsoleOutput = true;
        RtlSdrDeviceManager manager = RtlSdrDeviceManager.Instance;

        Console.WriteLine("RtlSdrManager hardware verification");
        Console.WriteLine($"Devices found: {manager.CountDevices}");
        foreach (KeyValuePair<uint, DeviceInfo> entry in manager.Devices)
        {
            Console.WriteLine($"  [{entry.Key}] {entry.Value.Manufacturer} {entry.Value.ProductType} " +
                              $"serial={entry.Value.Serial}");
        }

        Console.WriteLine();

        if (manager.CountDevices == 0)
        {
            Console.WriteLine("No devices attached. Nothing to verify.");
            return ExitNoDevice;
        }

        var report = new VerificationReport();

        manager.OpenManagedDevice(0, DeviceName);
        try
        {
            RtlSdrManagedDevice device = manager[DeviceName];

            // Read the tuner once: several checks only apply to particular tuners, and the
            // getter queries the device on every access.
            TunerTypes tuner = device.TunerType;

            Console.WriteLine($"Device under test: index 0, tuner = {tuner}");
            Console.WriteLine();

            VerifyTunerGain(device, tuner, report);
            Console.WriteLine();
            VerifyBiasTeeGpio(device, tuner, report, biasTeeOn);
        }
        finally
        {
            manager.CloseManagedDevice(DeviceName);
        }

        Console.WriteLine();
        report.PrintSummary();

        return report.HasFailures ? ExitFailure : ExitSuccess;
    }

    /// <summary>
    /// Verify tuner gain behavior, including the steps the R820T and R828D report.
    /// </summary>
    /// <param name="device">The device under test, which this method switches to manual gain mode.</param>
    /// <param name="tuner">Tuner type of the device under test.</param>
    /// <param name="report">Report collecting the outcomes.</param>
    /// <remarks>
    /// The R82xx family is singled out because its lowest step is <c>0.0</c> dB, a value this
    /// library once treated as an error marker. The checks below pin that down from both
    /// sides: the step is offered, accepted, and reads back.
    /// <para>
    /// These checks switch the tuner to manual gain control and move the gain around, so the
    /// original gain mode is captured on entry and restored on the way out. Without that the
    /// device would be left in manual mode at maximum gain, which is a poor state to hand to
    /// whatever the operator runs next.
    /// </para>
    /// </remarks>
    private static void VerifyTunerGain(RtlSdrManagedDevice device, TunerTypes tuner, VerificationReport report)
    {
        Console.WriteLine("Tuner gain");

        bool isR82xx = tuner is TunerTypes.R820T or TunerTypes.R828D;

        // Captured before anything is changed, and restored in the finally below.
        TunerGainModes originalGainMode = device.TunerGainMode;

        try
        {
            VerifyTunerGainCore(device, isR82xx, tuner, report);
        }
        finally
        {
            RestoreGainMode(device, originalGainMode);
        }
    }

    /// <summary>
    /// The tuner gain checks themselves, with the caller owning state restoration.
    /// </summary>
    /// <param name="device">The device under test, switched to manual gain mode here.</param>
    /// <param name="isR82xx">True when the tuner is an R820T or R828D.</param>
    /// <param name="tuner">Tuner type, used only to explain a skip.</param>
    /// <param name="report">Report collecting the outcomes.</param>
    private static void VerifyTunerGainCore(RtlSdrManagedDevice device, bool isR82xx,
        TunerTypes tuner, VerificationReport report)
    {
        // Manual mode is a precondition for every gain check: the property refuses to work
        // while the tuner runs its own gain control.
        device.TunerGainMode = TunerGainModes.Manual;

        report.Check("reading TunerGain before setting it throws InvalidOperationException",
            () => VerificationReport.Throws<InvalidOperationException>(() => _ = device.TunerGain),
            "InvalidOperationException; it was RtlSdrLibraryExecutionException before 0.8.0");

        List<double> gains = device.SupportedTunerGains;
        Console.WriteLine($"        SupportedTunerGains.Count = {gains.Count}");
        Console.WriteLine($"        min = {gains.Min()} dB, max = {gains.Max()} dB");

        if (isR82xx)
        {
            report.Check("the R82xx gain table is reported in full",
                () => gains.Count == 29,
                "29 entries; it was 28 before 0.8.0, with 0.0 filtered out");

            report.Check("0.0 dB is present in SupportedTunerGains",
                () => gains.Contains(0.0),
                "the list contains 0.0");

            report.Check("TunerGain = 0.0 is accepted",
                () =>
                {
                    device.TunerGain = 0.0;
                    return true;
                },
                "no exception; it threw ArgumentOutOfRangeException before 0.8.0");

            report.Check("TunerGain reads back 0.0 after setting it",
                () => Math.Abs(device.TunerGain) < GainTolerance,
                "0.0; it threw RtlSdrLibraryExecutionException before 0.8.0");

            report.Check("SetMinimumTunerGain selects 0.0 dB",
                () =>
                {
                    device.SetMinimumTunerGain();
                    return Math.Abs(device.TunerGain) < GainTolerance;
                },
                "0.0 dB; it selected 0.9 dB before 0.8.0");
        }
        else
        {
            report.Skip("R82xx gain table checks", $"the tuner is {tuner}, not R820T or R828D");
        }

        // Applies to every tuner with gain control: guards against the fix for the 0.0 dB
        // case having broken the ordinary path.
        report.Check("SetMaximumTunerGain round-trips the highest supported gain",
            () =>
            {
                device.SetMaximumTunerGain();
                return Math.Abs(device.TunerGain - gains.Max()) < GainTolerance;
            },
            $"{gains.Max()} dB");

        // 49.5 dB sits between two real R82xx steps, so it exercises rejection without
        // depending on the table's exact contents.
        report.Check("an unsupported gain step is still rejected",
            () => VerificationReport.Throws<ArgumentOutOfRangeException>(() => device.TunerGain = 49.5),
            "ArgumentOutOfRangeException, because 49.5 dB is not a supported step");
    }

    /// <summary>
    /// Put the tuner back under the gain control it was using before the checks ran.
    /// </summary>
    /// <param name="device">The device under test.</param>
    /// <param name="originalGainMode">The gain mode captured before the checks ran.</param>
    /// <remarks>
    /// Best effort by design: a failure to restore must not mask the check results, which are
    /// the point of the run. Restoring the mode is enough, because a gain value only applies
    /// in manual mode, and the tuner picks its own the moment automatic control resumes.
    /// </remarks>
    private static void RestoreGainMode(RtlSdrManagedDevice device, TunerGainModes originalGainMode)
    {
        try
        {
            device.TunerGainMode = originalGainMode;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  WARN  could not restore the tuner gain mode to " +
                              $"{originalGainMode}: {ex.Message}");
        }
    }

    /// <summary>
    /// Verify bias tee GPIO behavior: pin validation, and that no tuner is refused.
    /// </summary>
    /// <param name="device">The device under test.</param>
    /// <param name="tuner">Tuner type of the device under test.</param>
    /// <param name="report">Report collecting the outcomes.</param>
    /// <param name="biasTeeOn">
    /// When true, briefly enables the bias tee so the feed voltage can be metered. Only pass
    /// this with the antenna disconnected.
    /// </param>
    /// <remarks>
    /// The bias tee control pins belong to the demodulator rather than the tuner, so these
    /// calls should succeed on any device. That is the claim this method exists to check;
    /// note it cannot be proven on an R820T, which the removed restriction already allowed.
    /// <para>
    /// Only pin 0 is ever driven, and only to <see cref="BiasTeeModes.Disabled"/>. Setting a
    /// pin also switches it to output mode, and nothing resets that when the device is closed,
    /// so probing an unrelated pin would leave it reconfigured with no way to undo it. Pin 0
    /// is the pin the bias tee already uses, and driving it low is its safe state. It is also
    /// sufficient: the restriction this checks for rejected every pin on the wrong tuner, so
    /// one accepted call disproves it.
    /// </para>
    /// </remarks>
    private static void VerifyBiasTeeGpio(RtlSdrManagedDevice device, TunerTypes tuner,
        VerificationReport report, bool biasTeeOn)
    {
        Console.WriteLine("Bias tee GPIO");

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

        if (biasTeeOn)
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
