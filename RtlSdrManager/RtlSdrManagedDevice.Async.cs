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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
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
        #region Fields, Constants, Properties, Events

        /// <summary>
        /// Initialize the callback function for async sample reading (I/Q).
        /// </summary>
        private readonly unsafe RtlSdrLibraryWrapper.RtlSdrReadAsyncDelegate _asyncCallback =
            SamplesAvailableCallback;

        /// <summary>
        /// Async I/Q buffer (FIFO).
        /// </summary>
        private ConcurrentQueue<IQData> _asyncBuffer;

        /// <summary>
        /// Worker thread for async reader.
        /// </summary>
        private Thread _asyncWorker;

        /// <summary>
        /// Default amount of requested samples from RTL-SDR device.
        /// </summary>
        private const uint AsyncDefaultReadLength = 16384;

        /// <summary>
        /// Event to notify subscribers, if the new samples are available.
        /// </summary>
        public event EventHandler<SamplesAvailableEventArgs> SamplesAvailable;

        /// <summary>
        /// Accessor for the async I/Q buffer.
        /// </summary>
        public ConcurrentQueue<IQData> AsyncBuffer
        {
            get
            {
                // Check the buffer. It can be reachable, if there is an async reading.
                if (_asyncBuffer == null)
                {
                    throw new RtlSdrLibraryExecutionException(
                        "The async buffer is not initialized yet. " +
                        "StartReadSamplesAsync function must be invoked first.");
                }

                // Return the buffer.
                return _asyncBuffer;
            }
        }

        /// <summary>
        /// Maximum size of async I/Q buffer.
        /// </summary>
        public uint MaxAsyncBufferSize { get; set; }
        
        /// <summary>
        /// Define the behavior if the buffer is full.
        /// Drop samples (true), or throw exception (false).
        /// </summary>
        public bool DropSamplesOnFullBuffer { get; set; }
        
        /// <summary>
        /// Counter for dropped I/Q samples.
        /// It is possible to reset the counter with <see cref="ResetDroppedSamplesCounter"/>.
        /// </summary>
        public uint DroppedSamplesCount { get; private set; }

        #endregion

        #region Methods

        /// <summary>
        /// Reset the counter for dropped I/Q samples.
        /// </summary>
        public void ResetDroppedSamplesCounter()
        {
            DroppedSamplesCount = 0;
        }

        /// <summary>
        /// Get I/Q samples from the async buffer.
        /// </summary>
        /// <param name="maxCount">Maximum amount of requested I/Q samples. If there is less samples in the buffer,
        /// than the requested amount, maxCount will be reduced.</param>
        /// <returns>List if I/Q samples.</returns>
        /// <exception cref="RtlSdrLibraryExecutionException"></exception>
        public List<IQData> GetSamplesFromAsyncBuffer(int maxCount)
        {
            // Check the buffer. It can be reachable, if there is an async reading.
            if (_asyncBuffer == null)
            {
                throw new RtlSdrLibraryExecutionException(
                    "The async buffer is not initialized yet. " +
                    "StartReadSamplesAsync function must be invoked first.");
            }

            // Initialize the local buffer.
            var iqData = new List<IQData>();
            
            // Check the available samples in the async buffer.
            if (maxCount > _asyncBuffer.Count)
            {
                maxCount = _asyncBuffer.Count;
            }

            // Dequeue of the samples from the async buffer.
            for (var i = 0; i < maxCount; i++)
            {
                while (true)
                {
                    if (!_asyncBuffer.TryDequeue(out var data))
                    {
                        continue;
                    }

                    iqData.Add(data);
                    break;
                }
            }

            // Return the local buffer.
            return iqData;
        }

        /// <summary>
        /// Callback function for async reading (I/Q).
        /// </summary>
        /// <param name="buf">Buffer to store samples.</param>
        /// <param name="len">Length of the buffer.</param>
        /// <param name="ctx">Device context.</param>
        private static unsafe void SamplesAvailableCallback(byte* buf, uint len, IntPtr ctx)
        {
            // Get the context pointer.
            var context = GCHandle.FromIntPtr(ctx);

            // If the context pointer does not exist, everything must be stopped.
            if (!context.IsAllocated)
            {
                return;
            }

            // Get the context target (actual instance of RtlSdrManagedDevice).
            var target = (RtlSdrManagedDevice) context.Target;
            
            // Count of I/Q data.
            var length = (int) len / 2;
            
            // Check the the async buffer usage.
            if (target._asyncBuffer.Count + length >= target.MaxAsyncBufferSize)
            {
                // Throw an exception, if dropping samples was not asked.
                if (!target.DropSamplesOnFullBuffer)
                {
                    throw new RtlSdrManagedDeviceException(
                        "The async buffer of the managed device is full. " +
                        $"Current buffer size: {target._asyncBuffer.Count + length} I/Q samples, " +
                        $"Maximum buffer size: {target.MaxAsyncBufferSize} I/Q samples, " +
                        $"Device index: {target.DeviceInfo.Index}.");
                }
                
                // Drop samples, since the async buffer is full, but increase the counter.
                target.DroppedSamplesCount += (uint) length;
                return;               
            }
            
            // Add the samples to the async buffer.
            for (var i = 0; i < length; i++)
            {               
                var iqData = new IQData(*buf++, *buf++);
                target._asyncBuffer.Enqueue(iqData);
            }

            // Raise the SampleAvailable event.
            target.OnSamplesAvailable(new SamplesAvailableEventArgs(length));
        }

        /// <summary>
        /// Ensure that registered delegates receive the SamplesAvailable event.
        /// </summary>
        /// <param name="e">Event argument.</param>
        private void OnSamplesAvailable(SamplesAvailableEventArgs e)
        {
            // If there are subscriber(s), raise event.
            SamplesAvailable?.Invoke(this, e);
        }

        /// <summary>
        /// Worker method to asynchroniously read data from the RTL-SDR device.
        /// </summary>
        /// <exception cref="RtlSdrLibraryExecutionException"></exception>
        private void SamplesAsyncReader(object readLength)
        {
            // Read from device.
            RtlSdrLibraryWrapper.rtlsdr_read_async(_devicePointer, _asyncCallback,
                (IntPtr) _deviceContext, 0, (uint) readLength);
        }

        /// <summary>
        /// Start reading samples (I/Q) from the device asynchroniously.
        /// </summary>
        /// <param name="requestedSamples">Amount of requested samples by one device read.</param>
        /// <exception cref="RtlSdrLibraryExecutionException"></exception>
        public void StartReadSamplesAsync(uint requestedSamples = AsyncDefaultReadLength)
        {
            // If the buffer does not exist, must be initialized.
            if (_asyncBuffer == null)
            {
                _asyncBuffer = new ConcurrentQueue<IQData>();
            }

            // Check the worker thread.
            if (_asyncWorker != null)
            {
                throw new RtlSdrLibraryExecutionException(
                    "Problem happened during asynchronious data reading from the device. " +
                    $"The worker thread is already started. Device index: {DeviceInfo.Index}.");
            }

            // Start the worker with normal priorty.
            _asyncWorker = new Thread(SamplesAsyncReader)
            {
                Priority = ThreadPriority.Highest
            };
            _asyncWorker.Start(requestedSamples * 2);
        }

        /// <summary>
        /// Stop reading samples from the device.
        /// </summary>
        public void StopReadSamplesAsync()
        {
            // Check the worker thread.
            if (_asyncWorker == null)
            {
                return;
            }

            // Cancel the reading with the native function.
            var returnCode = RtlSdrLibraryWrapper.rtlsdr_cancel_async(_devicePointer);

            // If we did not get 0, there is an error.
            if (returnCode != 0)
            {
                throw new RtlSdrLibraryExecutionException(
                    "Problem happened during stopping asynchronious data reading. " +
                    $"Error code: {returnCode}, device index: {DeviceInfo.Index}.");
            }

            // Stop the worker thread.
            if (_asyncWorker.ThreadState == ThreadState.Running)
            {
                _asyncWorker.Join();
            }

            // Release the worker.
            _asyncWorker = null;

            // Empty the buffer.
            _asyncBuffer = null;
        }

        #endregion
    }
}