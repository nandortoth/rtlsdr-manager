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
//
// The source code uses "rtl-sdr" that was released under  GPLv2 license.
// The original work that may be used under the terms of that license,
// please see https://github.com/steve-m/librtlsdr.
//
//   rtl-sdr, turns your Realtek RTL2832 based DVB dongle into a SDR receiver
//   Copyright (C) 2012-2013 by Steve Markgraf <steve@steve-m.de>
//   Copyright (C) 2012 by Dimitri Stolnikov <horiz0n@gmx.net>
//
//   This program is free software: you can redistribute it and/or modify
//   it under the terms of the GNU General Public License as published by
//   the Free Software Foundation, either version 2 of the License, or
//   (at your option) any later version.
//
//   This program is distributed in the hope that it will be useful,
//   but WITHOUT ANY WARRANTY; without even the implied warranty of
//   MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
//   GNU General Public License for more details.
//
//   You should have received a copy of the GNU General Public License
//   along with this program.  If not, see http://www.gnu.org/licenses.

using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace RtlSdrManager.Interop;

/// <summary>
/// Handles cross-platform native library resolution for librtlsdr.
/// </summary>
internal static class LibResolver
{
    private const string LibraryName = "librtlsdr";

    /// <summary>
    /// Registers the custom DLL import resolver for the assembly.
    /// Call this once during initialization.
    /// </summary>
    public static void Register(Assembly assembly) => NativeLibrary.SetDllImportResolver(assembly, Resolve);

    /// <summary>
    /// Custom DLL import resolver for finding librtlsdr on different platforms.
    /// </summary>
    private static IntPtr Resolve(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        IntPtr handle = IntPtr.Zero;

        // Only handle librtlsdr
        if (libraryName != LibraryName)
        {
            return handle;
        }

        // 1. Try environment variable override first
        string? customPath = Environment.GetEnvironmentVariable("RTLSDR_LIBRARY_PATH");
        if (!string.IsNullOrEmpty(customPath) && File.Exists(customPath))
        {
            if (NativeLibrary.TryLoad(customPath, out handle))
            {
                return handle;
            }
        }

        // 2. Try standard resolution
        if (NativeLibrary.TryLoad(libraryName, assembly, searchPath, out handle))
        {
            return handle;
        }

        // 3. Platform-specific fallback paths
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return TryLoadMacOs(out handle) ? handle : IntPtr.Zero;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return TryLoadLinux(out handle) ? handle : IntPtr.Zero;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return TryLoadWindows(out handle) ? handle : IntPtr.Zero;
        }

        return IntPtr.Zero;
    }

    private static bool TryLoadMacOs(out IntPtr handle)
    {
        string[] macPaths =
        [
            "/opt/homebrew/lib/librtlsdr.dylib", // Apple Silicon
            "/opt/homebrew/lib/librtlsdr.0.dylib",
            "/usr/local/lib/librtlsdr.dylib", // Intel Mac
            "/usr/local/lib/librtlsdr.0.dylib"
        ];

        foreach (string path in macPaths)
        {
            if (File.Exists(path) && NativeLibrary.TryLoad(path, out handle))
            {
                return true;
            }
        }

        handle = IntPtr.Zero;
        return false;
    }

    private static bool TryLoadLinux(out IntPtr handle)
    {
        string[] linuxPaths =
        [
            "/usr/lib/librtlsdr.so",
            "/usr/lib/x86_64-linux-gnu/librtlsdr.so",
            "/usr/lib/aarch64-linux-gnu/librtlsdr.so",
            "/usr/local/lib/librtlsdr.so"
        ];

        foreach (string path in linuxPaths)
        {
            if (File.Exists(path) && NativeLibrary.TryLoad(path, out handle))
            {
                return true;
            }
        }

        handle = IntPtr.Zero;
        return false;
    }

    private static bool TryLoadWindows(out IntPtr handle)
    {
        string[] windowsPaths =
        [
            "rtlsdr.dll",
            "librtlsdr.dll",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "rtl-sdr",
                "rtlsdr.dll"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                "rtl-sdr",
                "rtlsdr.dll")
        ];

        foreach (string path in windowsPaths)
        {
            if (NativeLibrary.TryLoad(path, out handle))
            {
                return true;
            }
        }

        handle = IntPtr.Zero;
        return false;
    }
}
