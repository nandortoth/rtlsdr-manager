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
using RtlSdrManager.Hardware;
using RtlSdrManager.Modes;

namespace RtlSdrManager.Tools.HwVerify.Checks;

/// <summary>
/// Verify tuner gain behavior, including the steps the R820T and R828D report.
/// </summary>
/// <remarks>
/// The R82xx family is singled out because its lowest step is <c>0.0</c> dB, a value this
/// library once treated as an error marker. The checks below pin that down from both sides:
/// the step is offered, accepted, and reads back.
/// <para>
/// These checks switch the tuner to manual gain control and move the gain around, so the
/// original gain mode is captured on entry and restored on the way out. Without that the
/// device would be left in manual mode at maximum gain, which is a poor state to hand to
/// whatever the operator runs next.
/// </para>
/// </remarks>
internal sealed class TunerGainChecks : IHardwareCheck
{
    /// <summary>
    /// Tolerance for comparing gains in dB. The device works in tenths of a dB, so anything
    /// well below 0.1 separates two adjacent steps without tripping on float representation.
    /// </summary>
    private const double GainTolerance = 0.001;

    /// <inheritdoc />
    public string Title => "Tuner gain";

    /// <inheritdoc />
    public void Run(RtlSdrManagedDevice device, VerificationReport report)
    {
        // Captured before anything is changed, and restored in the finally below.
        TunerGainModes originalGainMode = device.TunerGainMode;

        try
        {
            RunCore(device, report);
        }
        finally
        {
            RestoreGainMode(device, originalGainMode);
        }
    }

    /// <summary>
    /// The gain checks themselves, with the caller owning state restoration.
    /// </summary>
    /// <param name="device">The device under test, switched to manual gain mode here.</param>
    /// <param name="report">Report collecting the outcomes.</param>
    private static void RunCore(RtlSdrManagedDevice device, VerificationReport report)
    {
        TunerTypes tuner = device.TunerType;
        bool isR82xx = tuner is TunerTypes.R820T or TunerTypes.R828D;

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
}
