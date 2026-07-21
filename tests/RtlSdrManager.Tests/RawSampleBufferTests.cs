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
using Xunit;

namespace RtlSdrManager.Tests;

/// <summary>
/// Unit tests for <see cref="RawSampleBuffer"/>.
/// </summary>
public class RawSampleBufferTests
{
    [Fact]
    public void Properties_ReflectPooledBuffer()
    {
        byte[] pooled = ArrayPool<byte>.Shared.Rent(1024);
        var buffer = new RawSampleBuffer(pooled, 1024);

        // The pool may return a larger array than requested; ByteLength is the
        // valid length, Data may be longer.
        Assert.Same(pooled, buffer.Data);
        Assert.Equal(1024, buffer.ByteLength);
        Assert.Equal(512, buffer.SampleCount);
        Assert.True(buffer.Data.Length >= buffer.ByteLength);

        buffer.Return();
    }

    [Fact]
    public void SampleCount_IsHalfOfByteLength()
    {
        byte[] pooled = ArrayPool<byte>.Shared.Rent(16384);
        var buffer = new RawSampleBuffer(pooled, 16384);

        Assert.Equal(8192, buffer.SampleCount);

        buffer.Return();
    }

    [Fact]
    public void Return_HandsBufferBackToPool()
    {
        byte[] pooled = ArrayPool<byte>.Shared.Rent(4096);
        var buffer = new RawSampleBuffer(pooled, 4096);

        // Return must not throw; afterwards the array can be rented again.
        buffer.Return();
        byte[] rentedAgain = ArrayPool<byte>.Shared.Rent(4096);
        ArrayPool<byte>.Shared.Return(rentedAgain);
    }
}
