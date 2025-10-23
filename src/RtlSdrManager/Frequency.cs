// RTL-SDR Manager Library for .NET
// Copyright (C) 2018-2025 Nandor Toth <dev@nandortoth.com>
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
/// Immutable record representing a frequency value in Hertz with unit conversions and comparison support.
/// </summary>
/// <param name="Hz">Frequency value in Hertz.</param>
public readonly record struct Frequency(uint Hz) : IComparable<Frequency>, IComparable
{
    /// <summary>
    /// Gets the frequency in Kilohertz (KHz).
    /// </summary>
    public double KHz => Hz / 1_000.0;

    /// <summary>
    /// Gets the frequency in Megahertz (MHz).
    /// </summary>
    public double MHz => Hz / 1_000_000.0;

    /// <summary>
    /// Gets the frequency in Gigahertz (GHz).
    /// </summary>
    public double GHz => Hz / 1_000_000_000.0;

    /// <summary>
    /// Creates a frequency from Hertz.
    /// </summary>
    /// <param name="hz">Frequency in Hertz.</param>
    /// <returns>A new Frequency instance.</returns>
    public static Frequency FromHz(uint hz) => new(hz);

    /// <summary>
    /// Creates a frequency from Hertz.
    /// </summary>
    /// <param name="hz">Frequency in Hertz.</param>
    /// <returns>A new Frequency instance.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the value is negative or exceeds the maximum frequency.</exception>
    public static Frequency FromHz(double hz)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(hz);
        double rounded = Math.Round(hz);
        return rounded > uint.MaxValue ? throw new ArgumentOutOfRangeException(nameof(hz), hz,
            $"Frequency {hz} Hz exceeds maximum value.") : new Frequency((uint)rounded);
    }

    /// <summary>
    /// Creates a frequency from Kilohertz.
    /// </summary>
    /// <param name="khz">Frequency in Kilohertz.</param>
    /// <returns>A new Frequency instance.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the value is negative or exceeds the maximum frequency.</exception>
    public static Frequency FromKHz(double khz)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(khz);
        double hz = Math.Round(khz * 1_000.0);
        return hz > uint.MaxValue ?
            throw new ArgumentOutOfRangeException(nameof(khz), khz, $"Frequency {khz} KHz exceeds maximum value.") :
            new Frequency((uint)hz);
    }

    /// <summary>
    /// Creates a frequency from Megahertz.
    /// </summary>
    /// <param name="mhz">Frequency in Megahertz.</param>
    /// <returns>A new Frequency instance.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the value is negative or exceeds the maximum frequency.</exception>
    public static Frequency FromMHz(double mhz)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(mhz);
        double hz = Math.Round(mhz * 1_000_000.0);
        return hz > uint.MaxValue ?
            throw new ArgumentOutOfRangeException(nameof(mhz), mhz, $"Frequency {mhz} MHz exceeds maximum value.") :
            new Frequency((uint)hz);
    }

    /// <summary>
    /// Creates a frequency from Gigahertz.
    /// </summary>
    /// <param name="ghz">Frequency in Gigahertz.</param>
    /// <returns>A new Frequency instance.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the value is negative or exceeds the maximum frequency.</exception>
    public static Frequency FromGHz(double ghz)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(ghz);
        double hz = Math.Round(ghz * 1_000_000_000.0);
        return hz > uint.MaxValue ?
            throw new ArgumentOutOfRangeException(nameof(ghz), ghz, $"Frequency {ghz} GHz exceeds maximum value.") :
            new Frequency((uint)hz);
    }

    /// <summary>
    /// Compares this frequency to another frequency.
    /// </summary>
    /// <param name="other">The frequency to compare to.</param>
    /// <returns>A value indicating the relative order of the frequencies.</returns>
    public int CompareTo(Frequency other) => Hz.CompareTo(other.Hz);

    /// <summary>
    /// Compares this frequency to another object.
    /// </summary>
    /// <param name="obj">The object to compare to.</param>
    /// <returns>A value indicating the relative order of the objects.</returns>
    /// <exception cref="ArgumentException">Thrown when obj is not a Frequency.</exception>
    public int CompareTo(object? obj)
    {
        if (obj is null)
        {
            return 1;
        }

        return obj is not Frequency other ?
            throw new ArgumentException($"Object must be of type {nameof(Frequency)}.", nameof(obj)) :
            CompareTo(other);
    }

    /// <summary>
    /// Returns a string representation of the frequency in all units.
    /// </summary>
    public override string ToString() => $"{Hz} Hz; {KHz:F3} KHz; {MHz:F6} MHz; {GHz:F9} GHz";

    // Comparison operators
    public static bool operator <(Frequency left, Frequency right) => left.Hz < right.Hz;
    public static bool operator >(Frequency left, Frequency right) => left.Hz > right.Hz;
    public static bool operator <=(Frequency left, Frequency right) => left.Hz <= right.Hz;
    public static bool operator >=(Frequency left, Frequency right) => left.Hz >= right.Hz;

    // Arithmetic operators
    /// <summary>
    /// Adds two frequencies together.
    /// </summary>
    /// <exception cref="OverflowException">Thrown when the result exceeds the maximum frequency.</exception>
    public static Frequency operator +(Frequency left, Frequency right) =>
        new(checked(left.Hz + right.Hz));

    /// <summary>
    /// Subtracts one frequency from another.
    /// </summary>
    /// <exception cref="OverflowException">Thrown when the result would be negative.</exception>
    public static Frequency operator -(Frequency left, Frequency right) =>
        new Frequency(checked(left.Hz - right.Hz));

    /// <summary>
    /// Multiplies a frequency by a scalar value.
    /// </summary>
    public static Frequency operator *(Frequency frequency, double multiplier)
    {
        double result = Math.Round(frequency.Hz * multiplier);
        return result is < 0 or > uint.MaxValue
            ? throw new OverflowException($"Frequency multiplication result {result} Hz is out of range.")
            : new Frequency((uint)result);
    }

    /// <summary>
    /// Multiplies a frequency by a scalar value (commutative).
    /// </summary>
    public static Frequency operator *(double multiplier, Frequency frequency) => frequency * multiplier;

    /// <summary>
    /// Divides a frequency by a scalar value.
    /// </summary>
    public static Frequency operator /(Frequency frequency, double divisor)
    {
        if (divisor == 0)
        {
            throw new DivideByZeroException("Cannot divide frequency by zero.");
        }

        double result = Math.Round(frequency.Hz / divisor);
        return result < 0
            ? throw new OverflowException($"Frequency division result {result} Hz is out of range.")
            : new Frequency((uint)result);
    }
}
