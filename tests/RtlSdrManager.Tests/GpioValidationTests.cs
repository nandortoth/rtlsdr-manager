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
/// Unit tests for GPIO pin validation.
/// The 0..7 range comes from the demodulator's single-byte GPO register, written as
/// 1 &lt;&lt; gpio, so it is the same for every tuner.
/// </summary>
public class GpioValidationTests
{
    [Theory]
    [InlineData(0)]     // the pin the bias tee is wired to on most dongles
    [InlineData(1)]
    [InlineData(4)]     // reserved for the tuner reset pulse, but deliberately not blocked
    [InlineData(6)]     // reserved for FC0012 band select, but deliberately not blocked
    [InlineData(7)]
    public void PinsWithinRange_AreAccepted(int gpio) =>
        RtlSdrManagedDevice.ValidateGpioPin(gpio);

    [Theory]
    [InlineData(8)]
    [InlineData(-1)]
    [InlineData(int.MaxValue)]
    [InlineData(int.MinValue)]
    public void PinsOutsideRange_AreRejected(int gpio)
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => RtlSdrManagedDevice.ValidateGpioPin(gpio));

        // Regression: the range check used to throw RtlSdrLibraryExecutionException, and an
        // earlier class of bug in this codebase passed the message into the paramName slot.
        Assert.Equal("gpio", exception.ParamName);
        Assert.Equal(gpio, exception.ActualValue);
    }
}
