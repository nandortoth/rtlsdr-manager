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
using System.Collections.Generic;
using RtlSdrManager.Hardware;
using RtlSdrManager.Tools.HwVerify.Checks;

namespace RtlSdrManager.Tools.HwVerify;

/// <summary>
/// Hardware verification harness for RtlSdrManager.
/// </summary>
/// <remarks>
/// The unit test suite deliberately covers only hardware-independent components, so behavior
/// that depends on a real device has no automated coverage. This harness fills that gap:
/// attach a dongle, run it before cutting a release, and extend it whenever a fix depends on
/// hardware. It exits nonzero on any failure, so it also works as a release gate.
/// <para>
/// Adding checks means writing an <see cref="IHardwareCheck"/> in the <c>Checks</c> folder and
/// registering it in <see cref="BuildChecks"/>. Each one is responsible for restoring whatever
/// device state it changes.
/// </para>
/// <para>
/// The harness restores what it changes. Gain mode and sampling mode are captured on entry and
/// put back on exit, including on the failure path. The bias tee is only ever written with
/// <see cref="Modes.BiasTeeModes.Disabled"/>, and only on pin 0, because setting a pin also
/// switches it to output mode and nothing clears that when the device is closed. Passing
/// <see cref="BiasTeeChecks.BiasTeeOnFlag"/> additionally tests enabling the bias tee, which
/// must not be done with a passive antenna connected.
/// </para>
/// <para>
/// Two things are still worth knowing. Device state does not survive a replug, so unplugging
/// the dongle resets anything the harness touched. And the harness intentionally leaves the
/// bias tee off rather than restoring its previous value: off is the safe state, and reading
/// the pin back is not possible.
/// </para>
/// </remarks>
internal static class Program
{
    /// <summary>
    /// Exit code reported when every check that ran passed.
    /// </summary>
    private const int ExitSuccess = 0;

    /// <summary>
    /// Exit code reported when at least one check failed.
    /// </summary>
    private const int ExitFailure = 1;

    /// <summary>
    /// Exit code reported when no device is attached, so nothing could be verified.
    /// Distinct from <see cref="ExitFailure"/>: an empty run is inconclusive, not a failure.
    /// </summary>
    private const int ExitNoDevice = 2;

    /// <summary>
    /// Friendly name the device under test is opened under.
    /// </summary>
    private const string DeviceName = "dut";

    /// <summary>
    /// Entry point. Opens the first device and runs every check against it.
    /// </summary>
    /// <param name="args">Command line arguments; see <see cref="BiasTeeChecks.BiasTeeOnFlag"/>.</param>
    /// <returns>
    /// <see cref="ExitSuccess"/>, <see cref="ExitFailure"/>, or <see cref="ExitNoDevice"/>.
    /// </returns>
    public static int Main(string[] args)
    {
        bool biasTeeOn = args.Contains(BiasTeeChecks.BiasTeeOnFlag);

        // The device chatter would interleave with the check results and make them hard to
        // read, and it is not what this tool is verifying.
        RtlSdrDeviceManager.SuppressLibraryConsoleOutput = true;
        RtlSdrDeviceManager manager = RtlSdrDeviceManager.Instance;

        Console.WriteLine("RtlSdrManager hardware verification");
        Console.WriteLine($"Devices found: {manager.CountDevices}");
        foreach (KeyValuePair<uint, DeviceInfo> entry in manager.Devices)
        {
            Console.WriteLine($"  [{entry.Key}] {entry.Value.Manufacturer} {entry.Value.ProductType} " +
                              $"serial={entry.Value.Serial}");
        }

        Console.WriteLine();

        if (manager.CountDevices == 0)
        {
            Console.WriteLine("No devices attached. Nothing to verify.");
            return ExitNoDevice;
        }

        var report = new VerificationReport();

        manager.OpenManagedDevice(0, DeviceName);
        try
        {
            RtlSdrManagedDevice device = manager[DeviceName];

            // The tuner type is cached on the device, so the checks read it themselves; this
            // is only for the banner.
            TunerTypes tuner = device.TunerType;

            Console.WriteLine($"Device under test: index 0, tuner = {tuner}");
            Console.WriteLine();

            foreach (IHardwareCheck check in BuildChecks(biasTeeOn, () => ReopenDevice(manager)))
            {
                Console.WriteLine(check.Title);

                // Resolved per check rather than captured once: a check is allowed to close
                // and reopen the device, which invalidates any reference taken before it ran.
                check.Run(manager[DeviceName], report);
                Console.WriteLine();
            }
        }
        finally
        {
            manager.CloseManagedDevice(DeviceName);
        }

        report.PrintSummary();

        return report.HasFailures ? ExitFailure : ExitSuccess;
    }

    /// <summary>
    /// The checks to run, in order.
    /// </summary>
    /// <param name="biasTeeOn">Whether the bias tee power-on check was requested.</param>
    /// <param name="reopenDevice">Closes the device under test and opens it again.</param>
    /// <returns>The checks, in the order they should run.</returns>
    /// <remarks>
    /// Order matters in one place: the direct sampling checks rely on the center frequency
    /// checks having left the device tuned above the ADC's reach, which is the case worth
    /// exercising when direct sampling is switched on.
    /// </remarks>
    private static IEnumerable<IHardwareCheck> BuildChecks(bool biasTeeOn,
        Func<RtlSdrManagedDevice> reopenDevice) =>
    [
        new TunerGainChecks(),
        new CenterFrequencyChecks(),
        new DirectSamplingChecks(),
        new ConsoleSuppressionChecks(),
        new SampleReadingChecks(reopenDevice),
        new BiasTeeChecks(biasTeeOn)
    ];

    /// <summary>
    /// Close the device under test and open it again, returning the new instance.
    /// </summary>
    /// <param name="manager">Device manager owning the device.</param>
    /// <returns>The reopened device.</returns>
    /// <remarks>
    /// Closing is the only point at which the driver waits for asynchronous work to finish
    /// and shuts the USB event handling down. Starting a fresh reading on a device that was
    /// merely stopped leaves a canceled transfer able to complete against memory the driver
    /// has already released, which crashes the process on macOS. A check that needs several
    /// readings therefore takes a new device for each one instead of restarting.
    /// </remarks>
    private static RtlSdrManagedDevice ReopenDevice(RtlSdrDeviceManager manager)
    {
        manager.CloseManagedDevice(DeviceName);
        manager.OpenManagedDevice(0, DeviceName);

        return manager[DeviceName];
    }
}
