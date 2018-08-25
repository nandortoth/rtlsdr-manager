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

using System.Collections.Generic;
using System.Runtime.InteropServices;
using RtlSdrManager.Exceptions;
using RtlSdrManager.Types;

namespace RtlSdrManager
{
    /// <summary>
    /// Class for a managed (opened) RTL-SDR device.
    /// </summary>
    /// <inheritdoc />
    public sealed partial class RtlSdrManagedDevice
    {
        #region Methods

        /// <summary>
        /// Read samples (I/Q) from the device.
        /// </summary>
        /// <param name="requestedSamples">Amount of requested samples.</param>
        /// <returns>I/Q data from the device as an IqData list.</returns>
        /// <exception cref="RtlSdrLibraryExecutionException"></exception>
        public List<IQData> ReadSamples(int requestedSamples)
        {
            // I/Q data means 2 bytes.
            var requestedBytes = requestedSamples * 2;

            // Initialize the buffer.
            var buffer = new byte[requestedBytes];
            var bufferPinned = GCHandle.Alloc(buffer, GCHandleType.Pinned);
            var bufferPointer = bufferPinned.AddrOfPinnedObject();

            // Read the ubytes from the device.
            var returnCode = RtlSdrLibraryWrapper.rtlsdr_read_sync(_devicePointer,
                bufferPointer, requestedBytes, out var receivedBytes);

            // Overflow happened.
            if (returnCode == -8)
            {
                throw new RtlSdrLibraryExecutionException(
                    "Problem happened during reading bytes from the device (overflow, device provided more data). " +
                    $"Error code: {returnCode}, requested bytes: {requestedBytes}, device index: {DeviceInfo.Index}.");
            }

            // Error happened during reading the data.
            if (returnCode != 0)
            {
                throw new RtlSdrLibraryExecutionException(
                    "Problem happened during reading bytes from the device. " +
                    $"Error code: {returnCode}, requested bytes: {requestedBytes}, device index: {DeviceInfo.Index}.");
            }

            // Amount of the received bytes is different than the requested.
            if (receivedBytes != requestedBytes)
            {
                throw new RtlSdrLibraryExecutionException(
                    "Problem happened during reading bytes from the device. " +
                    $"Error code: {returnCode}, requested bytes: {requestedBytes}, " +
                    $"received bytes: {receivedBytes}, device index: {DeviceInfo.Index}.");
            }

            // Release the memory object.
            bufferPinned.Free();

            // Convert byte array to IqData list.
            var iqData = new List<IQData>();
            for (var i = 0; i < buffer.Length; i += 2)
            {
                iqData.Add(new IQData(buffer[i], buffer[i + 1]));
            }

            // Return the IqData list.
            return iqData;
        }

        #endregion
    }
}