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

namespace RtlSdrManager.Modes;

/// <summary>
/// Direct sampling modes for the device.
/// </summary>
public enum DirectSamplingModes
{
    /// <summary>
    /// Direct sampling is disabled.
    /// </summary>
    Disabled,

    /// <summary>
    /// Direct sampling with the I-ADC input enabled.
    /// </summary>
    InPhaseADCInputEnabled,

    /// <summary>
    /// Direct sampling with the Q-ADC input enabled.
    /// </summary>
    QuadratureADCInputEnabled
}
