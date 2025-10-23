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

using System;

namespace RtlSdrManager;

/// <summary>
/// Event arguments for SamplesAvailable event.
/// </summary>
/// <inheritdoc />
public class SamplesAvailableEventArgs : EventArgs
{
    /// <summary>
    /// Create an instance of SamplesAvailableEventArgs.
    /// </summary>
    /// <param name="count">Available samples.</param>
    /// <inheritdoc />
    internal SamplesAvailableEventArgs(int count)
    {
        SampleCount = count;
    }

    /// <summary>
    /// Return the amount of the available samples.
    /// </summary>
    public int SampleCount { get; }
}
