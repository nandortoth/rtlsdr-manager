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
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Xunit;

namespace RtlSdrManager.Tests;

/// <summary>
/// Tests that console output suppression actually suppresses, rather than merely counting.
/// </summary>
/// <remarks>
/// The scope tests next door assert the reference count, which is arithmetic in
/// <see cref="RtlSdrDeviceManager"/> and holds even if no file descriptor ever moves. The
/// suppressor swallows every failure by design and reports nothing, so without the tests
/// here a completely broken implementation would still look healthy.
/// <para>
/// Writes go straight to file descriptor 1, the way the native driver's own diagnostics do.
/// Going through <see cref="Console"/> would prove nothing: it is a buffered managed stream
/// sitting above the redirection, so it would appear to work whatever the descriptors did.
/// </para>
/// <para>
/// Unix only. The Windows suppressor redirects through a specific C runtime, so a meaningful
/// test there has to write through the same one, and whether that covers the native driver
/// is an open question rather than something these tests can settle.
/// </para>
/// </remarks>
[Collection(nameof(ConsoleSuppressionScopeTests))]
public partial class ConsoleSuppressionBehaviorTests
{
    /// <summary>Standard output file descriptor.</summary>
    private const int StdOut = 1;

    /// <summary>Open for writing only. The file itself is created through .NET beforehand.</summary>
    private const int O_WRONLY = 0x0001;

    [LibraryImport("libc", EntryPoint = "write", SetLastError = true)]
    private static partial nint NativeWrite(int fd, byte[] buffer, nint count);

    [LibraryImport("libc", EntryPoint = "dup", SetLastError = true)]
    private static partial int NativeDup(int fd);

    [LibraryImport("libc", EntryPoint = "dup2", SetLastError = true)]
    private static partial int NativeDup2(int oldFd, int newFd);

    [LibraryImport("libc", EntryPoint = "close", SetLastError = true)]
    private static partial int NativeClose(int fd);

    [LibraryImport("libc", EntryPoint = "open", SetLastError = true,
        StringMarshalling = StringMarshalling.Utf8)]
    private static partial int NativeOpen(string path, int flags);

    /// <summary>
    /// True when these tests can run, which is anywhere the suppressor uses POSIX descriptors.
    /// </summary>
    private static bool IsSupportedPlatform =>
        OperatingSystem.IsLinux() || OperatingSystem.IsMacOS();

    /// <summary>
    /// Emit a line straight to file descriptor 1, bypassing the managed console.
    /// </summary>
    /// <param name="text">Line to write.</param>
    private static void Emit(string text)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(text + "\n");
        _ = NativeWrite(StdOut, bytes, bytes.Length);
    }

    /// <summary>
    /// Run an action with file descriptor 1 pointed at a file, and return what was written.
    /// </summary>
    /// <param name="scenario">The action to run while output is captured.</param>
    /// <returns>Everything the action wrote to descriptor 1.</returns>
    /// <remarks>
    /// The suppressor saves and restores whatever descriptor 1 happens to be, so this capture
    /// nests correctly underneath it. Restoring in a finally is not optional: leaving
    /// descriptor 1 pointed elsewhere would silently discard the output of every later test.
    /// </remarks>
    private static string CaptureStandardOutput(Action scenario)
    {
        string capturePath = Path.Combine(Path.GetTempPath(),
            $"rtlsdrmanager-suppression-{Guid.NewGuid():N}.txt");

        // Create through .NET so the native open needs no creation flags or mode bits.
        File.WriteAllText(capturePath, string.Empty);

        int savedStdOut = NativeDup(StdOut);
        Assert.True(savedStdOut >= 0, "could not duplicate the standard output descriptor");

        try
        {
            int captureFd = NativeOpen(capturePath, O_WRONLY);
            Assert.True(captureFd >= 0, $"could not open the capture file at {capturePath}");

            _ = NativeDup2(captureFd, StdOut);
            _ = NativeClose(captureFd);

            scenario();
        }
        finally
        {
            _ = NativeDup2(savedStdOut, StdOut);
            _ = NativeClose(savedStdOut);
        }

        string captured = File.ReadAllText(capturePath);
        File.Delete(capturePath);

        return captured;
    }

    /// <summary>
    /// Run a scenario with the suppression flag set, always clearing it afterward.
    /// </summary>
    /// <param name="suppress">Value for the suppression flag.</param>
    /// <param name="scenario">The action to run while output is captured.</param>
    /// <returns>Everything the action wrote to descriptor 1.</returns>
    private static string CaptureWithSuppression(bool suppress, Action scenario)
    {
        RtlSdrDeviceManager.SuppressLibraryConsoleOutput = suppress;

        try
        {
            return CaptureStandardOutput(scenario);
        }
        finally
        {
            RtlSdrDeviceManager.SuppressLibraryConsoleOutput = false;
        }
    }

    [Fact]
    public void WritesInsideAnActiveScopeAreSuppressed()
    {
        if (!IsSupportedPlatform)
        {
            return;
        }

        string captured = CaptureWithSuppression(true, () =>
        {
            using var scope = new RtlSdrDeviceManager.SuppressionScope();
            Emit("inside-the-scope");
        });

        Assert.DoesNotContain("inside-the-scope", captured);
    }

    [Fact]
    public void WritesAfterTheScopeReachTheConsoleAgain()
    {
        if (!IsSupportedPlatform)
        {
            return;
        }

        // Guards the restore path. A suppressor that redirects but never restores would leave
        // standard output discarded for the rest of the process, which the reference count
        // cannot notice.
        string captured = CaptureWithSuppression(true, () =>
        {
            using (new RtlSdrDeviceManager.SuppressionScope())
            {
                Emit("inside-the-scope");
            }

            Emit("after-the-scope");
        });

        Assert.DoesNotContain("inside-the-scope", captured);
        Assert.Contains("after-the-scope", captured);
    }

    [Fact]
    public void WritesAreNotSuppressedWhenTheFlagIsDisabled()
    {
        if (!IsSupportedPlatform)
        {
            return;
        }

        // The default path has to stay a genuine no-op: a scope entered with suppression off
        // must not touch the descriptors at all.
        string captured = CaptureWithSuppression(false, () =>
        {
            using var scope = new RtlSdrDeviceManager.SuppressionScope();
            Emit("not-suppressed");
        });

        Assert.Contains("not-suppressed", captured);
    }

    [Fact]
    public void TheInnerOfTwoNestedScopesDoesNotRestoreEarly()
    {
        if (!IsSupportedPlatform)
        {
            return;
        }

        // The reference counting exists so that a nested scope closing does not lift
        // suppression while the outer one is still open. Checked here by its effect rather
        // than by the counter.
        string captured = CaptureWithSuppression(true, () =>
        {
            using (new RtlSdrDeviceManager.SuppressionScope())
            {
                using (new RtlSdrDeviceManager.SuppressionScope())
                {
                    Emit("inside-both-scopes");
                }

                Emit("after-the-inner-scope");
            }

            Emit("after-both-scopes");
        });

        Assert.DoesNotContain("inside-both-scopes", captured);
        Assert.DoesNotContain("after-the-inner-scope", captured);
        Assert.Contains("after-both-scopes", captured);
    }
}
