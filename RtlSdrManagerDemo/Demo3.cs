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
using RtlSdrManager.Types;

namespace RtlSdrManager.Demo
{
    /// <summary>
    /// Demo for RtlSdrManager.
    /// 
    /// In this demo:
    ///   - Samples will be readed synchronously.
    ///   - Simply print the first 5 samples.
    /// </summary>
    public static class Demo3
    {
        /// <summary>
        /// Run the demo.
        /// </summary>
        public static void Run()
        {
            // Initialize the Manager instance.
            var manager = RtlSdrDeviceManager.Instance;

            // Open a managed device and set some parameters.
            manager.OpenManagedDevice(0, "my-rtl-sdr");
            manager["my-rtl-sdr"].CenterFrequency = new Frequency {MHz = 1090};
            manager["my-rtl-sdr"].SampleRate = new Frequency {MHz = 2};
            manager["my-rtl-sdr"].TunerGainMode = TunerGainModes.AGC;
            manager["my-rtl-sdr"].AGCMode = AGCModes.Enabled;
            manager["my-rtl-sdr"].ResetDeviceBuffer();

            // Read samples.
            var samples = manager["my-rtl-sdr"].ReadSamples(256 * 1024);

            // Dump the first five samples.
            Console.WriteLine("Samples (first five):");
            for (var i = 0; i < 5; i++)
            {
                Console.WriteLine($"  {i + 1}: {samples[i]}");
            }

            // Close the device.
            manager.CloseAllManagedDevice();
        }
    }
}