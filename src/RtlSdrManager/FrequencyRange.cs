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

namespace RtlSdrManager;

/// <summary>
/// A closed range of frequencies, inclusive at both ends.
/// </summary>
/// <remarks>
/// Comparisons are made in whole Hertz, so a bound such as 948.6 MHz is exact rather than
/// subject to floating point rounding.
/// <para>
/// One use is describing what a tuner can receive; see
/// <see cref="Hardware.TunerCapabilities"/>.
/// </para>
/// <para>
/// The default value of this type is an empty-looking range from 0 Hz to 0 Hz, which is not
/// meaningful as a tuner range. Obtain instances from <see cref="Hardware.TunerCapabilities"/>
/// rather than default-constructing them.
/// </para>
/// </remarks>
public readonly record struct FrequencyRange
{
    /// <summary>
    /// Create a frequency range.
    /// </summary>
    /// <param name="minimum">Lowest frequency in the range, inclusive.</param>
    /// <param name="maximum">Highest frequency in the range, inclusive.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when maximum is below minimum.</exception>
    public FrequencyRange(Frequency minimum, Frequency maximum)
    {
        if (maximum.Hz < minimum.Hz)
        {
            throw new ArgumentOutOfRangeException(nameof(maximum), maximum,
                $"The maximum frequency must not be below the minimum ({minimum}).");
        }

        Minimum = minimum;
        Maximum = maximum;
    }

    /// <summary>
    /// Lowest frequency in the range, inclusive.
    /// </summary>
    public Frequency Minimum { get; }

    /// <summary>
    /// Highest frequency in the range, inclusive.
    /// </summary>
    public Frequency Maximum { get; }

    /// <summary>
    /// Create a frequency range from Megahertz bounds.
    /// </summary>
    /// <param name="minimumMHz">Lowest frequency in the range, in MHz, inclusive.</param>
    /// <param name="maximumMHz">Highest frequency in the range, in MHz, inclusive.</param>
    /// <returns>The frequency range.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when maximum is below minimum.</exception>
    /// <remarks>
    /// The bounds are converted to whole Hertz once, here, so later membership tests are
    /// integer comparisons.
    /// </remarks>
    public static FrequencyRange FromMHz(double minimumMHz, double maximumMHz) =>
        new(Frequency.FromMHz(minimumMHz), Frequency.FromMHz(maximumMHz));

    /// <summary>
    /// Decide whether a frequency falls inside this range.
    /// </summary>
    /// <param name="frequency">The frequency to test.</param>
    /// <returns>True when the frequency is within the range, including its end points.</returns>
    public bool Contains(Frequency frequency) =>
        frequency.Hz >= Minimum.Hz && frequency.Hz <= Maximum.Hz;

    /// <summary>
    /// Returns a string representation of the range in Megahertz.
    /// </summary>
    /// <returns>String value of the frequency range, for example "24 - 1766 MHz".</returns>
    public override string ToString() => $"{Minimum.MHz} - {Maximum.MHz} MHz";
}
