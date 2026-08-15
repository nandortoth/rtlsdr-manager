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

using System.Collections.Generic;
using System.Linq;

namespace RtlSdrManager.Hardware;

/// <summary>
/// Frequency coverage of the supported tuner chips.
/// </summary>
/// <remarks>
/// The figures come from the RTL-SDR hardware documentation at
/// http://osmocom.org/projects/sdr/wiki/rtl-sdr.
/// <para>
/// Two different things are described here, and the distinction matters. A
/// <em>tunable range</em> is a hard limit: the tuner cannot reach outside it, so a request
/// beyond it is rejected without troubling the device. An <em>unreliable range</em> is a
/// region inside the tunable range where tuning often fails, with boundaries that differ
/// between individual devices. Unreliable ranges are never used to reject a request, because
/// a fixed boundary would be wrong for any device whose real one sits elsewhere: the tuner is
/// asked to do the work and reports whether it managed. They serve only to explain a failure.
/// </para>
/// </remarks>
public static class TunerCapabilities
{
    /// <summary>
    /// Hard frequency limits per tuner. A tuner absent from this table has no known coverage.
    /// </summary>
    private static readonly Dictionary<TunerTypes, FrequencyRange[]> TunableRanges = new()
    {
        // Documented as 52 - 2200 MHz with a gap near 1100 - 1250 MHz. The gap is not a hard
        // limit and varies between devices, so it lives in UnreliableRanges instead.
        [TunerTypes.E4000] = [FrequencyRange.FromMHz(52, 2200)],
        [TunerTypes.R820T] = [FrequencyRange.FromMHz(24, 1766)],
        [TunerTypes.R828D] = [FrequencyRange.FromMHz(24, 1766)],
        [TunerTypes.FC0012] = [FrequencyRange.FromMHz(22, 948.6)],

        // FC0013B/C. The FC0013G has a separate L-band input, but it is unconnected on most
        // devices, so the practical coverage is the same.
        [TunerTypes.FC0013] = [FrequencyRange.FromMHz(22, 1100)],

        // Two separate bands. Unlike the E4000, this gap is documented as fixed, so it is a
        // hard limit rather than an unreliable range.
        [TunerTypes.FC2580] = [FrequencyRange.FromMHz(146, 308), FrequencyRange.FromMHz(438, 924)]
    };

    /// <summary>
    /// Regions inside a tunable range where tuning commonly fails on some devices.
    /// </summary>
    private static readonly Dictionary<TunerTypes, FrequencyRange[]> UnreliableRanges = new()
    {
        [TunerTypes.E4000] = [FrequencyRange.FromMHz(1100, 1250)]
    };

    /// <summary>
    /// Get the frequency ranges a tuner can reach.
    /// </summary>
    /// <param name="tuner">The tuner type.</param>
    /// <returns>
    /// The tuner's frequency ranges, ordered by frequency. Empty when the tuner is not
    /// recognized, in which case no frequency can be validated as supported.
    /// </returns>
    public static IReadOnlyList<FrequencyRange> GetTunableRanges(TunerTypes tuner) =>
        TunableRanges.TryGetValue(tuner, out FrequencyRange[]? ranges) ? ranges : [];

    /// <summary>
    /// Get the regions where this tuner is known to have trouble.
    /// </summary>
    /// <param name="tuner">The tuner type.</param>
    /// <returns>
    /// Regions inside the tuner's coverage where tuning often fails, or an empty list when the
    /// tuner has none. These are advisory: they never make a frequency invalid.
    /// </returns>
    public static IReadOnlyList<FrequencyRange> GetUnreliableRanges(TunerTypes tuner) =>
        UnreliableRanges.TryGetValue(tuner, out FrequencyRange[]? ranges) ? ranges : [];

    /// <summary>
    /// Decide whether a tuner can reach a frequency.
    /// </summary>
    /// <param name="tuner">The tuner type.</param>
    /// <param name="frequency">The frequency to test.</param>
    /// <returns>True when the frequency falls inside one of the tuner's ranges.</returns>
    /// <remarks>
    /// A true result means the frequency is within the tuner's published coverage, not that
    /// this particular device will lock to it; see <see cref="IsUnreliable"/>.
    /// </remarks>
    public static bool IsTunable(TunerTypes tuner, Frequency frequency) =>
        GetTunableRanges(tuner).Any(range => range.Contains(frequency));

    /// <summary>
    /// Decide whether a frequency falls in a region where this tuner commonly has trouble.
    /// </summary>
    /// <param name="tuner">The tuner type.</param>
    /// <param name="frequency">The frequency to test.</param>
    /// <returns>True when the frequency is inside a known unreliable region.</returns>
    public static bool IsUnreliable(TunerTypes tuner, Frequency frequency) =>
        GetUnreliableRanges(tuner).Any(range => range.Contains(frequency));

    /// <summary>
    /// Describe a tuner's coverage in a form suitable for an error message.
    /// </summary>
    /// <param name="tuner">The tuner type.</param>
    /// <returns>
    /// The ranges joined with "and", for example "146 - 308 MHz and 438 - 924 MHz", or a note
    /// that the coverage is unknown.
    /// </returns>
    public static string DescribeTunableRanges(TunerTypes tuner)
    {
        IReadOnlyList<FrequencyRange> ranges = GetTunableRanges(tuner);

        return ranges.Count == 0
            ? "no known frequency range"
            : string.Join(" and ", ranges.Select(range => range.ToString()));
    }
}
