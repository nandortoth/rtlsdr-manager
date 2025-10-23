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
using Microsoft.Win32.SafeHandles;

namespace RtlSdrManager.Interop;

/// <summary>
/// Represents a safe handle for RTL-SDR devices that ensures proper cleanup.
/// This handle automatically closes the device when disposed or finalized.
/// </summary>
internal sealed class SafeRtlSdrHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    /// <summary>
    /// Initializes a new instance of the SafeRtlSdrHandle class.
    /// </summary>
    public SafeRtlSdrHandle() : base(ownsHandle: true)
    {
    }

    /// <summary>
    /// Initializes a new instance of the SafeRtlSdrHandle class with the specified handle.
    /// </summary>
    /// <param name="existingHandle">An IntPtr object that represents the pre-existing handle to use.</param>
    /// <param name="ownsHandle">true to reliably release the handle during finalization; false to prevent it.</param>
    public SafeRtlSdrHandle(IntPtr existingHandle, bool ownsHandle) : base(ownsHandle)
    {
        SetHandle(existingHandle);
    }

    /// <summary>
    /// Executes the code required to free the handle.
    /// </summary>
    /// <returns>true if the handle is released successfully; otherwise, false.</returns>
    protected override bool ReleaseHandle()
    {
        // Close the RTL-SDR device
        // Return value: 0 on success
        return LibRtlSdr.rtlsdr_close(handle) == 0;
    }
}
