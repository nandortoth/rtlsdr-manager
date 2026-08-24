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
        // The ceiling is itself a multiple of 256 samples (512 bytes), so it is the largest
        // valid count rather than merely the last one below the limit.
        RtlSdrManagedDevice.ValidateRequestedSamples(RtlSdrManagedDevice.MaximumRequestedSamples);
    }

    [Fact]
    public void CountJustAboveTheCeiling_IsRejected()
    {
        // A multiple of 256 samples, so it fails on size alone and not on alignment.
        const uint justAbove = RtlSdrManagedDevice.MaximumRequestedSamples + 256;

        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => RtlSdrManagedDevice.ValidateRequestedSamples(justAbove));

        Assert.Equal("requestedSamples", exception.ParamName);
        Assert.Equal(justAbove, exception.ActualValue);
    }

    [Theory]
    [InlineData(1u)]
    [InlineData(15u)]   // the default
    [InlineData(64u)]   // the ceiling
    public void ValidTransferBufferCounts_Pass(uint transferBufferCount) =>
        RtlSdrManagedDevice.ValidateTransferBufferCount(transferBufferCount);

    [Theory]
    [InlineData(0u)]
    [InlineData(65u)]
    [InlineData(uint.MaxValue)]
    public void InvalidTransferBufferCounts_AreRejected(uint transferBufferCount)
    {
        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => RtlSdrManagedDevice.ValidateTransferBufferCount(transferBufferCount));

        Assert.Equal("transferBufferCount", exception.ParamName);
    }

    [Fact]
    public void DefaultTransferBufferCount_IsWithinTheAcceptedRange() =>
        RtlSdrManagedDevice.ValidateTransferBufferCount(
            RtlSdrManagedDevice.DefaultTransferBufferCount);

    [Theory]
    [InlineData(16384u, 15u)]                                  // library defaults
    [InlineData(131072u, 15u)]                                 // used by the samples
    [InlineData(RtlSdrManagedDevice.MaximumRequestedSamples, 16u)]  // exactly 256 MiB
    public void TotalsWithinTheLimit_Pass(uint requestedSamples, uint transferBufferCount) =>
        RtlSdrManagedDevice.ValidateTotalTransferSize(requestedSamples, transferBufferCount);

    [Fact]
    public void TotalAboveTheLimit_IsRejected_EvenWhenEachValueIsValid()
    {
        // Both values pass their own check: 8388608 samples is exactly the per-read ceiling
        // and 17 buffers is well inside 1..64. Only the product is too large, which is the
        // entire reason this check exists.
        const uint requestedSamples = RtlSdrManagedDevice.MaximumRequestedSamples;
        const uint transferBufferCount = 17u;

        RtlSdrManagedDevice.ValidateRequestedSamples(requestedSamples);
        RtlSdrManagedDevice.ValidateTransferBufferCount(transferBufferCount);

        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => RtlSdrManagedDevice.ValidateTotalTransferSize(requestedSamples, transferBufferCount));

        Assert.Equal("requestedSamples", exception.ParamName);
        Assert.Contains("TransferBufferCount", exception.Message);
    }

    [Fact]
    public void TotalCheck_DoesNotOverflow_AtTheLargestAcceptedValues()
    {
        // These operands overflow long and ulong alike, so a narrower accumulator would wrap
        // to a small total and report the request as acceptable.
        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => RtlSdrManagedDevice.ValidateTotalTransferSize(uint.MaxValue, uint.MaxValue));

        Assert.Equal("requestedSamples", exception.ParamName);
    }
}
