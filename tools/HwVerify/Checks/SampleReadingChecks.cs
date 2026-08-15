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
using System.Threading;
using RtlSdrManager.Exceptions;
using RtlSdrManager.Modes;

namespace RtlSdrManager.Tools.HwVerify.Checks;

/// <summary>
/// Verify that samples actually flow, in every delivery mode the library offers.
/// </summary>
/// <remarks>
/// This is the library's purpose and the part with the worst bug history, yet neither the test
/// suite nor the rest of this harness touches it. The suite cannot: a device can only be
/// constructed by opening real hardware.
/// <para>
/// Every group of checks gets a device that has never streamed. Starting a second reading on a
/// device that was only stopped leaves a canceled transfer able to complete against memory the
/// driver has already released, which crashes the process on macOS often enough to matter.
/// Reopening is also how the device is put back afterward: handing back a freshly opened
/// device is a stronger guarantee than undoing settings one at a time, and it cannot be left
/// half done by a check that throws.
/// </para>
/// </remarks>
internal sealed class SampleReadingChecks : IHardwareCheck
{
    /// <summary>Sample rate used throughout; comfortably inside the supported range.</summary>
    private const uint SampleRateHz = 2_048_000;

    /// <summary>Samples requested per read. One default asynchronous buffer's worth.</summary>
    private const int ReadLength = 16384;

    /// <summary>How long a single device read may take before it counts as failed.</summary>
    private static readonly TimeSpan ReadDeadline = TimeSpan.FromSeconds(10);

    /// <summary>How long to wait for asynchronous delivery to start or for an error to surface.</summary>
    private static readonly TimeSpan DeliveryDeadline = TimeSpan.FromSeconds(5);

    /// <summary>How long to allow for a stop, which itself waits up to five seconds.</summary>
    private static readonly TimeSpan StopDeadline = TimeSpan.FromSeconds(11);

    /// <summary>Closes the device under test and opens it again, returning the new instance.</summary>
    private readonly Func<RtlSdrManagedDevice> _reopenDevice;

    /// <summary>
    /// Create the sample reading checks.
    /// </summary>
    /// <param name="reopenDevice">Closes the device under test and opens it again.</param>
    public SampleReadingChecks(Func<RtlSdrManagedDevice> reopenDevice)
    {
        _reopenDevice = reopenDevice;
    }

    /// <inheritdoc />
    public string Title => "Sample reading";

    /// <inheritdoc />
    public void Run(RtlSdrManagedDevice device, VerificationReport report)
    {
        try
        {
            // Runs on an unconfigured device, which is the only state where it is observable.
            VerifyUnsetSampleRateIsAUsageError(_reopenDevice(), report);

            RunOnFreshDevice(VerifySynchronousReading, report);
            RunOnFreshDevice(VerifyAsynchronousReading, report);
            RunOnFreshDevice(VerifyReadingAgainAfterReopening, report);
            RunOnFreshDevice(VerifyRawBufferMode, report);
            RunOnFreshDevice(VerifyBufferFullIsReported, report);

            // Last on purpose: if the invariant it checks is broken, the process dies here
            // rather than printing a result, so everything else has already been reported.
            RunOnFreshDevice(VerifyCallbackBoundaryHoldsAgainstAThrowingHandler, report);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  WARN  the sample reading checks could not complete: {ex.Message}");
        }
        finally
        {
            // Hands the next check a device in its opened state, whatever happened above.
            _reopenDevice();
        }
    }

    /// <summary>
    /// Run a group of checks against a newly opened and configured device.
    /// </summary>
    /// <param name="checks">The group to run.</param>
    /// <param name="report">Report collecting the outcomes.</param>
    private void RunOnFreshDevice(Action<RtlSdrManagedDevice, VerificationReport> checks,
        VerificationReport report) =>
        checks(Configure(_reopenDevice()), report);

    /// <summary>
    /// Put a device into a state where samples can be read.
    /// </summary>
    /// <param name="device">The device to configure.</param>
    /// <returns>The same device, configured.</returns>
    /// <remarks>
    /// The center frequency is taken from the middle of whatever the tuner reports, so this
    /// works on any tuner without hardcoding a band. Nothing is being demodulated, so the exact
    /// frequency does not matter.
    /// </remarks>
    private static RtlSdrManagedDevice Configure(RtlSdrManagedDevice device)
    {
        device.SampleRate = Frequency.FromHz(SampleRateHz);

        FrequencyRange range = device.SupportedFrequencyRanges[0];
        device.CenterFrequency =
            Frequency.FromHz(range.Minimum.Hz + ((range.Maximum.Hz - range.Minimum.Hz) / 2));

        // Makes the received bytes predictable, which is what lets these checks assert content
        // rather than just quantity.
        device.TestMode = TestModes.Enabled;
        device.ResetDeviceBuffer();

        return device;
    }

    /// <summary>
    /// Verify that reading the sample rate before setting one is reported as a usage error.
    /// </summary>
    /// <param name="device">A freshly opened device, which has no sample rate yet.</param>
    /// <param name="report">Report collecting the outcomes.</param>
    /// <remarks>
    /// The device reports zero until a rate is written, and the library used to treat that as a
    /// failed read and blame the device. This harness found it on its first run, by doing what
    /// the samples never do: reading the rate before setting one.
    /// </remarks>
    private static void VerifyUnsetSampleRateIsAUsageError(RtlSdrManagedDevice device,
        VerificationReport report) =>
        report.Check("reading SampleRate before setting it throws InvalidOperationException",
            () => VerificationReport.Throws<InvalidOperationException>(() => _ = device.SampleRate),
            "InvalidOperationException naming the cause; it was RtlSdrLibraryExecutionException " +
            "reporting \"Error code: 0\" before 0.8.0");

    /// <summary>
    /// Verify the blocking read path.
    /// </summary>
    /// <param name="device">The device under test.</param>
    /// <param name="report">Report collecting the outcomes.</param>
    private static void VerifySynchronousReading(RtlSdrManagedDevice device, VerificationReport report)
    {
        List<IQData>? samples = null;

        report.Check($"ReadSamples returns the requested {ReadLength} samples",
            () =>
            {
                Deadline.Run(() => samples = device.ReadSamples(ReadLength), ReadDeadline);
                return samples is { Count: ReadLength };
            },
            $"{ReadLength} samples; a synchronous read has no timeout of its own, so a " +
            "TimeoutException here means the device stopped delivering");

        report.Check("the received samples are the demodulator's counter, in order",
            () => samples is not null && TestModeCounter.Matches(samples),
            "each byte one greater than the last, wrapping at 256; anything else means the " +
            "data path reorders, drops or corrupts bytes");

        report.Check("ReadSamples(0) returns nothing without touching the device",
            () => device.ReadSamples(0).Count == 0,
            "an empty list, returned immediately");

        report.Check("ReadSamples rejects a negative count",
            () => VerificationReport.Throws<ArgumentOutOfRangeException>(() => device.ReadSamples(-1)),
            "ArgumentOutOfRangeException");
    }

    /// <summary>
    /// Verify the asynchronous path in its default per-sample delivery mode.
    /// </summary>
    /// <param name="device">The device under test.</param>
    /// <param name="report">Report collecting the outcomes.</param>
    private static void VerifyAsynchronousReading(RtlSdrManagedDevice device, VerificationReport report)
    {
        // Without this the default 65536 sample buffer fills after four reads and the reading
        // stops itself, which is a separate check further down.
        device.DropSamplesOnFullBuffer = true;

        int eventCount = 0;
        void OnSamplesAvailable(object? sender, SamplesAvailableEventArgs e) =>
            Interlocked.Increment(ref eventCount);

        device.SamplesAvailable += OnSamplesAvailable;

        try
        {
            device.StartReadSamplesAsync();

            report.Check("samples arrive after StartReadSamplesAsync",
                () => Deadline.WaitUntil(() => device.AsyncBuffer.Count > 0, DeliveryDeadline),
                $"a non-empty async buffer within {DeliveryDeadline.TotalSeconds:0} s");

            report.Check("the SamplesAvailable event is raised",
                () => Volatile.Read(ref eventCount) > 0,
                "at least one event; handlers run on the native callback thread");

            report.Check("GetSamplesFromAsyncBuffer returns the counter, in order",
                () => DeliversTheCounter(device),
                "samples continuing the demodulator's counter");

            report.Check("no error is recorded during a healthy reading",
                () => device.AsyncReadException is null,
                "AsyncReadException still null");

            report.Check("starting a second reading while one is running throws",
                () => VerificationReport.Throws<RtlSdrLibraryExecutionException>(
                    () => device.StartReadSamplesAsync()),
                "RtlSdrLibraryExecutionException about the worker already running");

            report.Check("StopReadSamplesAsync completes",
                () =>
                {
                    Deadline.Run(device.StopReadSamplesAsync, StopDeadline);
                    return true;
                },
                "a clean stop; the bounded join inside gives up after 5 s");

            report.Check("stopping when nothing is running is harmless",
                () =>
                {
                    device.StopReadSamplesAsync();
                    return true;
                },
                "no exception");
        }
        finally
        {
            device.SamplesAvailable -= OnSamplesAvailable;
            StopQuietly(device);
        }
    }

    /// <summary>
    /// Verify that a device which has already streamed can stream again once reopened.
    /// </summary>
    /// <param name="device">A device that was closed and opened again after a first reading.</param>
    /// <param name="report">Report collecting the outcomes.</param>
    /// <remarks>
    /// This is the supported way to take a second reading, and it replaces a check that used to
    /// call <c>StartReadSamplesAsync</c> again on a device that had only been stopped. Measured
    /// over repeated runs, that pattern crashed about half the time against about a sixth for
    /// reopening, so this is the safer of the two rather than a cure. The library's own
    /// per-session state resets correctly either way; what is being avoided is a fault in the
    /// layer below, not a defect here.
    /// </remarks>
    private static void VerifyReadingAgainAfterReopening(RtlSdrManagedDevice device,
        VerificationReport report)
    {
        device.DropSamplesOnFullBuffer = true;

        try
        {
            device.StartReadSamplesAsync();

            report.Check("a reopened device streams again",
                () => Deadline.WaitUntil(() => device.AsyncBuffer.Count > 0, DeliveryDeadline),
                $"samples within {DeliveryDeadline.TotalSeconds:0} s on a device that has " +
                "already completed one reading");

            report.Check("the second reading delivers the counter, in order",
                () => DeliversTheCounter(device),
                "samples continuing the demodulator's counter");
        }
        finally
        {
            StopQuietly(device);
        }
    }

    /// <summary>
    /// Verify the zero-copy delivery mode.
    /// </summary>
    /// <param name="device">The device under test.</param>
    /// <param name="report">Report collecting the outcomes.</param>
    private static void VerifyRawBufferMode(RtlSdrManagedDevice device, VerificationReport report)
    {
        device.UseRawBufferMode = true;

        try
        {
            device.StartReadSamplesAsync();

            RawSampleBuffer? buffer = null;
            bool delivered = Deadline.WaitUntil(
                () => (buffer = device.GetRawSamplesFromAsyncBuffer()) is not null, DeliveryDeadline);

            try
            {
                report.Check("raw buffer mode delivers a buffer",
                    () => delivered && buffer is not null,
                    $"a RawSampleBuffer within {DeliveryDeadline.TotalSeconds:0} s");

                report.Check("the buffer reports half as many samples as bytes",
                    () => buffer is not null && buffer.SampleCount == buffer.ByteLength / 2,
                    "SampleCount equal to ByteLength / 2");

                report.Check("the raw bytes are the demodulator's counter, in order",
                    () => buffer is not null && TestModeCounter.Matches(buffer),
                    "each byte one greater than the last, wrapping at 256");
            }
            finally
            {
                // Pooled: returning it exactly once is the caller's job, failure or not.
                buffer?.Return();
            }
        }
        finally
        {
            StopQuietly(device);
        }
    }

    /// <summary>
    /// Verify that a full buffer stops the reading and is reported rather than ignored.
    /// </summary>
    /// <param name="device">The device under test.</param>
    /// <param name="report">Report collecting the outcomes.</param>
    /// <remarks>
    /// With dropping disabled the library is expected to record an error and stop, from inside
    /// the native callback. That path used to crash the process, so it is worth exercising
    /// against real hardware rather than a stand-in.
    /// </remarks>
    private static void VerifyBufferFullIsReported(RtlSdrManagedDevice device, VerificationReport report)
    {
        device.DropSamplesOnFullBuffer = false;

        try
        {
            device.StartReadSamplesAsync();

            // Deliberately not draining: the buffer holds four reads' worth and fills quickly.
            report.Check("an overrun with dropping disabled is recorded rather than ignored",
                () => Deadline.WaitUntil(() => device.AsyncReadException is not null, DeliveryDeadline),
                $"AsyncReadException set within {DeliveryDeadline.TotalSeconds:0} s, because the " +
                "buffer holds only four reads and nothing is consuming it");

            report.Check("the recorded error is rethrown by StopReadSamplesAsync",
                () => VerificationReport.Throws<RtlSdrManagedDeviceException>(device.StopReadSamplesAsync),
                "RtlSdrManagedDeviceException wrapping the captured error");
        }
        finally
        {
            StopQuietly(device);
        }
    }

    /// <summary>
    /// Verify that an exception thrown by a subscriber never reaches the native caller.
    /// </summary>
    /// <param name="device">The device under test.</param>
    /// <param name="report">Report collecting the outcomes.</param>
    /// <remarks>
    /// An exception crossing back into native code terminates the process, so this check has an
    /// unusual failure mode: if the invariant is broken the run dies here instead of printing a
    /// result. That is why it runs last. A crash at this point is the finding.
    /// </remarks>
    private static void VerifyCallbackBoundaryHoldsAgainstAThrowingHandler(
        RtlSdrManagedDevice device, VerificationReport report)
    {
        void ThrowingHandler(object? sender, SamplesAvailableEventArgs e) =>
            throw new InvalidOperationException("deliberate failure from a SamplesAvailable handler");

        device.SamplesAvailable += ThrowingHandler;

        try
        {
            device.StartReadSamplesAsync();

            report.Check("a throwing SamplesAvailable handler is captured, not propagated",
                () => Deadline.WaitUntil(() => device.AsyncReadException is not null, DeliveryDeadline),
                "AsyncReadException set; reaching this line at all means the exception did not " +
                "cross the native boundary, which would have terminated the process");

            report.Check("the handler's error is rethrown by StopReadSamplesAsync",
                () => VerificationReport.Throws<RtlSdrManagedDeviceException>(device.StopReadSamplesAsync),
                "RtlSdrManagedDeviceException wrapping the handler's exception");
        }
        finally
        {
            device.SamplesAvailable -= ThrowingHandler;
            StopQuietly(device);
        }
    }

    /// <summary>
    /// Take samples from the async buffer and decide whether they continue the counter.
    /// </summary>
    /// <param name="device">A device with a reading in progress.</param>
    /// <returns>True when samples were available and formed the counter.</returns>
    private static bool DeliversTheCounter(RtlSdrManagedDevice device)
    {
        List<IQData> samples = device.GetSamplesFromAsyncBuffer(ReadLength);

        return samples.Count > 0 && TestModeCounter.Matches(samples);
    }

    /// <summary>
    /// Stop an asynchronous reading, ignoring whatever it reports.
    /// </summary>
    /// <param name="device">The device under test.</param>
    /// <remarks>
    /// Used on cleanup paths where a captured error has already been reported by a check, or
    /// where the reading may not be running at all. Both are expected here.
    /// </remarks>
    private static void StopQuietly(RtlSdrManagedDevice device)
    {
        try
        {
            device.StopReadSamplesAsync();
        }
        catch (Exception)
        {
            // Already reported, or nothing was running.
        }
    }
}
