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
    /// Marker written for a cycle whose reading received no samples before its deadline.
    /// </summary>
    /// <remarks>
    /// Such a cycle exercises nothing: with no transfer completing there is nothing for the
    /// stop to race against. It is counted apart from completed cycles, so a device that has
    /// stopped delivering cannot produce a run that looks clean.
    /// </remarks>
    public const string EmptyCycleMarker = " empty";

    /// <summary>
    /// Exit code used when the cycles ended because readings stopped receiving samples.
    /// </summary>
    /// <remarks>
    /// Says nothing about why. A worn-out device and a build that cannot restart a reading
    /// look identical from here; telling them apart takes a health check with a library known
    /// to stream, which is the caller's job.
    /// </remarks>
    public const int StarvedExitCode = 4;

    /// <summary>
    /// Consecutive empty cycles after which the run is abandoned.
    /// </summary>
    /// <remarks>
    /// Each empty cycle costs the full sample deadline, so continuing only spends time and
    /// wears the device further without measuring anything.
    /// </remarks>
    private const int StarvedCycleLimit = 3;

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
    /// <returns>
    /// Zero when every cycle completed, or <see cref="StarvedExitCode"/> when readings stopped
    /// receiving samples.
    /// </returns>
    public static int Run(CyclePattern pattern, int cycles, uint index)
    {
        RtlSdrDeviceManager.SuppressLibraryConsoleOutput = true;
        RtlSdrDeviceManager manager = RtlSdrDeviceManager.Instance;

        try
        {
            bool starved = pattern switch
            {
                CyclePattern.Restart => RunRestart(manager, cycles, index),
                CyclePattern.Reopen => RunReopen(manager, cycles, index),
                _ => RunOpenClose(manager, cycles, index)
            };

            return starved ? StarvedExitCode : 0;
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
    /// <returns>True when the run was abandoned because readings stopped receiving samples.</returns>
    private static bool RunRestart(RtlSdrDeviceManager manager, int cycles, uint index)
    {
        manager.OpenManagedDevice(index, DeviceName);

        try
        {
            RtlSdrManagedDevice device = Configure(manager[DeviceName]);
            int consecutiveEmpty = 0;

            for (int i = 0; i < cycles; i++)
            {
                if (MarkCycle(ReadOnce(device), ref consecutiveEmpty))
                {
                    return true;
                }
            }

            return false;
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
    /// <returns>True when the run was abandoned because readings stopped receiving samples.</returns>
    private static bool RunReopen(RtlSdrDeviceManager manager, int cycles, uint index)
    {
        int consecutiveEmpty = 0;

        for (int i = 0; i < cycles; i++)
        {
            manager.OpenManagedDevice(index, DeviceName);
            bool delivered;

            try
            {
                delivered = ReadOnce(Configure(manager[DeviceName]));
            }
            finally
            {
                manager.CloseAllManagedDevice();
            }

            if (MarkCycle(delivered, ref consecutiveEmpty))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Open and close, with no reading.
    /// </summary>
    /// <param name="manager">The device manager.</param>
    /// <param name="cycles">How many iterations.</param>
    /// <param name="index">Device index to use.</param>
    /// <returns>Always false: with no reading there are no samples to miss.</returns>
    private static bool RunOpenClose(RtlSdrDeviceManager manager, int cycles, uint index)
    {
        int consecutiveEmpty = 0;

        for (int i = 0; i < cycles; i++)
        {
            manager.OpenManagedDevice(index, DeviceName);
            manager.CloseAllManagedDevice();
            MarkCycle(delivered: true, ref consecutiveEmpty);
        }

        return false;
    }

    /// <summary>
    /// Take one reading and end it.
    /// </summary>
    /// <param name="device">The device to read from.</param>
    /// <returns>True when samples arrived before the deadline.</returns>
    /// <remarks>
    /// Waiting for samples matters: a start that is stopped before any transfer has completed
    /// exercises a different and gentler path than one that is stopped mid-stream.
    /// </remarks>
    private static bool ReadOnce(RtlSdrManagedDevice device)
    {
        device.StartReadSamplesAsync();
        bool delivered = Deadline.WaitUntil(() => device.AsyncBuffer.Count > 0, SampleDeadline);
        device.StopReadSamplesAsync();

        return delivered;
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
    /// <param name="delivered">Whether the cycle's reading received samples.</param>
    /// <param name="consecutiveEmpty">Empty cycles in a row so far; updated here.</param>
    /// <returns>True when <see cref="StarvedCycleLimit"/> empty cycles have run in a row.</returns>
    private static bool MarkCycle(bool delivered, ref int consecutiveEmpty)
    {
        Console.Error.WriteLine(delivered ? CycleMarker : EmptyCycleMarker);
        Console.Error.Flush();

        consecutiveEmpty = delivered ? 0 : consecutiveEmpty + 1;

        return consecutiveEmpty >= StarvedCycleLimit;
    }
}
