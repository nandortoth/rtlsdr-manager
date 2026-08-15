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

using RtlSdrManager.Hardware;
using Xunit;

namespace RtlSdrManager.Tests;

/// <summary>
/// Unit tests for the demodulator's direct sampling coverage.
/// The bound is half the crystal frequency, which is where the 22 bit register holding the
/// value runs out. Beyond it the device receives a different frequency without reporting an
/// error, so the boundary is the whole point of these tests.
/// </summary>
public class DemodulatorCapabilitiesTests
{
    /// <summary>The crystal frequency fitted to essentially every RTL-SDR device.</summary>
    private const double StandardCrystalMHz = 28.8;

    [Fact]
    public void StandardCrystal_ReachesHalfItsFrequency()
    {
        FrequencyRange range = DemodulatorCapabilities.GetDirectSamplingRange(
            Frequency.FromMHz(StandardCrystalMHz));

        Assert.Equal(0u, range.Minimum.Hz);
        Assert.Equal(14_400_000u, range.Maximum.Hz);
    }

    [Fact]
    public void TheUpperBoundIsReachable()
    {
        FrequencyRange range = DemodulatorCapabilities.GetDirectSamplingRange(
            Frequency.FromMHz(StandardCrystalMHz));

        // 14.4 MHz encodes to the most negative value the register can hold, so it is the
        // last frequency that still means what it says.
        Assert.True(range.Contains(Frequency.FromMHz(14.4)));
        Assert.False(range.Contains(Frequency.FromHz(14_400_001u)));
    }

    [Fact]
    public void DirectCurrentIsReachable()
    {
        FrequencyRange range = DemodulatorCapabilities.GetDirectSamplingRange(
            Frequency.FromMHz(StandardCrystalMHz));

        // Turning direct sampling on leaves the device here until a frequency is chosen.
        Assert.True(range.Contains(Frequency.FromHz(0u)));
    }

    [Theory]
    [InlineData(0.03)]      // long wave
    [InlineData(1)]         // medium wave
    [InlineData(7.1)]       // 40 m amateur band
    [InlineData(14.2)]      // 20 m amateur band, just inside the limit
    public void HighFrequencyBandsBelowTheLimitAreReachable(double mhz)
    {
        FrequencyRange range = DemodulatorCapabilities.GetDirectSamplingRange(
            Frequency.FromMHz(StandardCrystalMHz));

        Assert.True(range.Contains(Frequency.FromMHz(mhz)));
    }

    [Theory]
    [InlineData(15)]        // above the first Nyquist zone
    [InlineData(21)]        // 15 m amateur band, receivable only by aliasing
    [InlineData(28.8)]      // the crystal frequency itself
    public void FrequenciesAboveTheLimitAreNotReachable(double mhz)
    {
        FrequencyRange range = DemodulatorCapabilities.GetDirectSamplingRange(
            Frequency.FromMHz(StandardCrystalMHz));

        Assert.False(range.Contains(Frequency.FromMHz(mhz)));
    }

    [Fact]
    public void TheBoundFollowsTheCrystalRatherThanBeingFixed()
    {
        // The crystal is adjustable, and frequency correction is applied to it, so the limit
        // has to be derived from the device rather than assumed to be 14.4 MHz.
        FrequencyRange slower = DemodulatorCapabilities.GetDirectSamplingRange(
            Frequency.FromMHz(24));

        Assert.Equal(12_000_000u, slower.Maximum.Hz);
        Assert.True(slower.Contains(Frequency.FromMHz(12)));
        Assert.False(slower.Contains(Frequency.FromMHz(14.4)));
    }

    [Fact]
    public void AnOddCrystalFrequencyRoundsDown()
    {
        // Integer division truncates, which errs towards refusing a frequency that would have
        // been the last usable one. That is the safe direction: the alternative is a value
        // the register cannot hold.
        FrequencyRange range = DemodulatorCapabilities.GetDirectSamplingRange(
            Frequency.FromHz(28_800_001u));

        Assert.Equal(14_400_000u, range.Maximum.Hz);
    }
}
