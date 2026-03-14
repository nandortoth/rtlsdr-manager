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
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Channels;
using RtlSdrManager.Exceptions;
using RtlSdrManager.Interop;

namespace RtlSdrManager;

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
    private readonly unsafe LibRtlSdr.RtlSdrReadAsyncDelegate _asyncCallback =
        SamplesAvailableCallback;

    /// <summary>
    /// Async I/Q buffer (FIFO). Used when <see cref="UseRawBufferMode"/> is false.
    /// </summary>
    private ConcurrentQueue<IQData>? _asyncBuffer;

    /// <summary>
    /// Raw async channel for zero-copy buffer handoff. Used when <see cref="UseRawBufferMode"/> is true.
    /// </summary>
    private Channel<RawSampleBuffer>? _rawAsyncChannel;

    /// <summary>
    /// Maximum number of buffers in the raw async channel.
    /// </summary>
    private int _rawChannelCapacity;

    /// <summary>
    /// Worker thread for async reader.
    /// </summary>
    private Thread? _asyncWorker;

    /// <summary>
    /// Default amount of requested samples from RTL-SDR device.
    /// </summary>
    private const uint AsyncDefaultReadLength = 16384;

    /// <summary>
    /// Event to notify subscribers, if the new samples are available.
    /// </summary>
    public event EventHandler<SamplesAvailableEventArgs>? SamplesAvailable;

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
    /// Applies to both IQData mode and raw buffer mode.
    /// </summary>
    public bool DropSamplesOnFullBuffer { get; set; }

    /// <summary>
    /// Counter for dropped I/Q samples.
    /// It is possible to reset the counter with <see cref="ResetDroppedSamplesCounter"/>.
    /// </summary>
    public uint DroppedSamplesCount { get; private set; }

    /// <summary>
    /// When true, the device uses raw buffer mode: samples are delivered as raw byte[]
    /// buffers via <see cref="Channel{T}"/> instead of per-sample <see cref="IQData"/> objects
    /// via <see cref="ConcurrentQueue{T}"/>.
    /// Must be set before calling <see cref="StartReadSamplesAsync"/>. Default: false.
    /// </summary>
    public bool UseRawBufferMode { get; set; }

    #endregion

    #region Methods

    /// <summary>
    /// Reset the counter for dropped I/Q samples.
    /// </summary>
    public void ResetDroppedSamplesCounter() => DroppedSamplesCount = 0;

    /// <summary>
    /// Get I/Q samples from the async buffer.
    /// </summary>
    /// <param name="maxCount">Maximum amount of requested I/Q samples. If there are fewer samples in the buffer,
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

        // Check the available samples in the async buffer.
        if (maxCount > _asyncBuffer.Count)
        {
            maxCount = _asyncBuffer.Count;
        }

        // Initialize the local buffer with pre-allocated capacity.
        var iqData = new List<IQData>(maxCount);

        // Dequeue of the samples from the async buffer.
        for (int i = 0; i < maxCount; i++)
        {
            if (!_asyncBuffer.TryDequeue(out IQData data))
            {
                break;
            }

            iqData.Add(data);
        }

        // Return the local buffer.
        return iqData;
    }

    /// <summary>
    /// Get raw I/Q sample buffer from the async channel.
    /// Returns the next available buffer, or null if no buffer is ready.
    /// The caller MUST call <see cref="RawSampleBuffer.Return"/> after processing.
    /// </summary>
    /// <returns>Raw sample buffer, or null if none available.</returns>
    /// <exception cref="RtlSdrLibraryExecutionException">
    /// Thrown when raw buffer mode is not active or <see cref="StartReadSamplesAsync"/> has not been called.
    /// </exception>
    public RawSampleBuffer? GetRawSamplesFromAsyncBuffer()
    {
        if (_rawAsyncChannel == null)
        {
            throw new RtlSdrLibraryExecutionException(
                "The raw async channel is not initialized yet. " +
                "Set UseRawBufferMode = true and call StartReadSamplesAsync first.");
        }

        return _rawAsyncChannel.Reader.TryRead(out RawSampleBuffer? buffer) ? buffer : null;
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
        var target = (RtlSdrManagedDevice?)context.Target;
        if (target == null)
        {
            return;
        }

        if (target.UseRawBufferMode)
        {
            // Raw buffer mode: rent from pool, memcpy, hand off via Channel.
            Channel<RawSampleBuffer> channel = target._rawAsyncChannel!;
            int byteLength = (int)len;

            // Check backpressure: if channel is full, either drop or throw
            // (consistent with legacy IQData mode behavior).
            if (channel.Reader.Count >= target._rawChannelCapacity)
            {
                if (!target.DropSamplesOnFullBuffer)
                {
                    throw new RtlSdrManagedDeviceException(
                        "The raw async channel of the managed device is full. " +
                        $"Current channel usage: {channel.Reader.Count} buffers, " +
                        $"Maximum capacity: {target._rawChannelCapacity} buffers, " +
                        $"Device index: {target.DeviceInfo.Index}.");
                }

                target.DroppedSamplesCount += (uint)(byteLength / 2);
                return;
            }

            // Rent a buffer from the shared pool (zero-allocation on steady state).
            byte[] pooledBuffer = ArrayPool<byte>.Shared.Rent(byteLength);

            // Single memcpy from native buffer to managed array.
            new ReadOnlySpan<byte>(buf, byteLength).CopyTo(pooledBuffer);

            // Hand off to consumer via bounded Channel.
            var rawBuffer = new RawSampleBuffer(pooledBuffer, byteLength);
            if (!channel.Writer.TryWrite(rawBuffer))
            {
                // Channel full (race with pre-check above) — return buffer and count as dropped.
                ArrayPool<byte>.Shared.Return(pooledBuffer);
                target.DroppedSamplesCount += (uint)(byteLength / 2);
                return;
            }

            // Raise the SampleAvailable event.
            target.OnSamplesAvailable(new SamplesAvailableEventArgs(byteLength / 2));
        }
        else
        {
            // Legacy IQData mode: existing implementation.

            // Count of I/Q data.
            int length = (int)len / 2;

            // The buffer is guaranteed to be non-null when the callback is active.
            ConcurrentQueue<IQData>? buffer = target._asyncBuffer!;

            // Check the async buffer usage.
            if (buffer.Count + length >= target.MaxAsyncBufferSize)
            {
                // Throw an exception, if dropping samples was not asked.
                if (!target.DropSamplesOnFullBuffer)
                {
                    throw new RtlSdrManagedDeviceException(
                        "The async buffer of the managed device is full. " +
                        $"Current buffer size: {buffer.Count + length} I/Q samples, " +
                        $"Maximum buffer size: {target.MaxAsyncBufferSize} I/Q samples, " +
                        $"Device index: {target.DeviceInfo.Index}.");
                }

                // Drop samples, since the async buffer is full, but increase the counter.
                target.DroppedSamplesCount += (uint)length;
                return;
            }

            // Build all IQData items first (no locking overhead).
            var samples = new IQData[length];
            for (int i = 0; i < length; i++)
            {
                samples[i] = new IQData(*buf++, *buf++);
            }

            // Enqueue the batch into the concurrent buffer.
            for (int i = 0; i < length; i++)
            {
                buffer.Enqueue(samples[i]);
            }

            // Raise the SampleAvailable event.
            target.OnSamplesAvailable(new SamplesAvailableEventArgs(length));
        }
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
    /// Worker method to asynchronously read data from the RTL-SDR device.
    /// </summary>
    /// <exception cref="RtlSdrLibraryExecutionException"></exception>
    private void SamplesAsyncReader(object? readLength)
    {
        // Read from device.
        LibRtlSdr.rtlsdr_read_async(_deviceHandle!, _asyncCallback,
            (IntPtr)_deviceContext, 0, (uint)readLength!);
    }

    /// <summary>
    /// Start reading samples (I/Q) from the device asynchronously.
    /// </summary>
    /// <param name="requestedSamples">Amount of requested samples by one device read.</param>
    /// <exception cref="RtlSdrLibraryExecutionException"></exception>
    public void StartReadSamplesAsync(uint requestedSamples = AsyncDefaultReadLength)
    {
        // Initialize the appropriate buffer based on mode.
        if (UseRawBufferMode)
        {
            // Calculate channel capacity from MaxAsyncBufferSize.
            // Each buffer holds requestedSamples, so capacity = MaxAsyncBufferSize / requestedSamples.
            int capacity = Math.Max(8, (int)(MaxAsyncBufferSize / requestedSamples));
            _rawChannelCapacity = capacity;
            _rawAsyncChannel ??= Channel.CreateBounded<RawSampleBuffer>(
                new BoundedChannelOptions(capacity)
                {
                    SingleWriter = true,    // Only the native callback writes
                    SingleReader = true,    // Only OnSamplesAvailable reads
                    FullMode = BoundedChannelFullMode.DropWrite
                });
        }
        else
        {
            // If the buffer does not exist, must be initialized.
            _asyncBuffer ??= new ConcurrentQueue<IQData>();
        }

        // Check the worker thread.
        if (_asyncWorker != null)
        {
            throw new RtlSdrLibraryExecutionException(
                "Problem happened during asynchronous data reading from the device. " +
                $"The worker thread is already started. Device index: {DeviceInfo.Index}.");
        }

        // Start the worker with highest priority.
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
        // Check if the worker thread is running
        if (_asyncWorker == null)
        {
            return;
        }

        // Cancel the reading with the native function.
        int returnCode = LibRtlSdr.rtlsdr_cancel_async(_deviceHandle!);

        // If we did not get 0, there is an error.
        if (returnCode != 0)
        {
            throw new RtlSdrLibraryExecutionException(
                "Problem happened during stopping asynchronous data reading. " +
                $"Error code: {returnCode}, device index: {DeviceInfo.Index}.");
        }

        // Wait for the worker thread to finish.
        if (_asyncWorker.ThreadState == ThreadState.Running)
        {
            _asyncWorker.Join();
        }

        // Release the worker.
        _asyncWorker = null;

        // Empty the IQData buffer.
        _asyncBuffer = null;

        // Drain and clean up the raw async channel.
        if (_rawAsyncChannel != null)
        {
            _rawAsyncChannel.Writer.TryComplete();
            while (_rawAsyncChannel.Reader.TryRead(out RawSampleBuffer? remaining))
            {
                remaining.Return();
            }
            _rawAsyncChannel = null;
        }
    }

    #endregion
}
