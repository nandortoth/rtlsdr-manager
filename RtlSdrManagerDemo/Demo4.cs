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
using System.Linq;
using RtlSdrManager.Types;

namespace RtlSdrManager.Demo
{
    /// <summary>
    /// Demo for RtlSdrManager.
    /// 
    /// In this demo:
    ///   - Show RTL-SDR device(s) on the system.
    ///   - Show the detailed parameter of the opened device(s).
    /// </summary>
    public static class Demo4
    {
        /// <summary>
        /// Main function of the demo.
        /// </summary>
        public static void Run()
        {
            // Initialize the Manager instance.
            var manager = new RtlSdrDeviceManager();

            // Go through on all the devices on the system.
            Console.WriteLine("AVAILABLE DEVICES");
            foreach (var device in RtlSdrDeviceManager.Devices)
            {
                Console.WriteLine($"  Device (index: {device.Index}):\n" +
                                  $"    {"Manufacturer",-12}: {device.Manufacturer}\n" +
                                  $"    {"Product",-12}: {device.ProductType}\n" +
                                  $"    {"Serial",-12}: {device.Serial}\n" +
                                  $"    {"Name",-12}: {device.Name}\n");
            }

            // Quick check about the devices, before opening any device.
            Console.WriteLine("DETAILS - BEFORE MANAGING ANY OF THEM");
            Console.WriteLine($"  Number of device(s) on the system: {RtlSdrDeviceManager.CountDevices}\n" +
                              $"  Managed device(s) on the system:   {manager.CountManagedDevices}\n");

            // Open a managed device and set some parameters.
            manager.OpenManagedDevice(0, "my-rtl-sdr");
            manager["my-rtl-sdr"].CenterFrequency = new Frequency {MHz = 1090};
            manager["my-rtl-sdr"].SampleRate = new Frequency {MHz = 2};
            manager["my-rtl-sdr"].TunerGainMode = TunerGainModes.AGC;
            manager["my-rtl-sdr"].FrequencyCorrection = 10;
            manager["my-rtl-sdr"].AGCMode = AGCModes.Enabled;
            manager["my-rtl-sdr"].TestMode = TestModes.Disabled;
            manager["my-rtl-sdr"].ResetDeviceBuffer();

            // Quick check about the devices, after opening one.
            Console.WriteLine("DETAILS - AFTER OPENING ONE");
            Console.WriteLine($"  Number of device(s) on the system: {RtlSdrDeviceManager.CountDevices}\n" +
                              $"  Managed device(s) on the system:   {manager.CountManagedDevices}\n");


            // Go through on the managed devices (using manager's IEnumerable).
            Console.WriteLine("OPENED DEVICES");
            foreach (var device in manager)
            {
                Console.WriteLine($"  Device (index: {device.DeviceInfo.Index}):\n" +
                                  $"    {"Manufacturer",-22}: {device.DeviceInfo.Manufacturer}\n" +
                                  $"    {"Product",-22}: {device.DeviceInfo.ProductType}\n" +
                                  $"    {"Serial",-22}: {device.DeviceInfo.Serial}\n" +
                                  $"    {"Name",-22}: {device.DeviceInfo.Name}\n" +
                                  $"    {"Tuner type",-22}: {device.TunerType}\n" +
                                  $"    {"Center frequency",-22}: {device.CenterFrequency.MHz} MHz\n" +
                                  $"    {"Crystal frequency",-22}: {device.CrystalFrequency}\n" +
                                  $"    {"Frequency correction",-22}: {device.FrequencyCorrection} ppm\n" +
                                  $"    {"Bandwith selection:",-22}: {device.TunerBandwidthSelectionMode}\n" +
                                  $"    {"Sample rate",-22}: {device.SampleRate.MHz} MHz\n" +
                                  $"    {"Direct sampling mode",-22}: {device.DirectSamplingMode}\n" +
                                  $"    {"AGC mode",-22}: {device.AGCMode}\n" +
                                  $"    {"Tuner gain mode",-22}: {device.TunerGainMode}\n" +
                                  $"    {"Test mode",-22}: {device.TestMode}");
                Console.Write($"    {"Supported tuner gains",-22}: ");

                // Display supported gains in a fancy format.
                for (var i = 0; i < device.SupportedTunerGains.Count(); i++)
                {
                    if (i % 5 == 0 && i != 0)
                    {
                        Console.Write($"\n    {" ",-22}: ");
                    }

                    Console.Write($"{device.SupportedTunerGains.ElementAt(i),4:F1} dB  ");
                }

                Console.WriteLine("\n");
            }

            // Close the device.
            manager.CloseAllManagedDevice();
        }
    }
}