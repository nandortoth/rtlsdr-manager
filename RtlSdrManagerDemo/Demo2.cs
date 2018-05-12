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
using System.Threading.Tasks;
using RtlSdrManager.Types;

namespace RtlSdrManager.Demo
{
    /// <summary>
    /// Demo for RtlSdrManager.
    /// 
    /// In this demo:
    ///   - Samples will be readed asynchronously.
    ///   - Samples will be readed directly from the buffer.
    /// </summary>
    public static class Demo2
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

            // Use cancellation token.
            var cts = new CancellationTokenSource();
            var token = cts.Token;
            
            // Start asynchronous sample reading.
            manager["my-rtl-sdr"].StartReadSamplesAsync();

            // Create a task, which will dequeue the items from the buffer.
            Task.Factory.StartNew(() =>
            {
                // Counter for demo purposes.
                // Only every seventy-five thousandth data will be showed.
                var counter = 0;
                
                // Read samples from the buffer, till cancellation request.
                while (!token.IsCancellationRequested)
                {
                    // If the buffer is empty, wait for 0.1 sec.
                    if (manager["my-rtl-sdr"].AsyncBuffer.IsEmpty)
                    {
                        Thread.Sleep(100);
                        continue;
                    }

                    // Dequeue from the buffer.
                    if (!manager["my-rtl-sdr"].AsyncBuffer.TryDequeue(out var data))
                    {
                        Thread.Sleep(100);
                        continue;
                    }

                    // Increase the counter.
                    counter++;
                    if (counter % 75000 != 0)
                    {
                        continue;
                    }
                    
                    // Show the data.
                    Console.WriteLine(data);
                    counter = 0;
                }
            }, token);

            // Create a separated task, which continously check the buffer's size.
            Task.Factory.StartNew(() =>
            {
                while (!token.IsCancellationRequested)
                {
                    var bufferSize = manager["my-rtl-sdr"].AsyncBuffer.Count;
                    Console.WriteLine($"Still unhandled samples: {bufferSize}");
                    Thread.Sleep(250);
                }
            }, token);

            // Sleep the thread for 5 second before stop the samples reading.
            Thread.Sleep(5000);

            // Cancel the tasks.
            cts.Cancel();

            // Stop the reading of the samples.
            manager["my-rtl-sdr"].StopReadSamplesAsync();

            // Close the device.
            manager.CloseAllManagedDevice();
        }
    }
}