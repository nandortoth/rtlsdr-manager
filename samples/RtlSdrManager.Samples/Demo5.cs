// RTL-SDR Manager Library for .NET Core
// Copyright (C) 2018-2025 Nandor Toth <dev@nandortoth.eu>
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

        // Subscribe on the event with the function.
        manager["my-rtl-sdr"].SamplesAvailable += (_, _) =>
        {
            // Get a raw buffer from the channel.
            RawSampleBuffer buffer = manager["my-rtl-sdr"].GetRawSamplesFromAsyncBuffer();
            if (buffer == null)
            {
                return;
            }

            try
            {
                // Access raw interleaved I/Q bytes: [I0, Q0, I1, Q1, ...]
                ReadOnlySpan<byte> raw = buffer.Data.AsSpan(0, buffer.ByteLength);

                Console.WriteLine($"{buffer.SampleCount} samples received ({buffer.ByteLength} bytes).");

                // Dump the first five I/Q sample pairs.
                Console.WriteLine("Samples (first five):");
                int count = Math.Min(5, buffer.SampleCount);
                for (int i = 0; i < count; i++)
                {
                    byte iSample = raw[i * 2];
                    byte qSample = raw[i * 2 + 1];
                    Console.WriteLine($"  {i + 1}: I={iSample,4}, Q={qSample,4}");
                }
            }
            finally
            {
                // Return the pooled buffer — must be called exactly once.
                buffer.Return();
            }
        };

        // Sleep the thread for 5 seconds before stopping the sample reading.
        Thread.Sleep(5000);

        // Stop the reading of the samples.
        manager["my-rtl-sdr"].StopReadSamplesAsync();

        // Close the device.
        manager.CloseAllManagedDevice();
    }
}
