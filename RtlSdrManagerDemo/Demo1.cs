// RTL-SDR Manager Library for .NET Core
// Copyright (C) 2018 Nandor Toth <dev@nandortoth.eu>
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
using RtlSdrManager.Types;

namespace RtlSdrManager.Demo
{
    /// <summary>
    /// Demo for RtlSdrManager.
    /// 
    /// In this demo:
    ///   - Samples will be readed asynchronously.
    ///   - Samples will be handled by SamplesAvailable event.
    /// </summary>
    public static class Demo1
    {
        /// <summary>
        /// Run the demo.
        /// </summary>
        public static void Run()
        {
            // Initialize the Manager instance.
            var manager = new RtlSdrDeviceManager();

            // Open a managed device and set some parameters.
            manager.OpenManagedDevice(0, "my-rtl-sdr");
            manager["my-rtl-sdr"].CenterFrequency = new Frequency {MHz = 1090};
            manager["my-rtl-sdr"].SampleRate = new Frequency {MHz = 2};
            manager["my-rtl-sdr"].TunerGainMode = TunerGainModes.AGC;
            manager["my-rtl-sdr"].AGCMode = AGCModes.Enabled;
            manager["my-rtl-sdr"].MaxAsyncBufferSize = 512 * 1024;
            manager["my-rtl-sdr"].DropSamplesOnFullBuffer = true;
            manager["my-rtl-sdr"].ResetDeviceBuffer();

            // Start asynchronous sample reading.
            manager["my-rtl-sdr"].StartReadSamplesAsync(8 * 16384);

            // Subscribe on the event with the function.
            manager["my-rtl-sdr"].SamplesAvailable += (sender, args) =>
            {
                // Read the samples.
                var samples = manager["my-rtl-sdr"].GetSamplesFromAsyncBuffer(args.SampleCount);
                Console.WriteLine($"{samples.Count} samples handled, and removed from the buffer.");

                // Dump the first five samples.
                Console.WriteLine("Samples (first five):");
                for (var i = 0; i < 5; i++)
                {
                    Console.WriteLine($"  {i + 1}: {samples[i]}");
                }
            };

            // Sleep the thread for 5 second before stop the samples reading.
            Thread.Sleep(5000);

            // Stop the reading of the samples.
            manager["my-rtl-sdr"].StopReadSamplesAsync();

            // Close the device.
            manager.CloseAllManagedDevice();
        }
    }
}