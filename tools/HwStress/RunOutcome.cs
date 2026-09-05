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

namespace RtlSdrManager.Tools.HwStress;

/// <summary>
/// How a single run ended.
/// </summary>
/// <remarks>
/// <see cref="Stalled"/> is deliberately not folded into a failure or a timeout. A run that
/// stops making progress and a run that dies are different faults with different causes, and
/// treating a slow run as a hung one, or a hung one as merely slow, has produced wrong
/// conclusions here more than once.
/// </remarks>
internal enum RunStatus
{
    /// <summary>
    /// Every cycle completed and the process exited cleanly.
    /// </summary>
    Ok,

    /// <summary>
    /// The process died partway through.
    /// </summary>
    Crashed,

    /// <summary>
    /// The process stopped completing cycles while still alive.
    /// </summary>
    Stalled
}

/// <summary>
/// What one run of a pattern did.
/// </summary>
/// <param name="Status">How it ended.</param>
/// <param name="CompletedCycles">Cycles finished before it ended.</param>
/// <param name="Elapsed">How long it ran.</param>
/// <param name="ExitCode">Process exit code, or null when it was killed for stalling.</param>
internal sealed record RunOutcome(
    RunStatus Status,
    int CompletedCycles,
    TimeSpan Elapsed,
    int? ExitCode,
    string? Reason = null)
{
    /// <summary>
    /// Render the run as one line.
    /// </summary>
    /// <param name="number">Which run this was.</param>
    /// <returns>The line to print.</returns>
    public string Format(int number)
    {
        string head = $"    run {number}: {Status.ToString().ToUpperInvariant(),-8} " +
                      $"{CompletedCycles,5} cycles {Elapsed.TotalSeconds,7:0.0}s";

        return Status switch
        {
            RunStatus.Crashed when Reason is not null => $"{head} {Reason}",
            RunStatus.Crashed => $"{head} rc={ExitCode} (no managed frames; the native layer took the process)",
            RunStatus.Stalled => $"{head} no cycle completed before the stall budget expired",
            _ => head
        };
    }
}
