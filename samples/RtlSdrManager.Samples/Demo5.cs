// RTL-SDR Manager Library for .NET Core
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
using System.Collections.Concurrent;
using System.Threading;
using RtlSdrManager.Exceptions;
using RtlSdrManager.Modes;

namespace RtlSdrManager.Samples;

/// <summary>
/// Demo for RtlSdrManager.
///
/// In this demo:
///   - Samples will be received asynchronously using raw buffer mode (zero-copy).
///   - Raw I/Q byte buffers are accessed directly without per-sample object allocation.
///   - Buffers are returned to the pool after processing.
/// </summary>
public static class Demo5
{
    /// <summary>
    /// Run the demo.
    /// </summary>
    public static void Run()
    {
        // Initialize the Manager instance.
        RtlSdrDeviceManager manager = RtlSdrDeviceManager.Instance;

        // Open a managed device and set some parameters.
        try
        {
            manager.OpenManagedDevice(0, "my-rtl-sdr");
        }
        catch (RtlSdrDeviceException e)
        {
            Console.WriteLine(e);
            return;
        }
        catch
        {
            Console.WriteLine("Failed to open the RTL-SDR device.");
            return;
        }

        manager["my-rtl-sdr"].CenterFrequency = Frequency.FromMHz(1090);
        manager["my-rtl-sdr"].SampleRate = Frequency.FromMHz(2);
        manager["my-rtl-sdr"].TunerGainMode = TunerGainModes.AGC;
        manager["my-rtl-sdr"].AGCMode = AGCModes.Enabled;
        manager["my-rtl-sdr"].MaxAsyncBufferSize = 512 * 1024;
        manager["my-rtl-sdr"].DropSamplesOnFullBuffer = true;

        // Enable raw buffer mode for zero-copy sample access.
        manager["my-rtl-sdr"].UseRawBufferMode = true;

        manager["my-rtl-sdr"].ResetDeviceBuffer();

        // Start asynchronous sample reading.
        manager["my-rtl-sdr"].StartReadSamplesAsync(8 * 16384);

        // Buffers taken from the channel, waiting for this thread to read and return them.
        var pending = new ConcurrentQueue<RawSampleBuffer>();

        // The event is raised on the driver's callback thread, so the handler only takes the
        // buffer and hands it on. Anything slower here - console output above all - stalls the
        // transfer pipeline and costs samples.
        //
        // Ownership travels with the buffer: whoever dequeues it is responsible for calling
        // Return() exactly once, which is why the loop below does it in a finally.
        manager["my-rtl-sdr"].SamplesAvailable += (_, _) =>
        {
            RawSampleBuffer buffer = manager["my-rtl-sdr"].GetRawSamplesFromAsyncBuffer();
            if (buffer != null)
            {
                pending.Enqueue(buffer);
            }
        };

        // Receive for five seconds, printing whatever the handler has handed over.
        var until = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < until)
        {
            if (!pending.TryDequeue(out RawSampleBuffer buffer))
            {
                Thread.Sleep(50);
                continue;
            }

            try
            {
                // Access raw interleaved I/Q bytes: [I0, Q0, I1, Q1, ...]
                ReadOnlySpan<byte> raw = buffer.Data.AsSpan(0, buffer.ByteLength);

                Console.WriteLine($"{buffer.SampleCount} samples received ({buffer.ByteLength} bytes).");

                // Dump the first five I/Q sample pairs.
                int count = Math.Min(5, buffer.SampleCount);
                Console.WriteLine($"Samples (first {count}):");
                for (int i = 0; i < count; i++)
                {
                    byte iSample = raw[i * 2];
                    byte qSample = raw[i * 2 + 1];
                    Console.WriteLine($"  {i + 1}: I={iSample,4}, Q={qSample,4}");
                }
            }
            finally
            {
                // Return the pooled buffer - must be called exactly once.
                buffer.Return();
            }
        }

        // Anything the handler queued after the loop ended still owns a pooled buffer, so
        // hand every one of them back rather than leaking it.
        while (pending.TryDequeue(out RawSampleBuffer leftover))
        {
            leftover.Return();
        }

        // Stop the reading of the samples. Since v0.7.0 this rethrows an error that stopped
        // the reading (e.g. a device failure); always close the device regardless.
        try
        {
            manager["my-rtl-sdr"].StopReadSamplesAsync();
        }
        catch (RtlSdrManagedDeviceException e)
        {
            Console.WriteLine($"Asynchronous reading stopped with an error: {e.InnerException?.Message}");
        }
        finally
        {
            // Close the device.
            manager.CloseAllManagedDevice();
        }
    }
}
