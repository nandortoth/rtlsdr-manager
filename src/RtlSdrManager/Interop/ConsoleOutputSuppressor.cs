// RTL-SDR Manager Library for .NET
// Copyright (C) 2018-2025 Nandor Toth <dev@nandortoth.com>
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
using System.Runtime.InteropServices;

namespace RtlSdrManager.Interop;

/// <summary>
/// Utility class to temporarily suppress console output (stdout and stderr) at the native level.
/// This is useful for suppressing diagnostic messages from the native librtlsdr library.
/// </summary>
internal sealed partial class ConsoleOutputSuppressor : IDisposable
{
    private readonly int _oldStdout;
    private readonly int _oldStderr;
    private readonly bool _isSupported;

    /// <summary>
    /// Initializes a new instance of the <see cref="ConsoleOutputSuppressor"/> class.
    /// Redirects native stdout and stderr to /dev/null (Unix) or NUL (Windows).
    /// </summary>
    public ConsoleOutputSuppressor()
    {
        _isSupported = false;

        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // Windows implementation
                // Flush stdout and stderr buffers first
                _ = Windows.fflush(IntPtr.Zero);  // Flush all streams

                _oldStdout = Windows._dup(1);  // Duplicate stdout
                _oldStderr = Windows._dup(2);  // Duplicate stderr

                // Open NUL device
                int nullFile = Windows._open("NUL", Windows.O_WRONLY);
                if (nullFile == -1)
                {
                    return;
                }

                _ = Windows._dup2(nullFile, 1);  // Redirect stdout to NUL
                _ = Windows._dup2(nullFile, 2);  // Redirect stderr to NUL
                _ = Windows._close(nullFile);
                _isSupported = true;
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ||
                     RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                // Unix/Linux/macOS implementation
                // Flush stdout and stderr buffers first
                _ = Unix.fflush(IntPtr.Zero);  // Flush all streams

                _oldStdout = Unix.dup(1);  // Duplicate stdout
                _oldStderr = Unix.dup(2);  // Duplicate stderr

                // Open /dev/null
                int nullFile = Unix.open("/dev/null", Unix.O_WRONLY, 0);
                if (nullFile == -1)
                {
                    return;
                }

                _ = Unix.dup2(nullFile, 1);  // Redirect stdout to /dev/null
                _ = Unix.dup2(nullFile, 2);  // Redirect stderr to /dev/null
                _ = Unix.close(nullFile);
                _isSupported = true;
            }
        }
        catch
        {
            // If anything fails, silently continue without suppression
            _isSupported = false;
        }
    }

    /// <summary>
    /// Restores the original stdout and stderr file descriptors.
    /// </summary>
    public void Dispose()
    {
        if (!_isSupported)
        {
            return;
        }

        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                _ = Windows.fflush(IntPtr.Zero);  // Flush all streams before restoring
                _ = Windows._dup2(_oldStdout, 1);  // Restore stdout
                _ = Windows._dup2(_oldStderr, 2);  // Restore stderr
                _ = Windows._close(_oldStdout);
                _ = Windows._close(_oldStderr);
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ||
                     RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                _ = Unix.fflush(IntPtr.Zero);  // Flush all streams before restoring
                _ = Unix.dup2(_oldStdout, 1);  // Restore stdout
                _ = Unix.dup2(_oldStderr, 2);  // Restore stderr
                _ = Unix.close(_oldStdout);
                _ = Unix.close(_oldStderr);
            }
        }
        catch
        {
            // Silently fail - best effort restoration
        }
    }

    /// <summary>
    /// Native Windows C runtime functions for file descriptor manipulation.
    /// </summary>
    private static partial class Windows
    {
        public const int O_WRONLY = 0x0001;

        [LibraryImport("msvcrt.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
        [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
        public static partial int _open(string filename, int oflag);

        [LibraryImport("msvcrt.dll", SetLastError = true)]
        [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
        public static partial int _dup(int fd);

        [LibraryImport("msvcrt.dll", SetLastError = true)]
        [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
        public static partial int _dup2(int fd1, int fd2);

        [LibraryImport("msvcrt.dll", SetLastError = true)]
        [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
        public static partial int _close(int fd);

        [LibraryImport("msvcrt.dll", SetLastError = true)]
        [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
        public static partial int fflush(IntPtr stream);
    }

    /// <summary>
    /// Native Unix/Linux/macOS POSIX functions for file descriptor manipulation.
    /// </summary>
    private static partial class Unix
    {
        public const int O_WRONLY = 0x0001;

        [LibraryImport("libc", EntryPoint = "open", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
        [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
        public static partial int open(string pathname, int flags, int mode);

        [LibraryImport("libc", EntryPoint = "dup", SetLastError = true)]
        [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
        public static partial int dup(int oldfd);

        [LibraryImport("libc", EntryPoint = "dup2", SetLastError = true)]
        [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
        public static partial int dup2(int oldfd, int newfd);

        [LibraryImport("libc", EntryPoint = "close", SetLastError = true)]
        [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
        public static partial int close(int fd);

        [LibraryImport("libc", EntryPoint = "fflush", SetLastError = true)]
        [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
        public static partial int fflush(IntPtr stream);
    }
}
