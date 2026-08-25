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

namespace RtlSdrManager.Tools.HwVerify.Checks;

/// <summary>
/// Verify what a disposed device allows and what it refuses.
/// </summary>
/// <remarks>
/// None of this can be unit tested. A managed device is only reachable by opening a real one,
/// so the test project cannot produce a disposed instance to ask questions of.
/// <para>
/// The device is disposed by closing it, which is what the manager does, and the checks then
/// run against the reference captured beforehand. Reopening happens in a finally block: every
/// later check resolves the device by name, so leaving it closed would fail the rest of the
/// run for the wrong reason.
/// </para>
/// <para>
/// One cost worth knowing: this adds a close and reopen to the run, which is the pattern the
/// library documents as risky on macOS. A single extra cycle is a small, deliberate increase
/// in the chance of a run ending in the USB layer.
/// </para>
/// </remarks>
internal sealed class DisposalChecks : IHardwareCheck
{
    /// <summary>
    /// Closes the device under test and opens it again.
    /// </summary>
    private readonly Func<RtlSdrManagedDevice> _reopenDevice;

    /// <summary>
    /// Create the disposal checks.
    /// </summary>
    /// <param name="reopenDevice">Closes the device under test and opens it again.</param>
    public DisposalChecks(Func<RtlSdrManagedDevice> reopenDevice) => _reopenDevice = reopenDevice;

    /// <inheritdoc />
    public string Title => "Disposal";

    /// <inheritdoc />
    public void Run(RtlSdrManagedDevice device, VerificationReport report)
    {
        // Read this before disposing: it is the value the checks below compare against, and
        // reading it afterwards is one of the things being verified.
        string serialBeforeDisposal = device.DeviceInfo.Serial;

        try
        {
            // Closing the managed device disposes it. The reference captured above stays
            // usable as a handle onto the disposed instance.
            _reopenDevice();

            VerifyInspectionStillWorks(device, serialBeforeDisposal, report);
            VerifyOperationsAreRefused(device, report);

            // Last, because a regression here does not fail the check: it ends the process.
            VerifyStartingAReadingIsRefused(device, report);
        }
        finally
        {
            // Every later check resolves the device by name, so it has to exist again even if
            // something above threw.
            _reopenDevice();
        }
    }

    /// <summary>
    /// Verify that a disposed device can still be asked what it was and what it recorded.
    /// </summary>
    /// <param name="device">The disposed device.</param>
    /// <param name="expectedSerial">Serial read before disposal.</param>
    /// <param name="report">Report collecting the outcomes.</param>
    private static void VerifyInspectionStillWorks(RtlSdrManagedDevice device,
        string expectedSerial, VerificationReport report)
    {
        report.Check("DeviceInfo is readable after disposal",
            () => device.DeviceInfo.Serial == expectedSerial,
            "the same serial as before, so a cleanup path can still log which device it was");

        report.Check("ToString() works after disposal",
            () => !string.IsNullOrWhiteSpace(device.ToString()),
            "a description rather than an exception; debuggers call this while inspecting");

        report.Check("AsyncReadException is readable after disposal",
            () =>
            {
                _ = device.AsyncReadException;
                return true;
            },
            "no exception; this is how a failed disposal reports its cause");

        report.Check("DroppedSamplesCount is readable after disposal",
            () =>
            {
                _ = device.DroppedSamplesCount;
                return true;
            },
            "no exception; what a finished session lost is a fair question afterwards");

        report.Check("configuration is readable after disposal",
            () =>
            {
                _ = device.MaxAsyncBufferSize;
                _ = device.DropSamplesOnFullBuffer;
                _ = device.TransferBufferCount;
                return true;
            },
            "no exception; these getters are also read on the sample delivery thread");
    }

    /// <summary>
    /// Verify that a disposed device refuses to be configured or operated.
    /// </summary>
    /// <param name="device">The disposed device.</param>
    /// <param name="report">Report collecting the outcomes.</param>
    private static void VerifyOperationsAreRefused(RtlSdrManagedDevice device,
        VerificationReport report)
    {
        report.Check("configuring a disposed device throws",
            () => VerificationReport.Throws<ObjectDisposedException>(
                () => device.TransferBufferCount = 8),
            "ObjectDisposedException rather than a write that silently does nothing");

        report.Check("the exception names the device, not the handle underneath it",
            () =>
            {
                try
                {
                    device.UseRawBufferMode = true;
                    return false;
                }
                catch (ObjectDisposedException exception)
                {
                    // Checked by intent rather than against a literal: the framework reports
                    // the namespace-qualified type name, and the point is which type it is.
                    string name = exception.ObjectName;

                    return name.Contains(nameof(RtlSdrManagedDevice), StringComparison.Ordinal)
                           && !name.Contains("SafeRtlSdrHandle", StringComparison.Ordinal);
                }
            },
            "ObjectName naming RtlSdrManagedDevice; unguarded members name the handle instead");

        report.Check("resetting the dropped sample counter throws",
            () => VerificationReport.Throws<ObjectDisposedException>(
                device.ResetDroppedSamplesCounter),
            "ObjectDisposedException; the counter is readable but not resettable");

        report.Check("reading the tuner type throws",
            () => VerificationReport.Throws<ObjectDisposedException>(() => _ = device.TunerType),
            "ObjectDisposedException whether or not it was read before disposal");

        report.Check("synchronous reading throws",
            () => VerificationReport.Throws<ObjectDisposedException>(
                () => device.ReadSamples(256)),
            "ObjectDisposedException");

        report.Check("stopping a reading throws instead of quietly succeeding",
            () => VerificationReport.Throws<ObjectDisposedException>(
                device.StopReadSamplesAsync),
            "ObjectDisposedException; on a live device this is a harmless no-op");

        report.Check("the async buffer reports disposal, not a missing reading",
            () => VerificationReport.Throws<ObjectDisposedException>(() => _ = device.AsyncBuffer),
            "ObjectDisposedException rather than \"not initialized yet\"");

        report.Check("disposing twice is harmless",
            () =>
            {
                device.Dispose();
                return true;
            },
            "no exception; disposal is idempotent by contract");
    }

    /// <summary>
    /// Verify that starting a reading on a disposed device is refused.
    /// </summary>
    /// <param name="device">The disposed device.</param>
    /// <param name="report">Report collecting the outcomes.</param>
    /// <remarks>
    /// This is the check the whole group exists for, and it is unlike the others: without the
    /// guard the call does not fail, it starts a worker that hands a released handle to the
    /// native reader on a thread with no exception handler, and the process dies. A regression
    /// here therefore ends the run rather than reporting a failure, which is why it goes last.
    /// </remarks>
    private static void VerifyStartingAReadingIsRefused(RtlSdrManagedDevice device,
        VerificationReport report)
    {
        report.Check("starting a reading on a disposed device throws rather than killing the process",
            () => VerificationReport.Throws<ObjectDisposedException>(
                () => device.StartReadSamplesAsync()),
            "ObjectDisposedException on this thread; unguarded, this check would take the " +
            "whole run down instead of failing");
    }
}
