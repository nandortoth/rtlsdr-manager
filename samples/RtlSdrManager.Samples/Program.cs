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

namespace RtlSdrManager.Samples;

/// <summary>
/// Simple demo for RtlSdrManager.
/// </summary>
public static class Program
{
    /// <summary>
    /// Main function of the demo.
    /// </summary>
    public static void Main()
    {
        // Enable console output suppression for hiding messages from librtlsdr.
        RtlSdrDeviceManager.SuppressLibraryConsoleOutput = true;

        // Check the available devices.
        if (RtlSdrDeviceManager.Instance.CountDevices == 0)
        {
            Console.Clear();
            Console.WriteLine("There is no RTL-SDR device on the system.");
            Console.WriteLine("It is not possible to run the demos.");
            return;
        }

        // ConsoleKey buffer.
        ConsoleKey selectedDemo;

        do
        {
            // Clear the console.
            Console.Clear();

            // Display available demos.
            Console.WriteLine("-------------------------------------------------------");
            Console.WriteLine("Which demo do you want to run?");
            Console.WriteLine("-------------------------------------------------------");
            Console.WriteLine(" [1] DEMO 1");
            Console.WriteLine("     Samples will be read asynchronously.");
            Console.WriteLine("     Samples will be handled by SamplesAvailable event.");
            Console.WriteLine(" [2] DEMO 2");
            Console.WriteLine("     Samples will be read asynchronously.");
            Console.WriteLine("     Samples will be read directly from the buffer.");
            Console.WriteLine(" [3] DEMO 3");
            Console.WriteLine("     Samples will be read synchronously.");
            Console.WriteLine("     Simply print the first 5 samples.");
            Console.WriteLine(" [4] DEMO 4");
            Console.WriteLine("     Show RTL-SDR device(s) on the system.");
            Console.WriteLine("     Show the detailed parameters of the opened device(s).");
            Console.WriteLine("-------------------------------------------------------");

            // Display the possibilities.
            Console.Write("Please select [1, 2, 3, 4 or ESC to quit]: ");

            // Read from the console.
            selectedDemo = Console.ReadKey().Key;
            Console.WriteLine("\n");
        } while (selectedDemo != ConsoleKey.D1 &&
                 selectedDemo != ConsoleKey.D2 &&
                 selectedDemo != ConsoleKey.D3 &&
                 selectedDemo != ConsoleKey.D4 &&
                 selectedDemo != ConsoleKey.Escape);

        // Run the appropriate demo.
        switch (selectedDemo)
        {
            case ConsoleKey.D1:
                Demo1.Run();
                break;
            case ConsoleKey.D2:
                Demo2.Run();
                break;
            case ConsoleKey.D3:
                Demo3.Run();
                break;
            case ConsoleKey.D4:
                Demo4.Run();
                break;
            case ConsoleKey.Escape:
                break;
        }
    }
}
