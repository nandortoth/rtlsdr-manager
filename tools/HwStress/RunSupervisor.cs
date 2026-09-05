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
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;

namespace RtlSdrManager.Tools.HwStress;

/// <summary>
/// Runs cycles in a child process and classifies how each run ended.
/// </summary>
/// <remarks>
/// The split exists because the fault being provoked terminates the process. Counting from
/// inside the process that dies would lose the count, so the work happens in a child and this
/// half survives to report it.
/// <para>
/// Stalling is judged from <em>progress</em>, not from total time. A run that completes cycles
/// slowly is slow, and a run that completes none is stuck; a fixed overall budget cannot tell
/// them apart, and mistaking one for the other has cost real time here. The budget therefore
/// resets on every completed cycle.
/// </para>
/// </remarks>
internal sealed class RunSupervisor
{
    /// <summary>
    /// What to run and how.
    /// </summary>
    private readonly StressOptions _options;

    /// <summary>
    /// Create a supervisor.
    /// </summary>
    /// <param name="options">What to run and how.</param>
    public RunSupervisor(StressOptions options)
    {
        _options = options;
    }

    /// <summary>
    /// Run one child and classify it.
    /// </summary>
    /// <returns>What the run did.</returns>
    public RunOutcome RunOnce()
    {
        using var child = new Process();
        child.StartInfo = BuildStartInfo();

        int completed = 0;
        long lastProgressTicks = DateTime.UtcNow.Ticks;
        string? fault = null;

        child.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null)
            {
                return;
            }

            if (e.Data.StartsWith(CycleWorker.FaultMarker, StringComparison.Ordinal))
            {
                fault = e.Data[CycleWorker.FaultMarker.Length..];

                return;
            }

            if (!e.Data.Contains(CycleWorker.CycleMarker, StringComparison.Ordinal))
            {
                return;
            }

            Interlocked.Increment(ref completed);
            Interlocked.Exchange(ref lastProgressTicks, DateTime.UtcNow.Ticks);
        };

        var elapsed = Stopwatch.StartNew();

        child.Start();
        child.BeginErrorReadLine();

        while (!child.WaitForExit(250))
        {
            TimeSpan sinceProgress = DateTime.UtcNow -
                                     new DateTime(Interlocked.Read(ref lastProgressTicks),
                                         DateTimeKind.Utc);

            if (sinceProgress < _options.StallBudget)
            {
                continue;
            }

            KillQuietly(child);
            elapsed.Stop();

            return new RunOutcome(RunStatus.Stalled, Volatile.Read(ref completed),
                elapsed.Elapsed, null);
        }

        // Lets the reader drain what the child wrote just before exiting, so a cycle finished
        // on the way out is still counted.
        child.WaitForExit();
        elapsed.Stop();

        RunStatus status = child.ExitCode == 0 ? RunStatus.Ok : RunStatus.Crashed;

        return new RunOutcome(status, Volatile.Read(ref completed), elapsed.Elapsed,
            child.ExitCode, fault);
    }

    /// <summary>
    /// Build the command that runs the cycles.
    /// </summary>
    /// <returns>How to start the child.</returns>
    /// <remarks>
    /// The child is this same program in worker mode. When the tool runs from its own
    /// executable that is simply the executable; when it runs through the shared host, the
    /// assembly path has to be passed as the first argument, so both are handled rather than
    /// assuming either.
    /// </remarks>
    private ProcessStartInfo BuildStartInfo()
    {
        string host = Environment.ProcessPath ??
                      throw new InvalidOperationException("The running program has no path.");

        var startInfo = new ProcessStartInfo(host)
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false
        };

        if (Path.GetFileNameWithoutExtension(host)
            .Equals("dotnet", StringComparison.OrdinalIgnoreCase))
        {
            startInfo.ArgumentList.Add(Assembly.GetEntryAssembly()!.Location);
        }

        startInfo.ArgumentList.Add("--worker");
        startInfo.ArgumentList.Add("--pattern");
        startInfo.ArgumentList.Add(_options.Pattern.ToString().ToLowerInvariant());
        startInfo.ArgumentList.Add("--cycles");
        startInfo.ArgumentList.Add(_options.Cycles.ToString());
        startInfo.ArgumentList.Add("--index");
        startInfo.ArgumentList.Add(_options.Index.ToString());

        return startInfo;
    }

    /// <summary>
    /// Kill a stalled child without letting the kill itself throw.
    /// </summary>
    /// <param name="child">The process to kill.</param>
    private static void KillQuietly(Process child)
    {
        try
        {
            child.Kill(entireProcessTree: true);
            child.WaitForExit(5000);
        }
        catch (Exception)
        {
            // A process that has already gone is the outcome that was wanted anyway.
        }
    }
}
