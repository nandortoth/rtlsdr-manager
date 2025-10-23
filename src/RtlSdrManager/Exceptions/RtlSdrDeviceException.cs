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

namespace RtlSdrManager.Exceptions;

/// <summary>
/// Class for managing exceptions to RTL-SDR device.
/// </summary>
/// <inheritdoc />
public class RtlSdrDeviceException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RtlSdrDeviceException"/> class.
    /// </summary>
    public RtlSdrDeviceException()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="RtlSdrDeviceException"/> class with a specified error message.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    public RtlSdrDeviceException(string message) : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="RtlSdrDeviceException"/> class with a specified error message
    /// and a reference to the inner exception that is the cause of this exception.
    /// </summary>
    /// <param name="message">The error message that explains the reason for the exception.</param>
    /// <param name="innerException">The exception that is the cause of the current exception, or a null reference if no inner exception is specified.</param>
    public RtlSdrDeviceException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
