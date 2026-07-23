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

using Xunit;

namespace RtlSdrManager.Tests;

/// <summary>
/// Unit tests for the small immutable value types.
/// </summary>
public class ValueTypeTests
{
    [Fact]
    public void CrystalFrequency_StoresBothFrequencies()
    {
        var crystal = new CrystalFrequency(
            Frequency.FromMHz(28.8),
            Frequency.FromMHz(28.8));

        Assert.Equal(28_800_000u, crystal.Rtl2832Frequency.Hz);
        Assert.Equal(28_800_000u, crystal.TunerFrequency.Hz);
        Assert.Contains("RTL2832", crystal.ToString());
        Assert.Contains("Tuner", crystal.ToString());
    }

    [Fact]
    public void CrystalFrequency_RecordEquality_ByValue()
    {
        var first = new CrystalFrequency(Frequency.FromMHz(28.8), Frequency.FromMHz(28.8));
        var second = new CrystalFrequency(Frequency.FromMHz(28.8), Frequency.FromMHz(28.8));

        Assert.Equal(first, second);
    }

    [Fact]
    public void IQData_StoresComponents_AndFormats()
    {
        var data = new IQData(127, 255);

        Assert.Equal(127, data.I);
        Assert.Equal(255, data.Q);
        Assert.Contains("I:", data.ToString());
        Assert.Contains("Q:", data.ToString());
    }

    [Fact]
    public void DeviceInfo_RecordEquality_ByValue()
    {
        var first = new DeviceInfo(0, "0001", "Realtek", "RTL2838UHIDIR", "Generic RTL2832U OEM");
        var second = new DeviceInfo(0, "0001", "Realtek", "RTL2838UHIDIR", "Generic RTL2832U OEM");

        Assert.Equal(first, second);
        Assert.Equal("Realtek", first.Manufacturer);
    }
}
