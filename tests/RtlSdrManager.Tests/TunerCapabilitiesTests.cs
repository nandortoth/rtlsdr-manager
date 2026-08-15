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
using Xunit;

namespace RtlSdrManager.Tests;

/// <summary>
/// Unit tests for the tuner frequency coverage table.
/// The figures come from http://osmocom.org/projects/sdr/wiki/rtl-sdr, and the tests are
/// deliberately concentrated on the boundaries, which is where the previous hand-written
/// range checks went wrong.
/// </summary>
public class TunerCapabilitiesTests
{
    /// <summary>Smallest step either side of a boundary, in Hertz.</summary>
    private const uint OneHz = 1;

    // ---------------------------------------------------------------- hard limits

    [Theory]
    [InlineData(TunerTypes.E4000, 52, 2200)]
    [InlineData(TunerTypes.R820T, 24, 1766)]
    [InlineData(TunerTypes.R828D, 24, 1766)]
    [InlineData(TunerTypes.FC0012, 22, 948.6)]
    [InlineData(TunerTypes.FC0013, 22, 1100)]
    public void SingleRangeTuners_AcceptTheirBoundaries(TunerTypes tuner, double minMHz, double maxMHz)
    {
        Assert.True(TunerCapabilities.IsTunable(tuner, Frequency.FromMHz(minMHz)));
        Assert.True(TunerCapabilities.IsTunable(tuner, Frequency.FromMHz(maxMHz)));
    }

    [Theory]
    [InlineData(TunerTypes.E4000, 52, 2200)]
    [InlineData(TunerTypes.R820T, 24, 1766)]
    [InlineData(TunerTypes.R828D, 24, 1766)]
    [InlineData(TunerTypes.FC0012, 22, 948.6)]
    [InlineData(TunerTypes.FC0013, 22, 1100)]
    public void SingleRangeTuners_RejectOneHzOutsideTheirBoundaries(TunerTypes tuner, double minMHz, double maxMHz)
    {
        Frequency justBelow = Frequency.FromHz(Frequency.FromMHz(minMHz).Hz - OneHz);
        Frequency justAbove = Frequency.FromHz(Frequency.FromMHz(maxMHz).Hz + OneHz);

        Assert.False(TunerCapabilities.IsTunable(tuner, justBelow));
        Assert.False(TunerCapabilities.IsTunable(tuner, justAbove));
    }

    [Fact]
    public void Fc0012_AcceptsItsFractionalUpperBound()
    {
        // 948.6 MHz is not exactly representable as a double. The table converts to whole
        // Hertz once, so the comparison is integral and the bound is exact.
        Assert.Equal(948_600_000u, Frequency.FromMHz(948.6).Hz);
        Assert.True(TunerCapabilities.IsTunable(TunerTypes.FC0012, Frequency.FromMHz(948.6)));
        Assert.False(TunerCapabilities.IsTunable(TunerTypes.FC0012, Frequency.FromHz(948_600_001u)));
    }

    // ---------------------------------------------------------------- FC2580 fixed gap

    [Theory]
    [InlineData(146)]       // first band, lower bound
    [InlineData(308)]       // first band, upper bound
    [InlineData(438)]       // second band, lower bound
    [InlineData(924)]       // second band, upper bound
    public void Fc2580_AcceptsBothBandEdges(double mhz) =>
        Assert.True(TunerCapabilities.IsTunable(TunerTypes.FC2580, Frequency.FromMHz(mhz)));

    [Fact]
    public void Fc2580_RejectsTheGapBetweenItsBands()
    {
        // The FC2580 gap is documented as fixed, so unlike the E4000 it stays a hard limit.
        Assert.False(TunerCapabilities.IsTunable(TunerTypes.FC2580, Frequency.FromHz(308_000_001u)));
        Assert.False(TunerCapabilities.IsTunable(TunerTypes.FC2580, Frequency.FromMHz(400)));
        Assert.False(TunerCapabilities.IsTunable(TunerTypes.FC2580, Frequency.FromHz(437_999_999u)));
    }

    [Fact]
    public void Fc2580_ReportsTwoRanges() =>
        Assert.Equal(2, TunerCapabilities.GetTunableRanges(TunerTypes.FC2580).Count);

    // ---------------------------------------------------------------- E4000 varying gap

    [Theory]
    [InlineData(1100)]      // regression: rejected before 0.8.0 by a >= comparison
    [InlineData(1250)]      // regression: rejected before 0.8.0 by a <= comparison
    [InlineData(1150)]      // inside the gap, now the device's decision rather than ours
    public void E4000_TreatsItsVaryingGapAsTunable(double mhz) =>
        Assert.True(TunerCapabilities.IsTunable(TunerTypes.E4000, Frequency.FromMHz(mhz)));

    [Fact]
    public void E4000_ReportsOneContinuousRange() =>
        Assert.Single(TunerCapabilities.GetTunableRanges(TunerTypes.E4000));

    [Theory]
    [InlineData(1100)]
    [InlineData(1150)]
    [InlineData(1250)]
    public void E4000_FlagsItsGapAsUnreliable(double mhz) =>
        Assert.True(TunerCapabilities.IsUnreliable(TunerTypes.E4000, Frequency.FromMHz(mhz)));

    [Theory]
    [InlineData(600)]
    [InlineData(1099)]
    [InlineData(1251)]
    public void E4000_DoesNotFlagFrequenciesOutsideItsGap(double mhz) =>
        Assert.False(TunerCapabilities.IsUnreliable(TunerTypes.E4000, Frequency.FromMHz(mhz)));

    [Fact]
    public void TunersWithoutAVaryingGap_ReportNothingUnreliable()
    {
        Assert.Empty(TunerCapabilities.GetUnreliableRanges(TunerTypes.R820T));
        Assert.Empty(TunerCapabilities.GetUnreliableRanges(TunerTypes.FC2580));
        Assert.False(TunerCapabilities.IsUnreliable(TunerTypes.R820T, Frequency.FromMHz(1150)));
    }

    // ---------------------------------------------------------------- unknown tuner

    [Fact]
    public void UnknownTuner_HasNoCoverage()
    {
        Assert.Empty(TunerCapabilities.GetTunableRanges(TunerTypes.Unknown));
        Assert.False(TunerCapabilities.IsTunable(TunerTypes.Unknown, Frequency.FromMHz(100)));
        Assert.Equal("no known frequency range", TunerCapabilities.DescribeTunableRanges(TunerTypes.Unknown));
    }

    // ---------------------------------------------------------------- descriptions

    [Fact]
    public void Descriptions_ReadAsFrequencyRanges()
    {
        Assert.Equal("24 - 1766 MHz", TunerCapabilities.DescribeTunableRanges(TunerTypes.R820T));
        Assert.Equal("146 - 308 MHz and 438 - 924 MHz",
            TunerCapabilities.DescribeTunableRanges(TunerTypes.FC2580));
    }

    // ---------------------------------------------------------------- FrequencyRange

    [Fact]
    public void FrequencyRange_IsInclusiveAtBothEnds()
    {
        var range = FrequencyRange.FromMHz(100, 200);

        Assert.True(range.Contains(Frequency.FromMHz(100)));
        Assert.True(range.Contains(Frequency.FromMHz(200)));
        Assert.False(range.Contains(Frequency.FromHz(99_999_999u)));
        Assert.False(range.Contains(Frequency.FromHz(200_000_001u)));
    }

    [Fact]
    public void FrequencyRange_RejectsAnInvertedRange()
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => FrequencyRange.FromMHz(200, 100));

        Assert.Equal("maximum", exception.ParamName);
    }

    [Fact]
    public void FrequencyRange_AllowsASinglePointRange()
    {
        var range = FrequencyRange.FromMHz(100, 100);

        Assert.True(range.Contains(Frequency.FromMHz(100)));
        Assert.False(range.Contains(Frequency.FromHz(100_000_001u)));
    }

    [Fact]
    public void EveryKnownTuner_HasCoverage()
    {
        // Guards against a tuner being added to the enum without a table entry, which would
        // silently make every frequency invalid for it.
        foreach (TunerTypes tuner in Enum.GetValues<TunerTypes>())
        {
            if (tuner == TunerTypes.Unknown)
            {
                continue;
            }

            IReadOnlyList<FrequencyRange> ranges = TunerCapabilities.GetTunableRanges(tuner);
            Assert.True(ranges.Count > 0, $"{tuner} has no frequency coverage defined.");
        }
    }
}
