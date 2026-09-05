// RTL-SDR Manager Library for .NET
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
using RtlSdrManager.Tools.Common;

namespace RtlSdrManager.Tools.HwStress;

/// <summary>
/// Runs the cycles, in a process that is expected to die.
/// </summary>
/// <remarks>
/// This half of the tool is deliberately sacrificial. The defect it exists to provoke kills
/// the process outright, with no managed opportunity to catch anything, so the counting and
/// judging live in a parent that survives; see <see cref="RunSupervisor"/>.
/// <para>
/// Every completed cycle is announced on standard error and flushed immediately. That is the
/// only reason the parent can say how far a run got before it died, and it is why the marker
/// is written per cycle rather than summarized at the end.
/// </para>
/// </remarks>
internal static class CycleWorker
{
    /// <summary>
    /// Marker written for each completed cycle. The parent counts these.
    /// </summary>
    public const string CycleMarker = " ok";

    /// <summary>
    /// Prefix for a managed fault, so the parent can tell one from a native crash.
    /// </summary>
    /// <remarks>
    /// A process killed by the native layer leaves nothing behind; a managed exception can say
    /// what went wrong. Reporting both as the same bare exit code throws that away, and did:
    /// the first fault this tool hit was a missing device reset, which looked exactly like the
    /// use-after-free it was built to detect.
    /// </remarks>
    public const string FaultMarker = "FAULT ";

    /// <summary>
    /// Friendly name the device is opened under.
    /// </summary>
    private const string DeviceName = "stress";

    /// <summary>
    /// Sample rate used while cycling, matching the other hardware tools.
    /// </summary>
    private const uint SampleRateHz = 2_048_000;

    /// <summary>
    /// How long a single cycle may wait for samples before giving up on them.
    /// </summary>
    private static readonly TimeSpan SampleDeadline = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Run the requested cycles.
    /// </summary>
    /// <param name="pattern">Which lifecycle to cycle.</param>
    /// <param name="cycles">How many times.</param>
    /// <param name="index">Device index to use.</param>
    /// <returns>Zero when every cycle completed.</returns>
    public static int Run(CyclePattern pattern, int cycles, uint index)
    {
        RtlSdrDeviceManager.SuppressLibraryConsoleOutput = true;
        RtlSdrDeviceManager manager = RtlSdrDeviceManager.Instance;

        try
        {
            switch (pattern)
            {
                case CyclePattern.Restart:
                    RunRestart(manager, cycles, index);
                    break;

                case CyclePattern.Reopen:
                    RunReopen(manager, cycles, index);
                    break;

                default:
                    RunOpenClose(manager, cycles, index);
                    break;
            }

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"{FaultMarker}{ex.GetType().Name}: {ex.Message}");
            Console.Error.Flush();

            return ManagedFaultExitCode;
        }
    }

    /// <summary>
    /// Exit code used when the cycles ended in a managed exception rather than a crash.
    /// </summary>
    public const int ManagedFaultExitCode = 3;

    /// <summary>
    /// Open once, then start and stop a reading repeatedly.
    /// </summary>
    /// <param name="manager">The device manager.</param>
    /// <param name="cycles">How many readings to take.</param>
    /// <param name="index">Device index to use.</param>
    private static void RunRestart(RtlSdrDeviceManager manager, int cycles, uint index)
    {
        manager.OpenManagedDevice(index, DeviceName);

        try
        {
            RtlSdrManagedDevice device = Configure(manager[DeviceName]);

            for (int i = 0; i < cycles; i++)
            {
                ReadOnce(device);
                MarkCycle();
            }
        }
        finally
        {
            manager.CloseAllManagedDevice();
        }
    }

    /// <summary>
    /// Open, read, stop and close on every iteration.
    /// </summary>
    /// <param name="manager">The device manager.</param>
    /// <param name="cycles">How many iterations.</param>
    /// <param name="index">Device index to use.</param>
    private static void RunReopen(RtlSdrDeviceManager manager, int cycles, uint index)
    {
        for (int i = 0; i < cycles; i++)
        {
            manager.OpenManagedDevice(index, DeviceName);

            try
            {
                ReadOnce(Configure(manager[DeviceName]));
            }
            finally
            {
                manager.CloseAllManagedDevice();
            }

            MarkCycle();
        }
    }

    /// <summary>
    /// Open and close, with no reading.
    /// </summary>
    /// <param name="manager">The device manager.</param>
    /// <param name="cycles">How many iterations.</param>
    /// <param name="index">Device index to use.</param>
    private static void RunOpenClose(RtlSdrDeviceManager manager, int cycles, uint index)
    {
        for (int i = 0; i < cycles; i++)
        {
            manager.OpenManagedDevice(index, DeviceName);
            manager.CloseAllManagedDevice();
            MarkCycle();
        }
    }

    /// <summary>
    /// Take one reading and end it.
    /// </summary>
    /// <param name="device">The device to read from.</param>
    /// <remarks>
    /// Waiting for samples matters: a start that is stopped before any transfer has completed
    /// exercises a different and gentler path than one that is stopped mid-stream.
    /// </remarks>
    private static void ReadOnce(RtlSdrManagedDevice device)
    {
        device.StartReadSamplesAsync();
        Deadline.WaitUntil(() => device.AsyncBuffer.Count > 0, SampleDeadline);
        device.StopReadSamplesAsync();
    }

    /// <summary>
    /// Put a device into a state where samples flow.
    /// </summary>
    /// <param name="device">The device to configure.</param>
    /// <returns>The same device, configured.</returns>
    private static RtlSdrManagedDevice Configure(RtlSdrManagedDevice device)
    {
        device.SampleRate = Frequency.FromHz(SampleRateHz);

        FrequencyRange range = device.SupportedFrequencyRanges[0];
        device.CenterFrequency =
            Frequency.FromHz(range.Minimum.Hz + ((range.Maximum.Hz - range.Minimum.Hz) / 2));

        // The buffer is never drained here, so without this the device stalls rather than
        // cycling, and the pattern stops being the one that was asked for.
        device.DropSamplesOnFullBuffer = true;
        device.ResetDeviceBuffer();

        return device;
    }

    /// <summary>
    /// Announce a completed cycle to the parent.
    /// </summary>
    private static void MarkCycle()
    {
        Console.Error.WriteLine(CycleMarker);
        Console.Error.Flush();
    }
}
