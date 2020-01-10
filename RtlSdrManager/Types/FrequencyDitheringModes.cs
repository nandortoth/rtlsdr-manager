// RTL-SDR Manager Library for .NET Core
// Copyright (C) 2020 Nandor Toth <dev@nandortoth.com>
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

namespace RtlSdrManager.Types
{
    /// <summary>
    /// Frequency dithering for R820T tuners.
    /// </summary>
    public enum FrequencyDitheringModes
    {
        Disabled,
        Enabled,
                
        // This value is used for the devices, which do not support
        // the dithering. It is for internal usage.
        NotSet
    }
}