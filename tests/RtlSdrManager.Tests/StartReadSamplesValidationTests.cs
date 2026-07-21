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
/// Unit tests for the StartReadSamplesAsync argument validation
/// (<see cref="RtlSdrManagedDevice.ValidateRequestedSamples"/>).
/// The validation is an internal static helper, so it is testable without hardware.
/// </summary>
public class StartReadSamplesValidationTests
{
    [Theory]
    [InlineData(256u)]        // 512 bytes, the smallest valid read
    [InlineData(16384u)]      // library default
    [InlineData(131072u)]     // used by the sample applications
    [InlineData(1048576u)]
    public void ValidCounts_Pass(uint requestedSamples) =>
        RtlSdrManagedDevice.ValidateRequestedSamples(requestedSamples);

    [Fact]
    public void Zero_IsRejected_WithParamName()
    {
        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => RtlSdrManagedDevice.ValidateRequestedSamples(0));

        Assert.Equal("requestedSamples", exception.ParamName);
        Assert.Equal(0u, exception.ActualValue);
    }

    [Theory]
    [InlineData(1u)]          // 2 bytes
    [InlineData(255u)]        // 510 bytes
    [InlineData(1000u)]       // 2000 bytes
    [InlineData(16385u)]      // default + 1
    public void CountsWithByteSizeNotMultipleOf512_AreRejected(uint requestedSamples)
    {
        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => RtlSdrManagedDevice.ValidateRequestedSamples(requestedSamples));

        Assert.Equal("requestedSamples", exception.ParamName);
    }

    [Fact]
    public void OversizedCount_IsRejected_BeforeByteSizeOverflow()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => RtlSdrManagedDevice.ValidateRequestedSamples((uint.MaxValue / 2) + 1));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => RtlSdrManagedDevice.ValidateRequestedSamples(uint.MaxValue));
    }

    [Fact]
    public void LargestValidCount_Passes()
    {
        // Largest count within range whose byte size is a multiple of 512:
        // uint.MaxValue / 2 = 2147483647, aligned down to a multiple of 256 samples.
        const uint largestAligned = (uint.MaxValue / 2) / 256 * 256;
        RtlSdrManagedDevice.ValidateRequestedSamples(largestAligned);
    }
}
