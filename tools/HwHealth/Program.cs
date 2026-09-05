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

namespace RtlSdrManager.Tools.HwHealth;

/// <summary>
/// Answers one question: is the attached device sustaining sample delivery right now?
/// </summary>
/// <remarks>
/// This exists because a device can fail in a way that a short check cannot see. After a few
/// hundred open and close cycles these dongles enter a state where they still enumerate, still
/// open, and still deliver a first buffer, yet cannot sustain a stream; only replugging clears
/// it. A check that opens a device and reads once reports that state as healthy, which is how
/// a degraded dongle gets mistaken for a software fault.
/// <para>
/// So this probe streams for several seconds and measures throughput against the configured
/// sample rate. That is the measurement which separates a working device from a degraded one:
/// a healthy dongle delivers close to the rate it was asked for, a degraded one delivers a
/// small fraction of it.
/// </para>
/// <para>
/// It is deliberately a separate tool from the verification harness rather than a check inside
/// it. The harness is what the probe is usually validating, so sharing its device lifecycle
/// code would let both carry the same blind spot. Bracket harness runs and any hardware
/// measurement with this, and treat a run whose health is unknown as a run with no result.
/// </para>
/// </remarks>
internal static class Program
{
    /// <summary>
    /// Exit code reported when the device sustained delivery.
    /// </summary>
    private const int ExitHealthy = 0;

    /// <summary>
    /// Exit code reported when health could not be established: the device is attached but did
    /// not sustain delivery, could not be opened, or the command line made no sense.
    /// </summary>
    private const int ExitUnhealthy = 1;

    /// <summary>
    /// Exit code reported when no device is attached, so nothing could be measured.
    /// Distinct from <see cref="ExitUnhealthy"/>: an empty run is inconclusive.
    /// </summary>
    private const int ExitNoDevice = 2;

    /// <summary>
    /// Friendly name the probed device is opened under.
    /// </summary>
    private const string DeviceName = "probe";

    /// <summary>
    /// Entry point. Opens one device, probes it, and reports the verdict.
    /// </summary>
    /// <param name="args">Command line arguments; see <see cref="ProbeOptions.UsageText"/>.</param>
    /// <returns>
    /// <see cref="ExitHealthy"/>, <see cref="ExitUnhealthy"/>, or <see cref="ExitNoDevice"/>.
    /// </returns>
    public static int Main(string[] args)
    {
        if (!ProbeOptions.TryParse(args, out ProbeOptions options, out string? error))
        {
            Console.WriteLine(error);
            Console.WriteLine();
            Console.WriteLine(ProbeOptions.UsageText);

            return ExitUnhealthy;
        }

        if (options.HelpRequested)
        {
            Console.WriteLine(ProbeOptions.UsageText);

            return ExitHealthy;
        }

        // The driver's own chatter would bury the single line this tool exists to print.
        RtlSdrDeviceManager.SuppressLibraryConsoleOutput = true;
        RtlSdrDeviceManager manager = RtlSdrDeviceManager.Instance;

        if (manager.CountDevices == 0)
        {
            return Report(HealthVerdict.NoDevice, options.Quiet);
        }

        try
        {
            Open(manager, options);
        }
        catch (Exception ex)
        {
            return Report(
                new HealthVerdict(Verdict.Unhealthy,
                    $"the device could not be opened: {ex.Message}"),
                options.Quiet);
        }

        try
        {
            ProbeResult result = new DeliveryProbe(manager[DeviceName]).Run(options.Window);

            return Report(HealthVerdict.For(result), options.Quiet);
        }
        catch (Exception ex)
        {
            return Report(
                new HealthVerdict(Verdict.Unhealthy,
                    $"the probe could not complete: {ex.GetType().Name}: {ex.Message}"),
                options.Quiet);
        }
        finally
        {
            // Tolerant of a device that is already gone, so teardown cannot replace the
            // verdict with an exception.
            manager.CloseAllManagedDevice();
        }
    }

    /// <summary>
    /// Open the device the options name.
    /// </summary>
    /// <param name="manager">The device manager.</param>
    /// <param name="options">What the run was asked to probe.</param>
    /// <remarks>
    /// Opening by serial survives a replug, which opening by index does not; the index remains
    /// the default because most runs have one dongle attached and no reason to name it.
    /// </remarks>
    private static void Open(RtlSdrDeviceManager manager, ProbeOptions options)
    {
        if (options.Serial is null)
        {
            manager.OpenManagedDevice(options.Index, DeviceName);
        }
        else
        {
            manager.OpenManagedDeviceBySerial(options.Serial, DeviceName);
        }
    }

    /// <summary>
    /// Print a verdict and turn it into an exit code.
    /// </summary>
    /// <param name="verdict">What was concluded.</param>
    /// <param name="quiet">Whether to print the verdict alone.</param>
    /// <returns>The exit code for the verdict.</returns>
    private static int Report(HealthVerdict verdict, bool quiet)
    {
        Console.WriteLine(verdict.Format(quiet));

        return verdict.Verdict switch
        {
            Verdict.Healthy => ExitHealthy,
            Verdict.NoDevice => ExitNoDevice,
            _ => ExitUnhealthy
        };
    }
}
