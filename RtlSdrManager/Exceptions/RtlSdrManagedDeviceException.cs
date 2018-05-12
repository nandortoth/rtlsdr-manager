// RTL-SDR Manager Library for .NET Core
// Copyright (C) 2018 Nandor Toth <dev@nandortoth.eu>
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

namespace RtlSdrManager.Exceptions
{
    /// <summary>
    /// Class for handling exceptions in case of RtlSdrManagedDevice.
    /// </summary>
    /// <inheritdoc />
    public class RtlSdrManagedDeviceException : Exception
    {
        /// <summary>
        /// Create new RtlSdrManagedDeviceExecution instance.
        /// </summary>
        /// <param name="message">Reason and/or message for the exception.</param>
        /// <inheritdoc />
        public RtlSdrManagedDeviceException(string message) : base(message)
        {
        }
    }
}