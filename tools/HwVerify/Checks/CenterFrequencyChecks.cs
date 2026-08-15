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
using RtlSdrManager.Hardware;

namespace RtlSdrManager.Tools.HwVerify.Checks;

/// <summary>
/// Verify that the tuner's reported frequency coverage matches what the device accepts.
/// </summary>
/// <remarks>
/// The coverage table is unit tested on its own; what needs hardware is the agreement between
/// the table and the device. Tuning to a range boundary is the useful check, because that is
/// where a wrong table shows up first.
/// </remarks>
internal sealed class CenterFrequencyChecks : IHardwareCheck
{
    /// <inheritdoc />
    public string Title => "Center frequency";

    /// <inheritdoc />
    public void Run(RtlSdrManagedDevice device, VerificationReport report)
    {
        TunerTypes tuner = device.TunerType;

        IReadOnlyList<FrequencyRange> ranges = device.SupportedFrequencyRanges;
        Console.WriteLine($"        SupportedFrequencyRanges = {string.Join(" and ", ranges)}");

        report.Check("the tuner reports at least one frequency range",
            () => ranges.Count > 0,
            "a non-empty range list from SupportedFrequencyRanges");

        if (ranges.Count == 0)
        {
            return;
        }

        // The reported bounds must be reachable on the device, otherwise the table is wrong.
        Frequency lowest = ranges[0].Minimum;
        Frequency highest = ranges[^1].Maximum;

        report.Check($"the device tunes to its lowest reported frequency ({lowest.MHz} MHz)",
            () =>
            {
                device.CenterFrequency = lowest;
                return true;
            },
            "no exception; a failure here means the table claims more coverage than the tuner has");

        report.Check($"the device tunes to its highest reported frequency ({highest.MHz} MHz)",
            () =>
            {
                device.CenterFrequency = highest;
                return true;
            },
            "no exception; a failure here means the table claims more coverage than the tuner has");

        report.Check("a frequency below the reported range is rejected without touching the device",
            () => VerificationReport.Throws<ArgumentOutOfRangeException>(
                () => device.CenterFrequency = Frequency.FromHz(lowest.Hz - 1)),
            "ArgumentOutOfRangeException naming the supported ranges");

        if (TunerCapabilities.GetUnreliableRanges(tuner).Count == 0)
        {
            report.Skip("a frequency in a known unreliable range reports a useful error",
                $"the {tuner} has no range with device-dependent behavior; this needs an E4000");
        }
    }
}
