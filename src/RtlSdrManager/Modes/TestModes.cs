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

namespace RtlSdrManager.Modes;

/// <summary>
/// Test modes of the RTL2832.
/// </summary>
public enum TestModes
{
    /// <summary>
    /// The test mode of the RTL2832 is disabled.
    /// </summary>
    Disabled,

    /// <summary>
    /// The test mode of the RTL2832 is enabled: the device returns an
    /// 8 bit counter instead of the samples.
    /// </summary>
    Enabled
}
