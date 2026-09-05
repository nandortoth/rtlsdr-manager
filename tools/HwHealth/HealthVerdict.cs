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

namespace RtlSdrManager.Tools.HwHealth;

/// <summary>
/// Turns a measurement into a verdict and the line that explains it.
/// </summary>
/// <param name="Verdict">What was concluded.</param>
/// <param name="Detail">The supporting detail, printed unless the caller asked for quiet.</param>
/// <remarks>
/// Deliberately free of hardware and of the console: it takes a <see cref="ProbeResult"/> and
/// returns text. That keeps the one judgment call in this tool, the throughput threshold, in a
/// place where it can be read and changed without reasoning about USB.
/// </remarks>
internal sealed record HealthVerdict(Verdict Verdict, string Detail)
{
    /// <summary>
    /// Fraction of the requested sample rate the device must actually deliver.
    /// </summary>
    /// <remarks>
    /// A healthy device measures close to the full rate and a degraded one a small fraction,
    /// so the threshold sits in a wide gap rather than on a boundary.
    /// <para>
    /// It is not calibrated. Every degraded device seen so far went from near the full rate to
    /// delivering nothing, without passing through this fraction, so what has actually caught
    /// degradation is the check above rather than this one. The value has been exercised only
    /// by deliberately setting it above 1 to make healthy hardware trip it. If a device is ever
    /// measured part way down, that reading is worth more than this guess.
    /// </para>
    /// </remarks>
    private const double MinimumThroughputFraction = 0.5;

    /// <summary>
    /// The verdict for a device that was never there.
    /// </summary>
    public static HealthVerdict NoDevice { get; } =
        new(Verdict.NoDevice, "nothing attached, so health could not be measured");

    /// <summary>
    /// Judge a measurement.
    /// </summary>
    /// <param name="result">What the probe observed.</param>
    /// <returns>The verdict.</returns>
    /// <remarks>
    /// Ordered so the most specific cause is reported first: an error the reading recorded
    /// explains everything after it, and silence explains a throughput of zero. Reporting
    /// "not sustaining delivery" for a device that faulted would be true and useless.
    /// </remarks>
    public static HealthVerdict For(ProbeResult result)
    {
        if (result.Error is { } error)
        {
            return new HealthVerdict(Verdict.Unhealthy,
                $"the reading recorded an error: {error.GetType().Name}: {error.Message}");
        }

        if (!result.DeliveryStarted)
        {
            return new HealthVerdict(Verdict.Unhealthy,
                $"no samples arrived within {result.Window.TotalSeconds:0} s");
        }

        if (!result.CounterInOrder)
        {
            return new HealthVerdict(Verdict.Unhealthy,
                $"samples arrived out of order; {Describe(result)}");
        }

        if (result.ThroughputFraction < MinimumThroughputFraction)
        {
            return new HealthVerdict(Verdict.Unhealthy,
                $"not sustaining delivery: {Describe(result)}, below the " +
                $"{MinimumThroughputFraction:P0} the device was asked for");
        }

        return new HealthVerdict(Verdict.Healthy, $"{Describe(result)}, counter in order");
    }

    /// <summary>
    /// Render the verdict for the console.
    /// </summary>
    /// <param name="quiet">Whether to print the verdict alone, for use in a script.</param>
    /// <returns>The line to print.</returns>
    public string Format(bool quiet) => quiet ? Label : $"{Label,-9}  {Detail}";

    /// <summary>
    /// The word a run is judged by, and the first thing anyone reads.
    /// </summary>
    private string Label => Verdict switch
    {
        Verdict.Healthy => "HEALTHY",
        Verdict.NoDevice => "NO DEVICE",
        _ => "UNHEALTHY"
    };

    /// <summary>
    /// The measurement, in the form that makes a degraded device obvious.
    /// </summary>
    /// <param name="result">What the probe observed.</param>
    /// <returns>The description.</returns>
    private static string Describe(ProbeResult result) =>
        $"{result.DeliveredSamples / 1_000_000.0:0.0} MS in " +
        $"{result.Window.TotalSeconds:0.0} s ({result.ThroughputFraction:P1} of " +
        $"{result.RequestedSampleRateHz / 1_000_000.0:0.000} MSPS), " +
        $"{result.DroppedSamples} dropped";
}
