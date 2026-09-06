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
using System.Linq;
using RtlSdrManager.Exceptions;
using RtlSdrManager.Modes;

namespace RtlSdrManager.Samples;

/// <summary>
/// Demo for RtlSdrManager.
///
/// In this demo:
///   - Show RTL-SDR device(s) on the system.
///   - Show the detailed parameter of the opened device(s).
///   - Re-enumerate with RefreshDevices, open by serial, and report the reachable range.
/// </summary>
public static class Demo4
{
    /// <summary>
    /// Main function of the demo.
    /// </summary>
    public static void Run()
    {
        // Initialize the Manager instance.
        RtlSdrDeviceManager manager = RtlSdrDeviceManager.Instance;

        // Go through on all the devices on the system.
        Console.WriteLine("AVAILABLE DEVICES");
        foreach (DeviceInfo device in manager.Devices.Values)
        {
            Console.WriteLine($"  Device (index: {device.Index}):\n" +
                              $"    {"Manufacturer",-12}: {device.Manufacturer}\n" +
                              $"    {"Product",-12}: {device.ProductType}\n" +
                              $"    {"Serial",-12}: {device.Serial}\n" +
                              $"    {"Name",-12}: {device.Name}\n");
        }

        // Quick check about the devices, before opening any device.
        Console.WriteLine("DETAILS - BEFORE MANAGING ANY OF THEM");
        Console.WriteLine($"  Number of device(s) on the system: {manager.CountDevices}\n" +
                          $"  Managed device(s) on the system:   {manager.CountManagedDevices}\n");

        // Indices are positional and shift when devices are plugged or unplugged, so opening
        // by serial is the way to reach the same physical device again after a replug. It
        // throws rather than guessing if the serial is unknown, or if more than one device
        // carries it, which is common with unmodified factory values.
        if (manager.CountDevices > 0)
        {
            Console.WriteLine("OPENING BY SERIAL");

            foreach (DeviceInfo info in manager.Devices.Values)
            {
                Console.WriteLine($"  Serial '{info.Serial}' (enumerated at index {info.Index}):");

                try
                {
                    manager.OpenManagedDeviceBySerial(info.Serial, "by-serial");

                    // Reporting the index the lookup resolved to is the point: it should be
                    // the one enumeration reported, and it is what would shift after a replug.
                    Console.WriteLine($"    opened at index {manager["by-serial"].DeviceInfo.Index}, " +
                                      $"tuner: {manager["by-serial"].TunerType}");

                    manager.CloseManagedDevice("by-serial");
                }
                catch (RtlSdrDeviceException e)
                {
                    // Serials are not unique. Cheap dongles ship with the same factory value
                    // until someone reflashes them, and the library throws rather than
                    // picking one arbitrarily.
                    Console.WriteLine($"    could not be opened by serial: {e.Message}");
                }
            }

            Console.WriteLine();
        }

        // Open all available devices and set some parameters.
        foreach (DeviceInfo device in manager.Devices.Values)
        {
            string friendlyName = $"rtl-sdr-{device.Index}";
            try
            {
                manager.OpenManagedDevice(device.Index, friendlyName);
            }
            catch (RtlSdrDeviceException e)
            {
                Console.WriteLine(e);
                return;
            }
            catch
            {
                Console.WriteLine($"Failed to open the RTL-SDR device (index: {device.Index}).");
                return;
            }

            manager[friendlyName].CenterFrequency = Frequency.FromMHz(1090);
            manager[friendlyName].SampleRate = Frequency.FromMHz(2);
            manager[friendlyName].TunerGainMode = TunerGainModes.AGC;
            manager[friendlyName].FrequencyCorrection = 10;
            manager[friendlyName].AGCMode = AGCModes.Enabled;
            manager[friendlyName].TestMode = TestModes.Disabled;
            manager[friendlyName].ResetDeviceBuffer();
        }

        // Quick check about the devices, after opening all.
        Console.WriteLine("DETAILS - AFTER OPENING ALL");
        Console.WriteLine($"  Number of device(s) on the system: {manager.CountDevices}\n" +
                          $"  Managed device(s) on the system:   {manager.CountManagedDevices}\n");


        // Go through on the managed devices (using manager's IEnumerable).
        Console.WriteLine("OPENED DEVICES");
        foreach (RtlSdrManagedDevice device in manager)
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
                              $"    {"Bandwidth selection",-22}: {device.TunerBandwidthSelectionMode}\n" +
                              $"    {"Sample rate",-22}: {device.SampleRate.MHz} MHz\n" +
                              $"    {"Direct sampling mode",-22}: {device.DirectSamplingMode}\n" +
                              $"    {"AGC mode",-22}: {device.AGCMode}\n" +
                              $"    {"Tuner gain mode",-22}: {device.TunerGainMode}\n" +
                              $"    {"Offset tuning mode",-22}: {device.OffsetTuningMode}\n" +
                              $"    {"KerberosSDR mode",-22}: {device.KerberosSDRMode}\n" +
                              $"    {"Frequency dithering",-22}: {device.FrequencyDitheringMode}\n" +
                              $"    {"Test mode",-22}: {device.TestMode}");
            Console.Write($"    {"Supported tuner gains",-22}: ");

            // Display supported gains in a fancy format.
            for (int i = 0; i < device.SupportedTunerGains.Count; i++)
            {
                if (i % 5 == 0 && i != 0)
                {
                    Console.Write($"\n    {" ",-22}: ");
                }

                Console.Write($"{device.SupportedTunerGains.ElementAt(i),4:F1} dB  ");
            }

            // What CenterFrequency will currently accept. This is the tuner's range normally
            // and the ADC's while direct sampling is on, so it answers the question rather
            // than describing the hardware in the abstract.
            Console.Write($"\n    {"Reachable frequency",-22}: ");

            for (int i = 0; i < device.SupportedFrequencyRanges.Count; i++)
            {
                if (i != 0)
                {
                    Console.Write($"\n    {" ",-22}: ");
                }

                FrequencyRange range = device.SupportedFrequencyRanges[i];
                Console.Write($"{range.Minimum.MHz:0.###} - {range.Maximum.MHz:0.###} MHz");
            }

            Console.WriteLine("\n");
        }

        // Close the device.
        manager.CloseAllManagedDevice();
    }
}
