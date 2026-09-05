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

namespace RtlSdrManager.Tools.HwHealth;

/// <summary>
/// What a measurement observed, with no opinion about whether it was good.
/// </summary>
/// <param name="DeliveredSamples">Samples the device delivered during the window.</param>
/// <param name="Window">How long the measurement window lasted.</param>
/// <param name="DroppedSamples">Samples discarded because the buffer was full.</param>
/// <param name="CounterInOrder">Whether the demodulator's counter always arrived in order.</param>
/// <param name="RequestedSampleRateHz">The rate the device was asked for.</param>
/// <param name="DeliveryStarted">Whether any samples arrived at all.</param>
/// <param name="Error">An error the reading recorded, or null.</param>
/// <remarks>
/// Separating this from the verdict is the point of the split: <see cref="DeliveryProbe"/>
/// produces one of these and judges nothing, and <see cref="HealthVerdict"/> judges without
/// touching hardware. The threshold can then be reasoned about, and changed, without going
/// near the streaming code.
/// </remarks>
internal sealed record ProbeResult(
    long DeliveredSamples,
    TimeSpan Window,
    uint DroppedSamples,
    bool CounterInOrder,
    uint RequestedSampleRateHz,
    bool DeliveryStarted,
    Exception? Error)
{
    /// <summary>
    /// Samples per second the device actually delivered.
    /// </summary>
    public double SamplesPerSecond =>
        Window.TotalSeconds > 0 ? DeliveredSamples / Window.TotalSeconds : 0;

    /// <summary>
    /// Delivered rate as a fraction of the rate the device was asked for.
    /// </summary>
    /// <remarks>
    /// This is the number that separates a working device from a degraded one. A healthy
    /// dongle sits near 1; a dongle that has been cycled a few hundred times sits far below it
    /// while still opening and still delivering a first buffer.
    /// </remarks>
    public double ThroughputFraction =>
        RequestedSampleRateHz > 0 ? SamplesPerSecond / RequestedSampleRateHz : 0;

    /// <summary>
    /// A result for a device that never delivered anything.
    /// </summary>
    /// <param name="requestedSampleRateHz">The rate the device was asked for.</param>
    /// <param name="waited">How long delivery was waited for.</param>
    /// <returns>The result.</returns>
    public static ProbeResult NeverDelivered(uint requestedSampleRateHz, TimeSpan waited) =>
        new(0, waited, 0, true, requestedSampleRateHz, false, null);
}
