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

namespace RtlSdrManager.Tools.HwStress;

/// <summary>
/// What a single invocation was asked to do.
/// </summary>
/// <param name="Pattern">Which lifecycle to cycle.</param>
/// <param name="Cycles">Cycles per run.</param>
/// <param name="Runs">How many runs, each in its own process.</param>
/// <param name="Index">Device index to use.</param>
/// <param name="StallBudget">How long a run may make no progress before it is called stalled.</param>
/// <param name="IsWorker">Whether this process runs the cycles rather than supervising them.</param>
/// <param name="HelpRequested">Whether the usage text was asked for.</param>
internal sealed record StressOptions(
    CyclePattern Pattern,
    int Cycles,
    int Runs,
    uint Index,
    TimeSpan StallBudget,
    bool IsWorker,
    bool HelpRequested)
{
    /// <summary>
    /// How long a run may complete no cycles before it is called stalled.
    /// </summary>
    /// <remarks>
    /// Generous on purpose. A degraded dongle can take seconds per cycle and is slow rather
    /// than stuck, and calling that a stall is exactly the mistake this budget exists to avoid.
    /// </remarks>
    private static readonly TimeSpan DefaultStallBudget = TimeSpan.FromSeconds(60);

    /// <summary>
    /// The usage text.
    /// </summary>
    public static string UsageText =>
        """
        HwStress - cycle a device and report what broke.

          --pattern P        restart | reopen | openclose (default reopen)
          --cycles N         cycles per run (default 100)
          --runs M           runs, each in its own process (default 5)
          --index N          device index (default 0)
          --stall-seconds S  no completed cycle for this long means stalled (default 60)
          --help             print this text

        Patterns:
          restart    open once, then start and stop a reading repeatedly
          reopen     open, read, stop and close every iteration
          openclose  open and close only, with no reading

        Exit codes: 0 every run completed, 1 a run crashed or stalled, 2 bad usage.
        Set RTLSDR_LIBRARY_PATH to cycle against a specific native build.
        """;

    /// <summary>
    /// Parse the command line.
    /// </summary>
    /// <param name="args">Command line arguments.</param>
    /// <param name="options">The parsed options, when parsing succeeded.</param>
    /// <param name="error">What was wrong, when it did not.</param>
    /// <returns>True when the command line was understood.</returns>
    public static bool TryParse(string[] args, out StressOptions options, out string? error)
    {
        options = new StressOptions(CyclePattern.Reopen, 100, 5, 0, DefaultStallBudget, false,
            false);
        error = null;

        if (Array.IndexOf(args, "--help") >= 0)
        {
            options = options with { HelpRequested = true };

            return true;
        }

        CyclePattern pattern = CyclePattern.Reopen;

        if (TryReadValue(args, "--pattern", out string? rawPattern) &&
            !Enum.TryParse(rawPattern, ignoreCase: true, out pattern))
        {
            error = $"--pattern must be restart, reopen or openclose, not '{rawPattern}'.";

            return false;
        }

        if (!TryReadPositive(args, "--cycles", 100, out int cycles, out error) ||
            !TryReadPositive(args, "--runs", 5, out int runs, out error) ||
            !TryReadPositive(args, "--stall-seconds", 60, out int stallSeconds, out error))
        {
            return false;
        }

        uint index = 0;

        if (TryReadValue(args, "--index", out string? rawIndex) &&
            !uint.TryParse(rawIndex, NumberStyles.Integer, CultureInfo.InvariantCulture,
                out index))
        {
            error = $"--index needs a device index, not '{rawIndex}'.";

            return false;
        }

        options = new StressOptions(pattern, cycles, runs, index,
            TimeSpan.FromSeconds(stallSeconds), Array.IndexOf(args, "--worker") >= 0, false);

        return true;
    }

    /// <summary>
    /// Read an option that must be a positive whole number.
    /// </summary>
    /// <param name="args">Command line arguments.</param>
    /// <param name="name">Option name, including the leading dashes.</param>
    /// <param name="fallback">Value to use when the option is absent.</param>
    /// <param name="value">The parsed value.</param>
    /// <param name="error">What was wrong, when parsing failed.</param>
    /// <returns>True when the option was absent or valid.</returns>
    private static bool TryReadPositive(string[] args, string name, int fallback, out int value,
        out string? error)
    {
        value = fallback;
        error = null;

        if (!TryReadValue(args, name, out string? raw))
        {
            return true;
        }

        if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out value) &&
            value > 0)
        {
            return true;
        }

        error = $"{name} needs a positive whole number, not '{raw}'.";

        return false;
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
