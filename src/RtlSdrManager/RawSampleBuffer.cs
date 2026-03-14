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

using System.Buffers;

namespace RtlSdrManager;

/// <summary>
/// Represents a raw I/Q sample buffer from the RTL-SDR device.
/// Contains interleaved I/Q byte pairs: [I0, Q0, I1, Q1, ...].
/// The buffer is rented from <see cref="ArrayPool{T}"/> and must be returned after processing
/// by calling <see cref="Return"/>.
/// </summary>
public sealed class RawSampleBuffer
{
    /// <summary>
    /// Raw interleaved I/Q bytes.
    /// Length may exceed <see cref="ByteLength"/> because the buffer is rented from
    /// <see cref="ArrayPool{T}"/>, which may return a larger array than requested.
    /// </summary>
    public byte[] Data { get; }

    /// <summary>
    /// Number of valid bytes in <see cref="Data"/> (always even).
    /// </summary>
    public int ByteLength { get; }

    /// <summary>
    /// Number of I/Q sample pairs (ByteLength / 2).
    /// </summary>
    public int SampleCount => ByteLength / 2;

    /// <summary>
    /// Create an instance of <see cref="RawSampleBuffer"/>.
    /// </summary>
    /// <param name="data">Pooled byte array containing raw I/Q data.</param>
    /// <param name="byteLength">Number of valid bytes in the array.</param>
    internal RawSampleBuffer(byte[] data, int byteLength)
    {
        Data = data;
        ByteLength = byteLength;
    }

    /// <summary>
    /// Returns the underlying buffer to <see cref="ArrayPool{T}"/>.
    /// Must be called exactly once after processing is complete.
    /// </summary>
    public void Return() => ArrayPool<byte>.Shared.Return(Data);
}
