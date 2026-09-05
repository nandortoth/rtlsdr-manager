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

namespace RtlSdrManager.Tools.Common;

/// <summary>
/// Recognizes the counter a device emits while its test mode is on.
/// </summary>
/// <remarks>
/// In test mode the demodulator replaces real samples with an eight bit counter, one step per
/// byte. That makes the received data predictable, so a check can assert that the right bytes
/// arrived in the right order instead of merely that some arrived, and it removes any
/// dependence on an antenna or on what is on the air.
/// </remarks>
public static class TestModeCounter
{
    /// <summary>
    /// How many samples to inspect, which is one USB bulk packet's worth.
    /// </summary>
    /// <remarks>
    /// Inspecting everything would be wrong. A busy host drops a transfer from time to time,
    /// which shows up as a jump in the counter: a measured read of 16384 samples contained one
    /// such jump, around sample 1624. That is a property of the machine rather than a defect
    /// in the library, and it is what rtl_test reports as lost samples. A prefix of 256
    /// samples is 512 bytes, so it sits inside a single packet and cannot span a gap, while
    /// still catching reordering, corruption, or a broken byte-to-sample conversion.
    /// </remarks>
    private const int PrefixSamples = 256;

    /// <summary>
    /// Decide whether samples continue the counter without a break.
    /// </summary>
    /// <param name="samples">Samples to inspect.</param>
    /// <returns>True when each byte is one greater than the one before it.</returns>
    /// <remarks>
    /// The two components of a sample are consecutive bytes of the stream, so the sequence
    /// runs I, Q, I, Q. Only the increments are checked, because the counter is already
    /// running by the time a reading starts.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when samples is null.</exception>
    public static bool Matches(List<IQData> samples)
    {
        if (samples == null)
        {
            throw new ArgumentNullException(nameof(samples), "Samples cannot be null.");
        }

        if (samples.Count < 2)
        {
            return false;
        }

        int inspect = Math.Min(samples.Count, PrefixSamples);
        int expected = samples[0].I;

        for (int index = 0; index < inspect; index++)
        {
            if (samples[index].I != expected || samples[index].Q != Next(expected))
            {
                return false;
            }

            expected = Next(Next(expected));
        }

        return true;
    }

    /// <summary>
    /// Decide whether a raw buffer continues the counter without a break.
    /// </summary>
    /// <param name="buffer">Buffer to inspect.</param>
    /// <returns>True when each byte is one greater than the one before it.</returns>
    /// <exception cref="ArgumentNullException">Thrown when buffer is null.</exception>
    public static bool Matches(RawSampleBuffer buffer)
    {
        if (buffer == null)
        {
            throw new ArgumentNullException(nameof(buffer), "Buffer cannot be null.");
        }

        if (buffer.ByteLength < 2)
        {
            return false;
        }

        // Two bytes per sample, so the same prefix covers the same span of the stream.
        int inspect = Math.Min(buffer.ByteLength, PrefixSamples * 2);
        ReadOnlySpan<byte> raw = buffer.Data.AsSpan(0, inspect);
        int expected = raw[0];

        foreach (byte value in raw)
        {
            if (value != expected)
            {
                return false;
            }

            expected = Next(expected);
        }

        return true;
    }

    /// <summary>
    /// The value the counter takes after the given one.
    /// </summary>
    /// <param name="value">Current counter value.</param>
    /// <returns>The next value, wrapping at 256.</returns>
    private static int Next(int value) => (value + 1) & 0xFF;
}
