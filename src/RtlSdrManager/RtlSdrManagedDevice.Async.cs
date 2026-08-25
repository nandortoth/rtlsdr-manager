// RTL-SDR Manager Library for .NET
// Copyright (C) 2018-2026 Nandor Toth <dev@nandortoth.com>
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
    /// Transfer buffer count captured at StartReadSamplesAsync, for the same reason as
    /// <see cref="_activeRawBufferMode"/>: the reading is configured once, when it starts.
    /// </summary>
    private uint _activeTransferBufferCount;

    /// <summary>
    /// Backing field of <see cref="TransferBufferCount"/>.
    /// </summary>
    private uint _transferBufferCount = DefaultTransferBufferCount;

    /// <summary>
    /// Backing field of <see cref="MaxAsyncBufferSize"/>.
    /// </summary>
    private uint _maxAsyncBufferSize;

    /// <summary>
    /// Backing field of <see cref="DropSamplesOnFullBuffer"/>.
    /// </summary>
    private bool _dropSamplesOnFullBuffer;

    /// <summary>
    /// Backing field of <see cref="UseRawBufferMode"/>.
    /// </summary>
    private bool _useRawBufferMode;

    /// <summary>
    /// Backing field of <see cref="DroppedSamplesCount"/>.
    /// </summary>
    /// <remarks>
    /// A field rather than an auto-property because the interlocked helpers take it by
    /// reference. The native callback thread adds to it while the caller's thread may reset
    /// it, so every access goes through <see cref="Interlocked"/> or <see cref="Volatile"/>.
    /// </remarks>
    private uint _droppedSamplesCount;

    /// <summary>
    /// True when the current asynchronous reading was asked to stop (by
    /// StopReadSamplesAsync or by a captured error). Used to tell a requested stop
    /// apart from the reading stopping on its own: on a requested stop the native
    /// read can report a nonzero code (canceling already-completed transfers), which
    /// must not be reported as an error.
    /// </summary>
    private volatile bool _stopRequested;

    /// <summary>
    /// Default amount of requested samples from RTL-SDR device.
    /// </summary>
    private const uint AsyncDefaultReadLength = 16384;

    /// <summary>
    /// Default value of <see cref="TransferBufferCount"/>.
    /// </summary>
    public const uint DefaultTransferBufferCount = 15;

    /// <summary>
    /// Smallest accepted <see cref="TransferBufferCount"/>.
    /// </summary>
    private const uint MinimumTransferBufferCount = 1;

    /// <summary>
    /// Largest accepted <see cref="TransferBufferCount"/>.
    /// </summary>
    private const uint MaximumTransferBufferCount = 64;

    /// <summary>
    /// Largest accepted sample count for a single read, which is 16 MiB of samples.
    /// </summary>
    /// <remarks>
    /// Two bytes per sample, so this is 8388608 samples. The device's own default read is
    /// 256 KiB, so this leaves 64x headroom while keeping a mistyped value from becoming a
    /// multi-gigabyte allocation.
    /// </remarks>
    internal const uint MaximumRequestedSamples = 8 * 1024 * 1024;

    /// <summary>
    /// Largest accepted total allocation across every transfer buffer, in bytes.
    /// </summary>
    /// <remarks>
    /// The per-read and per-buffer-count limits are individually reasonable but multiply:
    /// 16 MiB across 64 buffers would reach 1 GiB. This bounds the product.
    /// </remarks>
    internal const long MaximumTotalTransferBytes = 256L * 1024 * 1024;

    /// <summary>
    /// Event to notify subscribers, if the new samples are available.
    /// </summary>
    /// <remarks>
    /// Subscribing to a disposed device is not blocked: it cannot raise anything, so the
    /// subscription is inert rather than wrong, and guarding an event's accessors would be
    /// unusual enough to surprise callers.
    /// </remarks>
    public event EventHandler<SamplesAvailableEventArgs>? SamplesAvailable;

    /// <summary>
    /// Accessor for the async I/Q buffer.
    /// </summary>
    /// <exception cref="ObjectDisposedException">Thrown when the device is disposed.</exception>
    /// <exception cref="InvalidOperationException">Thrown when no reading has been started.</exception>
    public ConcurrentQueue<IQData> AsyncBuffer
    {
        get
        {
            // Checked first, so a disposed device does not report itself as merely unstarted:
            // ending a reading releases the buffer, and disposing ends any reading.
            ThrowIfDisposed();

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
    /// <remarks>
    /// Readable after the device is disposed, so configuration can still be inspected; setting
    /// it then throws, because the value could never take effect.
    /// </remarks>
    /// <exception cref="ObjectDisposedException">Thrown when setting it on a disposed device.</exception>
    public uint MaxAsyncBufferSize
    {
        // The getter is deliberately unguarded: the native callback thread reads it.
        get => _maxAsyncBufferSize;
        set
        {
            ThrowIfDisposed();
            _maxAsyncBufferSize = value;
        }
    }

    /// <summary>
    /// Define the behavior if the buffer is full.
    /// Drop samples (true), or throw exception (false).
    /// Applies to both IQData mode and raw buffer mode.
    /// </summary>
    /// <remarks>
    /// Readable after the device is disposed, so configuration can still be inspected; setting
    /// it then throws, because the value could never take effect.
    /// </remarks>
    /// <exception cref="ObjectDisposedException">Thrown when setting it on a disposed device.</exception>
    public bool DropSamplesOnFullBuffer
    {
        // The getter is deliberately unguarded: the native callback thread reads it.
        get => _dropSamplesOnFullBuffer;
        set
        {
            ThrowIfDisposed();
            _dropSamplesOnFullBuffer = value;
        }
    }

    /// <summary>
    /// Counter for dropped I/Q samples.
    /// It is possible to reset the counter with <see cref="ResetDroppedSamplesCounter"/>.
    /// </summary>
    /// <remarks>
    /// The counter is written from the thread delivering samples and read from the caller's,
    /// so it is updated atomically and read through a barrier: a reset cannot be lost, and a
    /// reader never sees a partially applied update.
    /// <para>
    /// Readable after the device is disposed, deliberately, for the same reason as
    /// <see cref="AsyncReadException"/>: how many samples a finished session lost is a fair
    /// question afterward. <see cref="ResetDroppedSamplesCounter"/> is guarded, because that
    /// one is writing.
    /// </para>
    /// </remarks>
    public uint DroppedSamplesCount => Volatile.Read(ref _droppedSamplesCount);

    /// <summary>
    /// When true, the device uses raw buffer mode: samples are delivered as raw byte[]
    /// buffers via <see cref="Channel{T}"/> instead of per-sample <see cref="IQData"/> objects
    /// via <see cref="ConcurrentQueue{T}"/>.
    /// Must be set before calling <see cref="StartReadSamplesAsync"/>; changes take
    /// effect at the next <see cref="StartReadSamplesAsync"/> call. Default: false.
    /// </summary>
    /// <exception cref="ObjectDisposedException">Thrown when setting it on a disposed device.</exception>
    public bool UseRawBufferMode
    {
        get => _useRawBufferMode;
        set
        {
            ThrowIfDisposed();
            _useRawBufferMode = value;
        }
    }

    /// <summary>
    /// Number of buffers the device fills in rotation while a reading is running.
    /// Default: <see cref="DefaultTransferBufferCount"/>.
    /// </summary>
    /// <remarks>
    /// This is the main control over how much slack the reading has. More buffers absorb
    /// longer pauses in the consumer before samples are lost, at the cost of more memory and
    /// higher worst-case latency between capture and delivery; fewer buffers do the reverse.
    /// The default suits most applications, so leave it alone unless samples are being
    /// dropped or latency matters more than tolerance.
    /// <para>
    /// Must be set before <see cref="StartReadSamplesAsync"/>; the value is captured when the
    /// reading starts, so changing it during a reading has no effect until the next one.
    /// </para>
    /// </remarks>
    /// <exception cref="ObjectDisposedException">Thrown when setting it on a disposed device.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when the value is outside 1 to 64.
    /// </exception>
    public uint TransferBufferCount
    {
        get => _transferBufferCount;
        set
        {
            ThrowIfDisposed();
            ValidateTransferBufferCount(value);
            _transferBufferCount = value;
        }
    }

    /// <summary>
    /// The error that stopped the current (or most recent) asynchronous reading, or null if
    /// no error happened. An error is captured when the buffer is full and
    /// <see cref="DropSamplesOnFullBuffer"/> is false, when a <see cref="SamplesAvailable"/>
    /// handler throws, or when the device read fails on its own. The value is reset when a new
    /// reading starts; <see cref="StopReadSamplesAsync"/> also throws it.
    /// </summary>
    /// <remarks>
    /// Readable after the device is disposed, deliberately. Disposing routes a failed stop
    /// into this property precisely so the reason survives; guarding it would discard the
    /// only account of why the disposal went wrong.
    /// </remarks>
    public Exception? AsyncReadException => Volatile.Read(ref _asyncReadException);

    #endregion

    #region Methods

    /// <summary>
    /// Reset the counter for dropped I/Q samples.
    /// </summary>
    /// <exception cref="ObjectDisposedException">Thrown when the device is disposed. Reading <see cref="DroppedSamplesCount"/> still works.</exception>
    public void ResetDroppedSamplesCounter()
    {
        // Guarded although DroppedSamplesCount is not: reading the count afterward is a fair
        // post-mortem question, resetting it on a device that will never count again is not.
        ThrowIfDisposed();

        Interlocked.Exchange(ref _droppedSamplesCount, 0);
    }

    /// <summary>
    /// Get I/Q samples from the async buffer.
    /// </summary>
    /// <param name="maxCount">Maximum amount of requested I/Q samples. If there are fewer samples in the buffer,
    /// than the requested amount, maxCount will be reduced.</param>
    /// <returns>List if I/Q samples.</returns>
    /// <exception cref="ObjectDisposedException">Thrown when the device is disposed.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when maxCount is negative.</exception>
    /// <exception cref="InvalidOperationException">Thrown when no reading has been started.</exception>
    public List<IQData> GetSamplesFromAsyncBuffer(int maxCount)
    {
        // Guarded directly rather than relying on the AsyncBuffer property below, so the
        // argument check cannot report a problem with the caller's input when the real
        // problem is that the device is gone.
        ThrowIfDisposed();

        // Reject a negative count rather than clamping it to zero. It is nearly always a
        // computed value that went negative, and returning an empty list would hide that at
        // the one point it can still be caught.
        if (maxCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxCount), maxCount,
                "The maximum sample count cannot be negative.");
        }

        // The AsyncBuffer property throws InvalidOperationException if the async reading
        // has not been started yet.
        ConcurrentQueue<IQData> buffer = AsyncBuffer;

        // Check the available samples in the async buffer.
        if (maxCount > buffer.Count)
        {
            maxCount = buffer.Count;
        }

        // Initialize the local buffer with pre-allocated capacity.
        var iqData = new List<IQData>(maxCount);

        // Dequeue of the samples from the async buffer.
        for (int i = 0; i < maxCount; i++)
        {
            if (!buffer.TryDequeue(out IQData data))
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
    /// <exception cref="ObjectDisposedException">Thrown when the device is disposed.</exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when raw buffer mode is not active or <see cref="StartReadSamplesAsync"/> has not been called.
    /// </exception>
    public RawSampleBuffer? GetRawSamplesFromAsyncBuffer()
    {
        // Checked first, for the same reason as AsyncBuffer: after disposal the channel is
        // gone, and reporting that as "not initialized yet" sends the caller to start a
        // reading on a device that can no longer take one.
        ThrowIfDisposed();

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
        // Once a stop or a fault was requested, cancellation is asynchronous and some
        // in-flight transfers still arrive. Skip them: no work, no wasted allocations,
        // and the stopping error (if any) is already recorded.
        if (_stopRequested)
        {
            return;
        }

        if (_activeRawBufferMode)
        {
            // Raw buffer mode: rent from pool, memcpy, hand off via Channel.
            Channel<RawSampleBuffer> channel = _rawAsyncChannel!;
            int byteLength = (int)len;

            // Check backpressure: if the channel is full, drop the samples. If dropping
            // was not asked, record the error as well, which stops the reading.
            if (channel.Reader.Count >= _rawChannelCapacity)
            {
                Interlocked.Add(ref _droppedSamplesCount, (uint)(byteLength / 2));

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
                Interlocked.Add(ref _droppedSamplesCount, (uint)(byteLength / 2));
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
                Interlocked.Add(ref _droppedSamplesCount, (uint)length);

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
    /// via <see cref="AsyncReadException"/>.
    /// </summary>
    /// <param name="ex">The captured error.</param>
    private void FailAsyncRead(Exception ex)
    {
        RecordAsyncError(ex);

        // Request the reading to stop; best effort, the result is intentionally ignored.
        _ = RequestStopReading();
    }

    /// <summary>
    /// Request the native asynchronous reading to stop.
    /// Sets <see cref="_stopRequested"/> before canceling, so the worker thread ignores the
    /// nonzero code a requested cancel can produce. This ordering is a correctness invariant;
    /// both the callback (via <see cref="FailAsyncRead"/>) and <see cref="StopReadSamplesAsync"/>
    /// go through here so it lives in one place.
    /// </summary>
    /// <returns>The return code of the native cancel call.</returns>
    private int RequestStopReading()
    {
        _stopRequested = true;
        return LibRtlSdr.rtlsdr_cancel_async(_deviceHandle!);
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
        // The buffer count is the value captured at StartReadSamplesAsync rather than 0:
        // zero means "use your own default" to the native layer, which is what made
        // TransferBufferCount unreachable before it was exposed.
        int returnCode = LibRtlSdr.rtlsdr_read_async(_deviceHandle!, _asyncCallback,
            (IntPtr)_deviceContext, _activeTransferBufferCount, (uint)readLength!);

        // A nonzero code only means an error when the reading ended on its own
        // (e.g. device failure). On a requested stop, librtlsdr reports the last
        // internal transfer code, which is often nonzero (canceling transfers that
        // already completed yields -5), so it must be ignored. Record real errors,
        // so they are not lost: StopReadSamplesAsync throws them,
        // AsyncReadException exposes them.
        if (returnCode != 0 && !_stopRequested)
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
        if (requestedSamples == 0 || requestedSamples > MaximumRequestedSamples ||
            (requestedSamples * 2) % 512 != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(requestedSamples), requestedSamples,
                "Requested sample count must be greater than zero and at most " +
                $"{MaximumRequestedSamples}, and its byte size (requested samples * 2) " +
                "must be a multiple of 512.");
        }
    }

    /// <summary>
    /// Validate a requested transfer buffer count.
    /// </summary>
    /// <param name="transferBufferCount">Number of buffers filled in rotation.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when the count is outside <see cref="MinimumTransferBufferCount"/> to
    /// <see cref="MaximumTransferBufferCount"/>.
    /// </exception>
    /// <remarks>
    /// Extracted from the <see cref="TransferBufferCount"/> setter so it can be tested
    /// without a device, in the same way as the tuner gain and GPIO validation. The
    /// parameter name rather than <c>value</c> reaches the caller, which names the property
    /// being set instead of the setter's hidden argument.
    /// <para>
    /// The upper bound is this library's own. The native layer accepts any nonzero count and
    /// allocates that many buffers without a ceiling of its own, so nothing below these
    /// rejects an absurd value.
    /// </para>
    /// </remarks>
    internal static void ValidateTransferBufferCount(uint transferBufferCount)
    {
        if (transferBufferCount is < MinimumTransferBufferCount or > MaximumTransferBufferCount)
        {
            throw new ArgumentOutOfRangeException(nameof(transferBufferCount), transferBufferCount,
                $"The transfer buffer count must be between {MinimumTransferBufferCount} " +
                $"and {MaximumTransferBufferCount}.");
        }
    }

    /// <summary>
    /// Validate the memory a reading would need across every transfer buffer.
    /// </summary>
    /// <param name="requestedSamples">Samples requested per read.</param>
    /// <param name="transferBufferCount">Number of buffers filled in rotation.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when the total exceeds <see cref="MaximumTotalTransferBytes"/>.
    /// </exception>
    /// <remarks>
    /// Each value is checked on its own where it is set, but they multiply, and neither
    /// check can see the other. The native layer allocates one buffer of the requested size
    /// per transfer buffer and bounds neither the size nor the count, so this is the only
    /// place the product is caught before the allocation is attempted.
    /// </remarks>
    internal static void ValidateTotalTransferSize(uint requestedSamples, uint transferBufferCount)
    {
        // Int128, because the product of two uint values doubled does not fit in long or
        // ulong: at the extremes it reaches about 3.7e19 against an ulong ceiling of 1.8e19,
        // and a wrapped total compares as small enough to pass. The real call path validates
        // both operands first, but this method is reachable on its own.
        Int128 totalBytes = (Int128)requestedSamples * 2 * transferBufferCount;

        if (totalBytes > MaximumTotalTransferBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(requestedSamples), requestedSamples,
                $"Reading {requestedSamples} samples into {transferBufferCount} buffers needs " +
                $"{totalBytes / (1024 * 1024)} MiB, which is above the " +
                $"{MaximumTotalTransferBytes / (1024 * 1024)} MiB limit. Reduce the requested " +
                $"samples, or lower {nameof(TransferBufferCount)}.");
        }
    }

    /// <summary>
    /// Start reading samples (I/Q) from the device asynchronously.
    /// </summary>
    /// <remarks>
    /// To read again after stopping, close the device and open it again rather than calling
    /// this a second time on the same instance. Ending a reading releases its transfer buffers
    /// without waiting for every canceled transfer to report, and on macOS a late completion
    /// can then fault inside the USB layer and terminate the process.
    /// <para>
    /// The risk grows with the number of readings a process performs, and closing in between
    /// lowers it without removing it. Measured with a harness that reads and stops six times in
    /// one process, about half of runs crashed when restarting on the same instance and about
    /// one in six when reopening between readings. An application that streams once per device,
    /// which is the usual shape, does not meet the problem at all.
    /// </para>
    /// </remarks>
    /// <param name="requestedSamples">Amount of requested samples by one device read.
    /// The byte size (requested samples * 2) must be a multiple of 512.</param>
    /// <exception cref="ObjectDisposedException">Thrown when the device is disposed.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when requestedSamples is not supported.</exception>
    /// <exception cref="RtlSdrLibraryExecutionException"></exception>
    public void StartReadSamplesAsync(uint requestedSamples = AsyncDefaultReadLength)
    {
        // FIRST, before anything else. Without this the method runs to completion and starts
        // the worker, which hands the released device handle to the native reader on a thread
        // with no exception handler: that terminates the process rather than throwing here.
        ThrowIfDisposed();

        // Validate the requested amount before touching any state, then the total the
        // reading would allocate: the per-read and per-buffer limits pass individually but
        // multiply, and this is the first point where both values are known.
        ValidateRequestedSamples(requestedSamples);
        ValidateTotalTransferSize(requestedSamples, TransferBufferCount);

        // Check the worker thread.
        if (_asyncWorker != null)
        {
            throw new RtlSdrLibraryExecutionException(
                "Problem happened during asynchronous data reading from the device. " +
                $"The worker thread is already started. Device index: {DeviceInfo.Index}.");
        }

        // Capture the buffer mode and transfer buffer count for this reading session, and
        // reset the per-session state (stop request and captured error) of any previous one.
        _activeRawBufferMode = UseRawBufferMode;
        _activeTransferBufferCount = TransferBufferCount;
        _stopRequested = false;
        _asyncReadException = null;

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

        // Start the worker with the highest priority. The thread is a background thread, so a
        // worker left running on a wedged device (see StopReadSamplesAsync / Dispose) cannot
        // block process exit.
        _asyncWorker = new Thread(SamplesAsyncReader)
        {
            Priority = ThreadPriority.Highest,
            IsBackground = true
        };

        try
        {
            _asyncWorker.Start(requestedSamples * 2);
        }
        catch
        {
            // The worker could not be started (e.g. the OS refused a new thread). Roll back
            // so no half-initialized session (an unstarted worker plus an allocated GC
            // handle) lingers to trip StopReadSamplesAsync / Dispose later.
            _asyncWorker = null;
            if (_deviceContext.IsAllocated)
            {
                _deviceContext.Free();
            }

            throw;
        }
    }

    /// <summary>
    /// Stop reading samples from the device.
    /// If an error was captured during the reading (see <see cref="AsyncReadException"/>),
    /// it is thrown from here.
    /// </summary>
    /// <remarks>
    /// Stopping leaves the device open and configured, but it is not a safe point to begin
    /// another reading from: see the remarks on <see cref="StartReadSamplesAsync"/>. Close the
    /// device and open it again if more samples are needed.
    /// </remarks>
    /// <exception cref="ObjectDisposedException">Thrown when the device is disposed. On a live device, stopping when no reading is running is harmless.</exception>
    /// <exception cref="RtlSdrLibraryExecutionException">Thrown when the reading cannot be stopped.</exception>
    /// <exception cref="RtlSdrManagedDeviceException">Thrown when an error was captured during the reading.</exception>
    public void StopReadSamplesAsync()
    {
        // Guarded before the no-op path below. Stopping a reading that is not running is
        // harmless and stays that way, but on a disposed device it is a use-after-dispose
        // that would otherwise succeed silently.
        ThrowIfDisposed();

        // Check if the worker thread is running
        if (_asyncWorker == null)
        {
            return;
        }

        // Mark the stop as requested (so the worker ignores the nonzero native code a
        // requested cancel can produce) and cancel the reading. Do not throw on failure
        // here: the callback may have canceled the reading already (on a captured error),
        // in which case this request fails although the reading is finished.
        int cancelReturnCode = RequestStopReading();

        // Wait for the worker thread to finish. The join is bounded: if the cancel
        // request genuinely failed while the device keeps streaming, an unbounded
        // join would never return. On timeout, keep all state intact and throw: the caller
        // can retry, or Dispose will leak the resources safely rather than free them under a
        // still-running native callback. That covers the worker, the buffers, the device
        // context, and the stop request, which the worker still reads when the native read
        // eventually returns.
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

        // If an error was captured during the reading, throw it now. The slot is not
        // cleared: the error stays observable via AsyncReadException until the next
        // StartReadSamplesAsync resets it. A nonzero cancel code on an already-finished
        // reading is not an error.
        Exception? asyncReadException = Volatile.Read(ref _asyncReadException);
        if (asyncReadException != null)
        {
            throw new RtlSdrManagedDeviceException(
                "Problem happened during asynchronous data reading from the device. " +
                $"Device index: {DeviceInfo.Index}.", asyncReadException);
        }
    }

    #endregion
}
