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
    /// First error captured during asynchronous reading (native callback or worker thread).
    /// Written with Interlocked.CompareExchange; the first error wins.
    /// </summary>
    private Exception? _asyncReadException;

    /// <summary>
    /// Buffer mode captured at StartReadSamplesAsync. The callback branches on this
    /// snapshot, so toggling <see cref="UseRawBufferMode"/> during reading has no effect.
    /// </summary>
    private bool _activeRawBufferMode;

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
    /// <exception cref="InvalidOperationException">Thrown when the buffer is not initialized yet.</exception>
    public ConcurrentQueue<IQData> AsyncBuffer
    {
        get
        {
            // Check the buffer. It can be reachable, if there is an async reading.
            if (_asyncBuffer == null)
            {
                throw new InvalidOperationException(
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
    /// Must be set before calling <see cref="StartReadSamplesAsync"/>; changes take
    /// effect at the next <see cref="StartReadSamplesAsync"/> call. Default: false.
    /// </summary>
    public bool UseRawBufferMode { get; set; }

    /// <summary>
    /// The last error captured during asynchronous reading, or null if no error happened.
    /// When an error happens (e.g. the buffer is full and <see cref="DropSamplesOnFullBuffer"/>
    /// is false, or a <see cref="SamplesAvailable"/> handler throws), the reading stops and
    /// the error is available here; <see cref="StopReadSamplesAsync"/> also throws it.
    /// </summary>
    public Exception? LastAsyncException => Volatile.Read(ref _asyncReadException);

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
    /// <exception cref="InvalidOperationException">Thrown when the buffer is not initialized yet.</exception>
    public List<IQData> GetSamplesFromAsyncBuffer(int maxCount)
    {
        // Check the buffer. It can be reachable, if there is an async reading.
        if (_asyncBuffer == null)
        {
            throw new InvalidOperationException(
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
    /// <exception cref="InvalidOperationException">
    /// Thrown when raw buffer mode is not active or <see cref="StartReadSamplesAsync"/> has not been called.
    /// </exception>
    public RawSampleBuffer? GetRawSamplesFromAsyncBuffer()
    {
        if (_rawAsyncChannel == null)
        {
            throw new InvalidOperationException(
                "The raw async channel is not initialized yet. " +
                "Set UseRawBufferMode = true and call StartReadSamplesAsync first.");
        }

        return _rawAsyncChannel.Reader.TryRead(out RawSampleBuffer? buffer) ? buffer : null;
    }

    /// <summary>
    /// Callback function for async reading (I/Q).
    /// No exception may escape to the native caller: an exception which crosses the
    /// native boundary terminates the process. Errors are recorded via
    /// <see cref="FailAsyncRead"/>, which stops the reading instead.
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

        try
        {
            target.ProcessSamplesFromCallback(buf, len);
        }
        catch (Exception ex)
        {
            // Record the error and stop the reading; never rethrow to the native caller.
            target.FailAsyncRead(ex);
        }
    }

    /// <summary>
    /// Process the samples delivered by the native callback.
    /// </summary>
    /// <param name="buf">Buffer to store samples.</param>
    /// <param name="len">Length of the buffer.</param>
    private unsafe void ProcessSamplesFromCallback(byte* buf, uint len)
    {
        if (_activeRawBufferMode)
        {
            // Raw buffer mode: rent from pool, memcpy, hand off via Channel.
            Channel<RawSampleBuffer> channel = _rawAsyncChannel!;
            int byteLength = (int)len;

            // Check backpressure: if the channel is full, drop the samples. If dropping
            // was not asked, record the error as well, which stops the reading.
            if (channel.Reader.Count >= _rawChannelCapacity)
            {
                DroppedSamplesCount += (uint)(byteLength / 2);

                if (!DropSamplesOnFullBuffer)
                {
                    FailAsyncRead(new RtlSdrManagedDeviceException(
                        "The raw async channel of the managed device is full. " +
                        $"Current channel usage: {channel.Reader.Count} buffers, " +
                        $"Maximum capacity: {_rawChannelCapacity} buffers, " +
                        $"Device index: {DeviceInfo.Index}."));
                }

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
                DroppedSamplesCount += (uint)(byteLength / 2);
                return;
            }

            // Raise the SampleAvailable event (skip allocation if no subscribers).
            if (SamplesAvailable != null)
            {
                OnSamplesAvailable(new SamplesAvailableEventArgs(byteLength / 2));
            }
        }
        else
        {
            // Legacy IQData mode: existing implementation.

            // Count of I/Q data.
            int length = (int)len / 2;

            // The buffer is guaranteed to be non-null when the callback is active.
            ConcurrentQueue<IQData> buffer = _asyncBuffer!;

            // Check the async buffer usage: if the buffer is full, drop the samples.
            // If dropping was not asked, record the error as well, which stops the reading.
            if (buffer.Count + length >= MaxAsyncBufferSize)
            {
                DroppedSamplesCount += (uint)length;

                if (!DropSamplesOnFullBuffer)
                {
                    FailAsyncRead(new RtlSdrManagedDeviceException(
                        "The async buffer of the managed device is full. " +
                        $"Current buffer size: {buffer.Count + length} I/Q samples, " +
                        $"Maximum buffer size: {MaxAsyncBufferSize} I/Q samples, " +
                        $"Device index: {DeviceInfo.Index}."));
                }

                return;
            }

            // Construct and enqueue each I/Q sample in a single pass.
            // Avoids the intermediate IQData[] allocation (GC pressure in the hot path).
            for (int i = 0; i < length; i++)
            {
                buffer.Enqueue(new IQData(*buf++, *buf++));
            }

            // Raise the SampleAvailable event (skip allocation if no subscribers).
            if (SamplesAvailable != null)
            {
                OnSamplesAvailable(new SamplesAvailableEventArgs(length));
            }
        }
    }

    /// <summary>
    /// Record the first error captured during asynchronous reading.
    /// Subsequent errors are ignored; the first error wins.
    /// </summary>
    /// <param name="ex">The captured error.</param>
    private void RecordAsyncError(Exception ex) =>
        Interlocked.CompareExchange(ref _asyncReadException, ex, null);

    /// <summary>
    /// Record the error and request the asynchronous reading to stop.
    /// The error is thrown by <see cref="StopReadSamplesAsync"/> and is available
    /// via <see cref="LastAsyncException"/>.
    /// </summary>
    /// <param name="ex">The captured error.</param>
    private void FailAsyncRead(Exception ex)
    {
        RecordAsyncError(ex);

        // Request the reading to stop; best effort, the result is intentionally ignored.
        _ = LibRtlSdr.rtlsdr_cancel_async(_deviceHandle!);
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
        // Read from device. The call blocks until the reading is canceled or fails.
        int returnCode = LibRtlSdr.rtlsdr_read_async(_deviceHandle!, _asyncCallback,
            (IntPtr)_deviceContext, 0, (uint)readLength!);

        // A nonzero code means the reading ended on its own (e.g. device failure);
        // a requested cancel returns zero. Record the error, so it is not lost:
        // StopReadSamplesAsync throws it, LastAsyncException exposes it.
        if (returnCode != 0)
        {
            RecordAsyncError(new RtlSdrLibraryExecutionException(
                "Problem happened during asynchronous data reading from the device. " +
                "The reading stopped unexpectedly. " +
                $"Error code: {returnCode}, device index: {DeviceInfo.Index}."));
        }
    }

    /// <summary>
    /// Validate the amount of requested samples for asynchronous reading.
    /// The byte size of a device read (requested samples * 2) must be a multiple of 512.
    /// </summary>
    /// <param name="requestedSamples">Amount of requested samples by one device read.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the value is not supported.</exception>
    internal static void ValidateRequestedSamples(uint requestedSamples)
    {
        if (requestedSamples == 0 || requestedSamples > uint.MaxValue / 2 ||
            (requestedSamples * 2) % 512 != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(requestedSamples), requestedSamples,
                "Requested sample count must be greater than zero, and its byte size " +
                "(requested samples * 2) must be a multiple of 512.");
        }
    }

    /// <summary>
    /// Start reading samples (I/Q) from the device asynchronously.
    /// </summary>
    /// <param name="requestedSamples">Amount of requested samples by one device read.
    /// The byte size (requested samples * 2) must be a multiple of 512.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when requestedSamples is not supported.</exception>
    /// <exception cref="RtlSdrLibraryExecutionException"></exception>
    public void StartReadSamplesAsync(uint requestedSamples = AsyncDefaultReadLength)
    {
        // Validate the requested amount before touching any state.
        ValidateRequestedSamples(requestedSamples);

        // Check the worker thread.
        if (_asyncWorker != null)
        {
            throw new RtlSdrLibraryExecutionException(
                "Problem happened during asynchronous data reading from the device. " +
                $"The worker thread is already started. Device index: {DeviceInfo.Index}.");
        }

        // Capture the buffer mode for this reading session.
        _activeRawBufferMode = UseRawBufferMode;

        // Initialize the appropriate buffer based on mode.
        if (_activeRawBufferMode)
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

        // Set the device context for the native callback. The handle intentionally
        // roots the device while the reading is active; it is released by
        // StopReadSamplesAsync after the worker thread has finished.
        _deviceContext = GCHandle.Alloc(this);

        // Start the worker with highest priority.
        _asyncWorker = new Thread(SamplesAsyncReader)
        {
            Priority = ThreadPriority.Highest
        };
        _asyncWorker.Start(requestedSamples * 2);
    }

    /// <summary>
    /// Stop reading samples from the device.
    /// If an error was captured during the reading (see <see cref="LastAsyncException"/>),
    /// it is thrown from here.
    /// </summary>
    /// <exception cref="RtlSdrLibraryExecutionException">Thrown when the reading cannot be stopped.</exception>
    /// <exception cref="RtlSdrManagedDeviceException">Thrown when an error was captured during the reading.</exception>
    public void StopReadSamplesAsync()
    {
        // Check if the worker thread is running
        if (_asyncWorker == null)
        {
            return;
        }

        // Cancel the reading with the native function. Do not throw on failure here:
        // the callback may have canceled the reading already (on a captured error),
        // in which case this request fails although the reading is finished.
        int cancelReturnCode = LibRtlSdr.rtlsdr_cancel_async(_deviceHandle!);

        // Wait for the worker thread to finish. The join is bounded: if the cancel
        // request genuinely failed while the device keeps streaming, an unbounded
        // join would never return. On timeout, keep all state intact (worker, buffers,
        // device context), so the caller can retry.
        if (!_asyncWorker.Join(TimeSpan.FromSeconds(5)))
        {
            throw new RtlSdrLibraryExecutionException(
                "Problem happened during stopping asynchronous data reading. " +
                "The reading did not stop in time. " +
                $"Error code: {cancelReturnCode}, device index: {DeviceInfo.Index}.");
        }

        // Release the worker.
        _asyncWorker = null;

        // Release the device context handle; the callback can no longer fire.
        if (_deviceContext.IsAllocated)
        {
            _deviceContext.Free();
        }

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

        // If an error was captured during the reading, throw it now.
        // A nonzero cancel code on an already-finished reading is not an error.
        Exception? asyncReadException = Interlocked.Exchange(ref _asyncReadException, null);
        if (asyncReadException != null)
        {
            throw new RtlSdrManagedDeviceException(
                "Problem happened during asynchronous data reading from the device. " +
                $"Device index: {DeviceInfo.Index}.", asyncReadException);
        }
    }

    #endregion
}
