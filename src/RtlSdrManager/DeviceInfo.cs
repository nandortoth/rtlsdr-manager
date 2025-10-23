// RTL-SDR Manager Library for .NET
// Copyright (C) 2018-2025 Nandor Toth <dev@nandortoth.eu>
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

namespace RtlSdrManager;

/// <summary>
/// Immutable record containing fundamental information about an RTL-SDR device.
/// </summary>
/// <param name="Index">Index of the device on the system.</param>
/// <param name="Serial">Serial number of the device.</param>
/// <param name="Manufacturer">Manufacturer of the device.</param>
/// <param name="ProductType">Product type of the device.</param>
/// <param name="Name">Name of the device.</param>
public readonly record struct DeviceInfo(
    uint Index,
    string Serial,
    string Manufacturer,
    string ProductType,
    string Name);
