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
/// Immutable record to store RTL-SDR's crystal frequencies.
/// </summary>
/// <param name="Rtl2832Frequency">Frequency value used to clock the RTL2832.</param>
/// <param name="TunerFrequency">Frequency value used to clock the tuner IC.</param>
public readonly record struct CrystalFrequency(
    Frequency Rtl2832Frequency,
    Frequency TunerFrequency)
{
    /// <summary>
    /// Returns a string representation of the crystal frequencies.
    /// </summary>
    /// <returns>String value of the CrystalFrequency instance.</returns>
    public override string ToString() =>
        $"RTL2832: {Rtl2832Frequency.MHz} MHz; Tuner: {TunerFrequency.MHz} MHz";
}
