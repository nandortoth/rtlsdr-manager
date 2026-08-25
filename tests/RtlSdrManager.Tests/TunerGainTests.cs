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
using Xunit;

namespace RtlSdrManager.Tests;

/// <summary>
/// Unit tests for the tuner gain table logic.
/// The tables below are copied verbatim from the gain tables the native library reports,
/// as of rtl-sdr v2.0.3, so the tests exercise the real hardware data without needing a
/// device. The version matters: if upstream ever revises a table, these fixtures have to be
/// re-checked against it rather than trusted.
/// </summary>
public class TunerGainTests
{
    /// <summary>
    /// R820T/R828D gains in tenths of a dB. Note the leading 0: it is the tuner's
    /// minimum-gain step (LNA index 0, mixer index 0), not a placeholder.
    /// </summary>
    private static readonly int[] R82xxGains =
    [
        0, 9, 14, 27, 37, 77, 87, 125, 144, 157,
        166, 197, 207, 229, 254, 280, 297, 328,
        338, 364, 372, 386, 402, 421, 434, 439,
        445, 480, 496
    ];

    /// <summary>Elonics E4000 gains in tenths of a dB, including a negative step.</summary>
    private static readonly int[] E4000Gains =
        [-10, 15, 40, 65, 90, 115, 140, 165, 190, 215, 240, 290, 340, 420];

    /// <summary>Fitipower FC0012 gains in tenths of a dB.</summary>
    private static readonly int[] Fc0012Gains = [-99, -40, 71, 179, 192];

    /// <summary>
    /// The placeholder librtlsdr reports for the FC2580 and for an unknown tuner,
    /// commented "no gain values" upstream.
    /// </summary>
    private static readonly int[] NoGainPlaceholder = [0];

    [Fact]
    public void Placeholder_IsRecognizedAsNoGainValues()
    {
        Assert.True(RtlSdrManagedDevice.HasNoGainValues(NoGainPlaceholder));
        Assert.Empty(RtlSdrManagedDevice.ToSupportedGains(NoGainPlaceholder));
    }

    [Fact]
    public void RealTables_AreNotMistakenForThePlaceholder()
    {
        Assert.False(RtlSdrManagedDevice.HasNoGainValues(R82xxGains));
        Assert.False(RtlSdrManagedDevice.HasNoGainValues(E4000Gains));
        Assert.False(RtlSdrManagedDevice.HasNoGainValues(Fc0012Gains));
    }

    [Fact]
    public void SingleEntryNonZeroTable_IsNotThePlaceholder()
    {
        // Only a lone zero means "no gain control"; a lone real gain is still a gain.
        Assert.False(RtlSdrManagedDevice.HasNoGainValues([115]));
        Assert.Equal([11.5], RtlSdrManagedDevice.ToSupportedGains([115]));
    }

    [Fact]
    public void R82xx_ExposesAllGains_IncludingZero()
    {
        List<double> gains = RtlSdrManagedDevice.ToSupportedGains(R82xxGains);

        // Regression: the 0.0 dB step used to be filtered out, exposing only 28 of 29.
        Assert.Equal(29, gains.Count);
        Assert.Contains(0.0, gains);
        Assert.Equal(0.0, gains.Min());
        Assert.Equal(49.6, gains.Max());
    }

    [Fact]
    public void E4000_PreservesNegativeGain()
    {
        List<double> gains = RtlSdrManagedDevice.ToSupportedGains(E4000Gains);

        Assert.Equal(14, gains.Count);
        Assert.Equal(-1.0, gains.Min());
        Assert.Equal(42.0, gains.Max());
    }

    [Fact]
    public void TenthsOfDb_AreConvertedToDb() =>
        Assert.Equal([-9.9, -4.0, 7.1, 17.9, 19.2], RtlSdrManagedDevice.ToSupportedGains(Fc0012Gains));

    [Theory]
    [InlineData(0)]     // regression: 0.0 dB used to be rejected as unsupported
    [InlineData(9)]
    [InlineData(496)]
    public void R82xx_SupportedSteps_AreAccepted(int gainTenths) =>
        Assert.True(RtlSdrManagedDevice.IsSupportedGain(R82xxGains, gainTenths));

    [Theory]
    [InlineData(1)]
    [InlineData(495)]   // 49.5 dB is not a step; 49.6 dB is
    [InlineData(-10)]   // an E4000 step, not a R82xx one
    public void R82xx_UnsupportedSteps_AreRejected(int gainTenths) =>
        Assert.False(RtlSdrManagedDevice.IsSupportedGain(R82xxGains, gainTenths));

    [Fact]
    public void E4000_NegativeStep_IsAccepted() =>
        Assert.True(RtlSdrManagedDevice.IsSupportedGain(E4000Gains, -10));

    [Fact]
    public void NoGainTuner_SupportsNothing_NotEvenZero()
    {
        Assert.False(RtlSdrManagedDevice.IsSupportedGain(NoGainPlaceholder, 0));
        Assert.False(RtlSdrManagedDevice.IsSupportedGain(NoGainPlaceholder, 115));
    }
}
