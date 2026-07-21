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
using Xunit;

namespace RtlSdrManager.Tests;

/// <summary>
/// Unit tests for the <see cref="Frequency"/> value type.
/// </summary>
public class FrequencyTests
{
    [Fact]
    public void UnitConversions_AreConsistent()
    {
        var frequency = new Frequency(1_090_000_000);

        Assert.Equal(1_090_000_000u, frequency.Hz);
        Assert.Equal(1_090_000.0, frequency.KHz);
        Assert.Equal(1_090.0, frequency.MHz);
        Assert.Equal(1.09, frequency.GHz);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1_766_000_000)]
    [InlineData(uint.MaxValue)]
    public void FromHz_UInt_RoundTrips(uint hz) =>
        Assert.Equal(hz, Frequency.FromHz(hz).Hz);

    [Fact]
    public void FactoryMethods_ConvertUnitsCorrectly()
    {
        Assert.Equal(1_000u, Frequency.FromKHz(1).Hz);
        Assert.Equal(2_048_000u, Frequency.FromMHz(2.048).Hz);
        Assert.Equal(1_090_000_000u, Frequency.FromMHz(1090).Hz);
        Assert.Equal(1_500_000_000u, Frequency.FromGHz(1.5).Hz);
    }

    [Fact]
    public void FactoryMethods_RoundToNearestHz()
    {
        // 0.0000015 MHz = 1.5 Hz, rounds to 2 Hz (banker's rounding rounds .5 to even).
        Assert.Equal(2u, Frequency.FromHz(1.5).Hz);
        Assert.Equal(1u, Frequency.FromHz(1.4).Hz);
    }

    [Fact]
    public void FactoryMethods_RejectNegativeValues()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Frequency.FromHz(-1.0));
        Assert.Throws<ArgumentOutOfRangeException>(() => Frequency.FromKHz(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => Frequency.FromMHz(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => Frequency.FromGHz(-1));
    }

    [Fact]
    public void FactoryMethods_RejectValuesAboveUIntMax()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Frequency.FromHz((double)uint.MaxValue + 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => Frequency.FromGHz(5));
    }

    [Fact]
    public void ComparisonOperators_OrderByHz()
    {
        var lower = Frequency.FromMHz(100);
        var higher = Frequency.FromMHz(200);

        Assert.True(lower < higher);
        Assert.True(higher > lower);
        Assert.True(lower <= higher);
        Assert.True(lower <= Frequency.FromMHz(100));
        Assert.True(higher >= lower);
        Assert.False(lower > higher);
    }

    [Fact]
    public void CompareTo_MatchesHzOrdering()
    {
        var lower = Frequency.FromMHz(100);
        var higher = Frequency.FromMHz(200);

        Assert.True(lower.CompareTo(higher) < 0);
        Assert.True(higher.CompareTo(lower) > 0);
        Assert.Equal(0, lower.CompareTo(Frequency.FromMHz(100)));
        Assert.Equal(1, lower.CompareTo(null));
        Assert.Throws<ArgumentException>(() => lower.CompareTo("not a frequency"));
    }

    [Fact]
    public void Addition_And_Subtraction_Work()
    {
        Frequency result = Frequency.FromMHz(100) + Frequency.FromKHz(100);
        Assert.Equal(100_100_000u, result.Hz);

        Frequency difference = Frequency.FromMHz(100) - Frequency.FromKHz(100);
        Assert.Equal(99_900_000u, difference.Hz);
    }

    [Fact]
    public void Addition_Overflow_Throws()
    {
        var max = new Frequency(uint.MaxValue);
        Assert.Throws<OverflowException>(() => max + new Frequency(1));
    }

    [Fact]
    public void Subtraction_Underflow_Throws() =>
        Assert.Throws<OverflowException>(() => new Frequency(0) - new Frequency(1));

    [Fact]
    public void Multiplication_IsCommutative_AndChecksRange()
    {
        var frequency = Frequency.FromMHz(100);

        Assert.Equal(200_000_000u, (frequency * 2.0).Hz);
        Assert.Equal(200_000_000u, (2.0 * frequency).Hz);
        Assert.Throws<OverflowException>(() => new Frequency(uint.MaxValue) * 2.0);
        Assert.Throws<OverflowException>(() => frequency * -1.0);
    }

    [Fact]
    public void Division_Works_AndRejectsZeroDivisor()
    {
        Assert.Equal(50_000_000u, (Frequency.FromMHz(100) / 2.0).Hz);
        Assert.Throws<DivideByZeroException>(() => Frequency.FromMHz(100) / 0.0);
    }

    [Fact]
    public void ToString_ContainsAllUnits()
    {
        string text = Frequency.FromMHz(1090).ToString();

        Assert.Contains("Hz", text);
        Assert.Contains("KHz", text);
        Assert.Contains("MHz", text);
        Assert.Contains("GHz", text);
    }
}
