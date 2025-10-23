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

namespace RtlSdrManager;

/// <summary>
/// Immutable record to store I/Q data (analytic signal), representing the signal from RTL-SDR device.
/// </summary>
/// <param name="I">In-Phase signal component.</param>
/// <param name="Q">Quadrature signal component.</param>
public readonly record struct IQData(int I, int Q)
{
    /// <summary>
    /// Returns a string representation of the I/Q data.
    /// </summary>
    /// <returns>String value of the I/Q data instance.</returns>
    public override string ToString() => $"I: {I,3}, Q: {Q,3}";
}
