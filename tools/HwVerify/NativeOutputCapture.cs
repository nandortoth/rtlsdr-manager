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

namespace RtlSdrManager.Tools.HwVerify;

/// <summary>
/// Captures what is written to the standard streams at the file descriptor level.
/// </summary>
/// <remarks>
/// Needed because the native driver writes its diagnostics with <c>fprintf</c>, below
/// anything the managed <see cref="Console"/> can see. Redirecting
/// <see cref="Console.Out"/> would capture nothing of interest.
/// </remarks>
internal static partial class NativeOutputCapture
{
    /// <summary>Standard output file descriptor.</summary>
    private const int StdOut = 1;

    /// <summary>Standard error file descriptor, where the driver writes its diagnostics.</summary>
    private const int StdErr = 2;

    /// <summary>Open for writing only. The file itself is created through .NET beforehand.</summary>
    private const int O_WRONLY = 0x0001;

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
    /// True where this capture works, which is anywhere with POSIX file descriptors.
    /// </summary>
    public static bool IsSupported => OperatingSystem.IsLinux() || OperatingSystem.IsMacOS();

    /// <summary>
    /// Run an action with both standard streams pointed at a file, and return what was written.
    /// </summary>
    /// <param name="scenario">The action to run while output is captured.</param>
    /// <returns>
    /// Everything the action wrote to standard output or standard error, or an empty string
    /// if the capture file could not be opened.
    /// </returns>
    /// <remarks>
    /// Both descriptors are captured because the driver writes to standard error while the
    /// suppressor covers both. Restoring in a finally is not optional: leaving either
    /// descriptor pointed at the file would discard the rest of the run's report.
    /// </remarks>
    public static string CaptureOutput(Action scenario)
    {
        string capturePath = Path.Combine(Path.GetTempPath(),
            $"hwverify-capture-{Guid.NewGuid():N}.txt");

        // Create through .NET so the native open needs no creation flags or mode bits.
        File.WriteAllText(capturePath, string.Empty);

        int savedStdOut = NativeDup(StdOut);
        int savedStdErr = NativeDup(StdErr);

        try
        {
            int captureFd = NativeOpen(capturePath, O_WRONLY);
            if (captureFd < 0)
            {
                return string.Empty;
            }

            _ = NativeDup2(captureFd, StdOut);
            _ = NativeDup2(captureFd, StdErr);
            _ = NativeClose(captureFd);

            scenario();
        }
        finally
        {
            _ = NativeDup2(savedStdOut, StdOut);
            _ = NativeDup2(savedStdErr, StdErr);
            _ = NativeClose(savedStdOut);
            _ = NativeClose(savedStdErr);
        }

        string captured = File.ReadAllText(capturePath);
        File.Delete(capturePath);

        return captured;
    }
}
