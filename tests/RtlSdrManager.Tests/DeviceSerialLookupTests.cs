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
using RtlSdrManager.Exceptions;
using Xunit;

namespace RtlSdrManager.Tests;

/// <summary>
/// Unit tests for resolving a serial number to a device index
/// (<see cref="RtlSdrDeviceManager.ResolveSerialToIndex"/>).
/// </summary>
/// <remarks>
/// These need no device: the lookup runs over the manager's device list, and
/// <see cref="DeviceInfo"/> is a plain record, so a list can be built directly. That is
/// unusual for this project, where most device behavior can only be checked by the
/// hardware harness.
/// </remarks>
public class DeviceSerialLookupTests
{
    /// <summary>
    /// Build a device list from serial numbers, indexed from zero in the order given.
    /// </summary>
    private static Dictionary<uint, DeviceInfo> DeviceList(params string[] serials)
    {
        var devices = new Dictionary<uint, DeviceInfo>();

        for (uint index = 0; index < serials.Length; index++)
        {
            devices.Add(index, new DeviceInfo(
                Index: index,
                Serial: serials[index],
                Manufacturer: "Realtek",
                ProductType: "RTL2838UHIDIR",
                Name: $"Device {index}"));
        }

        return devices;
    }

    [Theory]
    [InlineData("00000001", 0u)]
    [InlineData("19862103", 1u)]
    [InlineData("19862104", 2u)]
    public void MatchingSerial_ReturnsItsIndex(string serial, uint expectedIndex)
    {
        Dictionary<uint, DeviceInfo> devices = DeviceList("00000001", "19862103", "19862104");

        Assert.Equal(expectedIndex, RtlSdrDeviceManager.ResolveSerialToIndex(devices, serial));
    }

    [Fact]
    public void UnknownSerial_Throws_AndNamesTheAvailableOnes()
    {
        Dictionary<uint, DeviceInfo> devices = DeviceList("19862103", "19862104");

        RtlSdrDeviceException exception = Assert.Throws<RtlSdrDeviceException>(
            () => RtlSdrDeviceManager.ResolveSerialToIndex(devices, "00000009"));

        Assert.Contains("00000009", exception.Message);
        Assert.Contains("19862103", exception.Message);
        Assert.Contains("19862104", exception.Message);

        // A device list that predates a plug or unplug is the likely cause, and the remedy
        // is not otherwise discoverable from the message.
        Assert.Contains("RefreshDevices", exception.Message);
    }

    [Fact]
    public void EmptyDeviceList_Throws_WithoutClaimingSerialsExist()
    {
        RtlSdrDeviceException exception = Assert.Throws<RtlSdrDeviceException>(
            () => RtlSdrDeviceManager.ResolveSerialToIndex(DeviceList(), "19862103"));

        Assert.Contains("no devices were found", exception.Message);
    }

    [Fact]
    public void DuplicateSerials_Throw_RatherThanPickingOne()
    {
        // The factory default on many devices, which is exactly the case that must not
        // resolve to an arbitrary device.
        Dictionary<uint, DeviceInfo> devices = DeviceList("00000001", "19862103", "00000001");

        RtlSdrDeviceException exception = Assert.Throws<RtlSdrDeviceException>(
            () => RtlSdrDeviceManager.ResolveSerialToIndex(devices, "00000001"));

        Assert.Contains("2 RTL-SDR devices", exception.Message);
        Assert.Contains("00000001", exception.Message);
    }

    [Fact]
    public void DuplicateSerials_DoNotAffectADistinctSerial()
    {
        Dictionary<uint, DeviceInfo> devices = DeviceList("00000001", "19862103", "00000001");

        Assert.Equal(1u, RtlSdrDeviceManager.ResolveSerialToIndex(devices, "19862103"));
    }

    [Fact]
    public void SerialComparison_IsCaseSensitive()
    {
        // A serial number is an opaque identifier read from the device, not text, so nothing
        // makes these the same device.
        Dictionary<uint, DeviceInfo> devices = DeviceList("ABC123");

        Assert.Throws<RtlSdrDeviceException>(
            () => RtlSdrDeviceManager.ResolveSerialToIndex(devices, "abc123"));
    }

    [Fact]
    public void SerialComparison_IsExact_NotAPrefixOrSubstring()
    {
        Dictionary<uint, DeviceInfo> devices = DeviceList("19862103");

        Assert.Throws<RtlSdrDeviceException>(
            () => RtlSdrDeviceManager.ResolveSerialToIndex(devices, "1986"));
        Assert.Throws<RtlSdrDeviceException>(
            () => RtlSdrDeviceManager.ResolveSerialToIndex(devices, "198621030"));
    }

    [Fact]
    public void NullSerial_Throws_WithParamName()
    {
        ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
            () => RtlSdrDeviceManager.ResolveSerialToIndex(DeviceList("19862103"), null!));

        Assert.Equal("serial", exception.ParamName);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    public void EmptyOrWhitespaceSerial_Throws_WithParamName(string serial)
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => RtlSdrDeviceManager.ResolveSerialToIndex(DeviceList("19862103"), serial));

        Assert.Equal("serial", exception.ParamName);
    }

    [Fact]
    public void ADeviceWithAnEmptySerial_CannotBeOpenedBySerial()
    {
        // A known limitation rather than an oversight. The empty serial is rejected as an
        // argument before the device list is consulted, so a device reporting one cannot be
        // reached this way at all, even though an exact match exists. Opening it by index
        // still works, and giving it a real serial is the fix.
        Dictionary<uint, DeviceInfo> devices = DeviceList("");

        Assert.Throws<ArgumentException>(
            () => RtlSdrDeviceManager.ResolveSerialToIndex(devices, ""));
    }
}
