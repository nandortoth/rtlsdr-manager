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
using RtlSdrManager.Modes;

namespace RtlSdrManager.Tools.HwVerify.Checks;

/// <summary>
/// Verify that console suppression hides the driver's own diagnostics.
/// </summary>
/// <remarks>
/// The unit tests prove the redirection mechanism works. Only a device proves what the feature
/// is for, because only a device makes the driver print anything. Changing the direct sampling
/// mode is the probe: the driver reports it on standard error every time, whether or not the
/// mode actually changes.
/// <para>
/// The check runs twice, and the first run is the one that makes the second meaningful.
/// Asserting that nothing was captured proves little on its own, since a broken capture would
/// look identical; observing the message with suppression off establishes that the capture
/// works before asking whether suppression removes it.
/// </para>
/// </remarks>
internal sealed class ConsoleSuppressionChecks : IHardwareCheck
{
    /// <summary>Fragment of the driver's message about the sampling mode.</summary>
    private const string DriverMessageFragment = "direct sampling";

    /// <inheritdoc />
    public string Title => "Console suppression";

    /// <inheritdoc />
    public void Run(RtlSdrManagedDevice device, VerificationReport report)
    {
        if (!NativeOutputCapture.IsSupported)
        {
            report.Skip("the driver's diagnostics are hidden while suppression is on",
                "this check redirects POSIX file descriptors, so it only runs on Linux and macOS");
            return;
        }

        // The driver announces every direct sampling change, so setting the mode it is already
        // in still produces a line without disturbing the device.
        DirectSamplingModes currentMode = device.DirectSamplingMode;

        try
        {
            string withoutSuppression = NativeOutputCapture.CaptureOutput(() =>
            {
                RtlSdrDeviceManager.SuppressLibraryConsoleOutput = false;
                device.DirectSamplingMode = currentMode;
            });

            report.Check("the driver's diagnostics are visible while suppression is off",
                () => withoutSuppression.Contains(DriverMessageFragment, StringComparison.OrdinalIgnoreCase),
                "the driver's message about the sampling mode; without this the next check " +
                "would pass even if the capture were broken");

            string withSuppression = NativeOutputCapture.CaptureOutput(() =>
            {
                RtlSdrDeviceManager.SuppressLibraryConsoleOutput = true;
                device.DirectSamplingMode = currentMode;
            });

            report.Check("the driver's diagnostics are hidden while suppression is on",
                () => !withSuppression.Contains(DriverMessageFragment, StringComparison.OrdinalIgnoreCase),
                "no output at all; the same operation printed a message a moment ago");
        }
        finally
        {
            // The harness runs with suppression on throughout; put it back.
            RtlSdrDeviceManager.SuppressLibraryConsoleOutput = true;
        }
    }
}
