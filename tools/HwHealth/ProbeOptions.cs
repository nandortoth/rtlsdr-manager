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
using System.Globalization;

namespace RtlSdrManager.Tools.HwHealth;

/// <summary>
/// What a single run was asked to do.
/// </summary>
/// <param name="Window">How long to stream for.</param>
/// <param name="Index">Device index to probe, ignored when a serial is given.</param>
/// <param name="Serial">Serial of the device to probe, or null to use the index.</param>
/// <param name="Quiet">Whether to print the verdict alone, for use in a script.</param>
/// <param name="HelpRequested">Whether the usage text was asked for.</param>
internal sealed record ProbeOptions(
    TimeSpan Window,
    uint Index,
    string? Serial,
    bool Quiet,
    bool HelpRequested)
{
    /// <summary>
    /// Default measurement window, long enough for the rate to settle and short enough to sit
    /// in front of.
    /// </summary>
    private static readonly TimeSpan DefaultWindow = TimeSpan.FromSeconds(5);

    /// <summary>
    /// The usage text.
    /// </summary>
    public static string UsageText =>
        """
        HwHealth - is the attached device sustaining sample delivery right now?

          --seconds N   how long to stream for (default 5)
          --index N     device index to probe (default 0)
          --serial S    probe the device with this serial instead
          --quiet       print the verdict alone, for use in a script
          --help        print this text

        Exit codes: 0 healthy, 1 health could not be established, 2 no device attached.
        Set RTLSDR_LIBRARY_PATH to probe against a specific native build.
        """;

    /// <summary>
    /// Parse the command line.
    /// </summary>
    /// <param name="args">Command line arguments.</param>
    /// <param name="options">The parsed options, when parsing succeeded.</param>
    /// <param name="error">What was wrong, when it did not.</param>
    /// <returns>True when the command line was understood.</returns>
    /// <remarks>
    /// A malformed value fails rather than falling back to the default. Silently ignoring a
    /// typo would answer a different question from the one that was asked, and this tool exists
    /// to be trusted about what it measured.
    /// </remarks>
    public static bool TryParse(string[] args, out ProbeOptions options, out string? error)
    {
        options = new ProbeOptions(DefaultWindow, 0, null, false, false);
        error = null;

        if (Array.IndexOf(args, "--help") >= 0)
        {
            options = options with { HelpRequested = true };

            return true;
        }

        TimeSpan window = DefaultWindow;

        if (TryReadValue(args, "--seconds", out string? rawSeconds))
        {
            if (!double.TryParse(rawSeconds, NumberStyles.Float, CultureInfo.InvariantCulture,
                    out double seconds) || seconds <= 0)
            {
                error = $"--seconds needs a positive number, not '{rawSeconds}'.";

                return false;
            }

            window = TimeSpan.FromSeconds(seconds);
        }

        uint index = 0;

        if (TryReadValue(args, "--index", out string? rawIndex) &&
            !uint.TryParse(rawIndex, NumberStyles.Integer, CultureInfo.InvariantCulture,
                out index))
        {
            error = $"--index needs a device index, not '{rawIndex}'.";

            return false;
        }

        TryReadValue(args, "--serial", out string? serial);

        options = new ProbeOptions(window, index, serial,
            Array.IndexOf(args, "--quiet") >= 0, false);

        return true;
    }

    /// <summary>
    /// Read the value that follows an option.
    /// </summary>
    /// <param name="args">Command line arguments.</param>
    /// <param name="name">Option name, including the leading dashes.</param>
    /// <param name="value">The value, when the option was present and had one.</param>
    /// <returns>True when the option was present with a value.</returns>
    private static bool TryReadValue(string[] args, string name, out string? value)
    {
        int at = Array.IndexOf(args, name);
        value = at >= 0 && at + 1 < args.Length ? args[at + 1] : null;

        return value is not null;
    }
}
