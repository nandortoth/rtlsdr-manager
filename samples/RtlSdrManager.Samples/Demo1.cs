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
using System.Collections.Generic;
using System.Threading;
using RtlSdrManager.Exceptions;
using RtlSdrManager.Modes;

namespace RtlSdrManager.Samples;

/// <summary>
/// Demo for RtlSdrManager.
///
/// In this demo:
///   - Samples will be received asynchronously.
///   - Samples will be handled by SamplesAvailable event.
///   - The handler hands work off instead of doing it, because it runs on the driver's
///     callback thread; see the comment on the subscription below.
/// </summary>
public static class Demo1
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
        manager["my-rtl-sdr"].ResetDeviceBuffer();

        // Start asynchronous sample reading.
        manager["my-rtl-sdr"].StartReadSamplesAsync(8 * 16384);

        // Batches taken out of the device buffer, waiting to be printed by this thread.
        var pending = new ConcurrentQueue<List<IQData>>();

        // The event is raised on the driver's callback thread, so the handler only drains the
        // buffer and hands the batch on. Anything slower here - console output above all -
        // stalls the transfer pipeline and costs samples.
        manager["my-rtl-sdr"].SamplesAvailable += (_, args) =>
        {
            pending.Enqueue(manager["my-rtl-sdr"].GetSamplesFromAsyncBuffer(args.SampleCount));
        };

        // Receive for five seconds, printing whatever the handler has handed over.
        var until = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < until)
        {
            if (!pending.TryDequeue(out List<IQData> samples))
            {
                Thread.Sleep(50);
                continue;
            }

            Console.WriteLine($"{samples.Count} samples handled, and removed from the buffer.");

            // A batch can be shorter than five samples, so take whichever is smaller.
            int show = Math.Min(5, samples.Count);
            Console.WriteLine($"Samples (first {show}):");
            for (int i = 0; i < show; i++)
            {
                Console.WriteLine($"  {i + 1}: {samples[i]}");
            }
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

            // The same error stays readable here until the next StartReadSamplesAsync, which
            // is useful when the reading stopped on its own rather than on request.
            Console.WriteLine($"AsyncReadException: {manager["my-rtl-sdr"].AsyncReadException?.Message}");
        }
        finally
        {
            // Close the device.
            manager.CloseAllManagedDevice();
        }
    }
}
