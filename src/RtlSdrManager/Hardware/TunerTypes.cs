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

namespace RtlSdrManager.Hardware;

/// <summary>
/// Supported RTL-SDR tuner types.
/// </summary>
public enum TunerTypes
{
    /// <summary>
    /// Unknown tuner type, also used as error indication.
    /// </summary>
    Unknown,

    /// <summary>
    /// Elonics E4000.
    /// </summary>
    E4000,

    /// <summary>
    /// Fitipower FC0012.
    /// </summary>
    FC0012,

    /// <summary>
    /// Fitipower FC0013.
    /// </summary>
    FC0013,

    /// <summary>
    /// FCI FC2580.
    /// </summary>
    FC2580,

    /// <summary>
    /// Rafael Micro R820T.
    /// </summary>
    R820T,

    /// <summary>
    /// Rafael Micro R828D.
    /// </summary>
    R828D
}
