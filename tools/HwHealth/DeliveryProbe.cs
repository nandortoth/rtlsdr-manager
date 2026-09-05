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
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using RtlSdrManager.Modes;
using RtlSdrManager.Tools.Common;

namespace RtlSdrManager.Tools.HwHealth;

/// <summary>
/// Streams from one device for a fixed window and records what arrived.
/// </summary>
/// <remarks>
/// One instance per measurement, because it accumulates a count across a native callback.
/// It judges nothing: everything it observes goes into a <see cref="ProbeResult"/> for
/// <see cref="HealthVerdict"/> to interpret.
/// </remarks>
internal sealed class DeliveryProbe
{
    /// <summary>
    /// Sample rate requested for the measurement, matching the one the harness uses.
    /// </summary>
    public const uint SampleRateHz = 2_048_000;

    /// <summary>
    /// How many samples to take from the buffer per drain.
    /// </summary>
    private const int DrainBatchSize = 16384;

    /// <summary>
    /// How long to wait for the first samples before concluding none are coming.
    /// </summary>
    private static readonly TimeSpan FirstSampleDeadline = TimeSpan.FromSeconds(5);

    /// <summary>
    /// The device being probed.
    /// </summary>
    private readonly RtlSdrManagedDevice _device;

    /// <summary>
    /// Samples the device has delivered, accumulated on the native callback thread.
    /// </summary>
    private long _delivered;

    /// <summary>
    /// Create a probe for one device.
    /// </summary>
    /// <param name="device">The device to probe.</param>
    public DeliveryProbe(RtlSdrManagedDevice device)
    {
        _device = device;
    }

    /// <summary>
    /// Stream for the requested window and report what arrived.
    /// </summary>
    /// <param name="window">How long to stream for.</param>
    /// <returns>What was observed.</returns>
    /// <remarks>
    /// Delivery is counted in the event handler rather than by draining, so the figure is what
    /// the device produced and not what this process managed to consume. A slow consumer would
    /// otherwise understate the very throughput being measured.
    /// </remarks>
    public ProbeResult Run(TimeSpan window)
    {
        Configure();

        _device.SamplesAvailable += OnSamplesAvailable;

        bool counterInOrder = true;
        var elapsed = new Stopwatch();

        try
        {
            _device.StartReadSamplesAsync();

            if (!Deadline.WaitUntil(() => Interlocked.Read(ref _delivered) > 0,
                    FirstSampleDeadline))
            {
                return ProbeResult.NeverDelivered(SampleRateHz, FirstSampleDeadline);
            }

            // Restarted so the rate covers the streaming window rather than the time spent
            // waiting for the tuner to start producing.
            Interlocked.Exchange(ref _delivered, 0);
            elapsed.Restart();

            while (elapsed.Elapsed < window)
            {
                if (!DrainAndCheckCounter())
                {
                    counterInOrder = false;
                }

                Thread.Sleep(1);
            }

            elapsed.Stop();

            return new ProbeResult(
                Interlocked.Read(ref _delivered),
                elapsed.Elapsed,
                _device.DroppedSamplesCount,
                counterInOrder,
                SampleRateHz,
                DeliveryStarted: true,
                _device.AsyncReadException);
        }
        finally
        {
            _device.SamplesAvailable -= OnSamplesAvailable;
            StopQuietly();
        }
    }

    /// <summary>
    /// Put the device into a state where the counter can be asserted.
    /// </summary>
    /// <remarks>
    /// The center frequency comes from the middle of whatever the tuner reports, so this works
    /// on any tuner without naming a band. Nothing is demodulated, so the exact frequency does
    /// not matter. Dropping on a full buffer keeps a slow drain from stalling the device.
    /// </remarks>
    private void Configure()
    {
        _device.SampleRate = Frequency.FromHz(SampleRateHz);

        FrequencyRange range = _device.SupportedFrequencyRanges[0];
        _device.CenterFrequency =
            Frequency.FromHz(range.Minimum.Hz + ((range.Maximum.Hz - range.Minimum.Hz) / 2));

        _device.TestMode = TestModes.Enabled;
        _device.DropSamplesOnFullBuffer = true;
        _device.ResetDeviceBuffer();
    }

    /// <summary>
    /// Accumulate what the device delivered.
    /// </summary>
    /// <param name="sender">The device.</param>
    /// <param name="e">The delivery, whose sample count is all this needs.</param>
    /// <remarks>
    /// Runs on the native callback thread, so it does one interlocked add and nothing else:
    /// anything slower here stalls the transfer pipeline and costs samples.
    /// </remarks>
    private void OnSamplesAvailable(object? sender, SamplesAvailableEventArgs e) =>
        Interlocked.Add(ref _delivered, e.SampleCount);

    /// <summary>
    /// Take a batch from the buffer and check it is the counter, in order.
    /// </summary>
    /// <returns>False only when the data path is at fault.</returns>
    /// <remarks>
    /// A dropped sample legitimately breaks the counter, so a batch spanning one proves nothing
    /// and is not held against the device. Neither is a batch too short to have an order.
    /// </remarks>
    private bool DrainAndCheckCounter()
    {
        uint droppedBefore = _device.DroppedSamplesCount;
        List<IQData> batch = _device.GetSamplesFromAsyncBuffer(DrainBatchSize);

        return batch.Count <= 1 ||
               _device.DroppedSamplesCount != droppedBefore ||
               TestModeCounter.Matches(batch);
    }

    /// <summary>
    /// Stop the reading without letting the stop itself change the verdict.
    /// </summary>
    private void StopQuietly()
    {
        try
        {
            _device.StopReadSamplesAsync();
        }
        catch (Exception)
        {
            // The measurement is already taken. Whether stopping is reliable is the harness's
            // question, not this tool's.
        }
    }
}
