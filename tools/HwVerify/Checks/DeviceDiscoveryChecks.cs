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
using System.Linq;
using RtlSdrManager.Exceptions;

namespace RtlSdrManager.Tools.HwVerify.Checks;

/// <summary>
/// Verify opening a device by its serial number.
/// </summary>
/// <remarks>
/// The lookup itself is covered by unit tests, which can build a device list without any
/// hardware. What only a device proves is the part the tests cannot reach: that the serial
/// reported by enumeration actually opens, and opens the device it named.
/// <para>
/// The success path needs a second device. The harness holds device 0 open for the whole run,
/// and opening a device claims its USB interface, so asking for device 0 again by serial would
/// fail because it is already open rather than because anything is wrong. With one device
/// attached that check reports SKIP; the failure paths need no device and always run.
/// </para>
/// </remarks>
internal sealed class DeviceDiscoveryChecks : IHardwareCheck
{
    /// <summary>
    /// Friendly name for the device this check opens, distinct from the one under test.
    /// </summary>
    private const string ProbeDeviceName = "serial-probe";

    /// <summary>
    /// Index of the device the harness holds open for the whole run.
    /// </summary>
    /// <remarks>
    /// Owned by <c>Program</c>, which opens this index before any check runs. Repeated here
    /// because this check has to open a <em>different</em> device, and there is no way to ask
    /// the manager which of its devices are already open.
    /// </remarks>
    private const uint DeviceUnderTestIndex = 0;

    /// <summary>
    /// A serial number no attached device will carry in practice, for the not-found path.
    /// </summary>
    /// <remarks>
    /// Not a guarantee: serial numbers come from the device and can hold anything. This one
    /// is unlike any factory value, and a device deliberately given it would make the
    /// not-found check fail loudly rather than pass for the wrong reason.
    /// </remarks>
    private const string UnknownSerial = "hwverify-no-such-serial";

    /// <inheritdoc />
    public string Title => "Device discovery";

    /// <inheritdoc />
    public void Run(RtlSdrManagedDevice device, VerificationReport report)
    {
        RtlSdrDeviceManager manager = RtlSdrDeviceManager.Instance;

        report.Check("the device under test reports a serial number",
            () => !string.IsNullOrWhiteSpace(device.DeviceInfo.Serial),
            "a non-empty serial from enumeration, which is what makes opening by serial possible");

        report.Check("an unknown serial is refused",
            () => VerificationReport.Throws<RtlSdrDeviceException>(
                () => manager.OpenManagedDeviceBySerial(UnknownSerial, ProbeDeviceName)),
            "RtlSdrDeviceException naming the available serials");

        report.Check("an empty serial is refused",
            () => VerificationReport.Throws<ArgumentException>(
                () => manager.OpenManagedDeviceBySerial(string.Empty, ProbeDeviceName)),
            "ArgumentException rather than a device error");

        VerifyOpeningASecondDeviceBySerial(manager, report);
    }

    /// <summary>
    /// Open a device other than the one under test, by its serial number.
    /// </summary>
    /// <param name="manager">Device manager owning the devices.</param>
    /// <param name="report">Report collecting the outcomes.</param>
    /// <remarks>
    /// Uses the first device that is not the one under test, so nothing the rest of the run
    /// depends on is disturbed. The device is closed again before returning.
    /// <para>
    /// Which device that is depends on <c>Program</c>, which opens index 0 for the whole run.
    /// If that ever becomes configurable, this has to follow: excluding the wrong index would
    /// leave the check asking for a device that is already open, which fails for a reason
    /// that has nothing to do with opening by serial.
    /// </para>
    /// </remarks>
    private static void VerifyOpeningASecondDeviceBySerial(RtlSdrDeviceManager manager,
        VerificationReport report)
    {
        DeviceInfo[] candidates = manager.Devices
            .Where(entry => entry.Key != DeviceUnderTestIndex)
            .Select(entry => entry.Value)
            .ToArray();

        if (candidates.Length == 0)
        {
            report.Skip("a device opens by the serial number enumeration reported",
                "only one device is attached, and it is already open as the device under " +
                "test; this needs a second dongle");
            return;
        }

        DeviceInfo expected = candidates[0];

        // A shared factory serial cannot identify one device, so the library refuses it by
        // design. That is the duplicate case, which the unit tests cover; here it would only
        // look like a failure.
        if (candidates.Count(candidate =>
                string.Equals(candidate.Serial, expected.Serial, StringComparison.Ordinal)) > 1
            || string.Equals(manager.Devices[DeviceUnderTestIndex].Serial, expected.Serial,
                StringComparison.Ordinal))
        {
            report.Skip("a device opens by the serial number enumeration reported",
                $"more than one attached device carries the serial '{expected.Serial}', which " +
                "the library refuses on purpose; give the devices distinct serials to run this");
            return;
        }

        try
        {
            report.Check("a device opens by the serial number enumeration reported",
                () =>
                {
                    manager.OpenManagedDeviceBySerial(expected.Serial, ProbeDeviceName);
                    return true;
                },
                $"device at index {expected.Index} opening by serial '{expected.Serial}'");

            report.Check("the device opened by serial is the one that serial named",
                () => manager[ProbeDeviceName].DeviceInfo.Index == expected.Index &&
                      string.Equals(manager[ProbeDeviceName].DeviceInfo.Serial, expected.Serial,
                          StringComparison.Ordinal),
                "the opened device reporting the expected index and serial");

            report.Check("opening the same serial twice is refused by friendly name",
                () => VerificationReport.Throws<ArgumentException>(
                    () => manager.OpenManagedDeviceBySerial(expected.Serial, ProbeDeviceName)),
                "ArgumentException about the friendly name already existing");
        }
        finally
        {
            CloseQuietly(manager);
        }
    }

    /// <summary>
    /// Close the probe device if it was opened, ignoring the case where it was not.
    /// </summary>
    /// <param name="manager">Device manager owning the device.</param>
    private static void CloseQuietly(RtlSdrDeviceManager manager)
    {
        try
        {
            manager.CloseManagedDevice(ProbeDeviceName);
        }
        catch (ArgumentException)
        {
            // Never opened, because the check that opens it failed. Not a finding on its own:
            // that check has already reported.
        }
    }
}
