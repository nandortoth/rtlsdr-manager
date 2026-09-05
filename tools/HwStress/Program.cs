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
/// Cycles a device through a chosen lifecycle and reports what broke.
/// </summary>
/// <remarks>
/// The verification harness is a regression gate, not a crash detector: it performs few cycles,
/// so a fault that fires with low probability passes it comfortably. Showing that a crash is
/// present, or gone, needs enough cycles to make a low-probability fault near-certain, and that
/// is what this tool is for.
/// <para>
/// It runs each set of cycles in a child process, because the faults worth measuring kill the
/// process outright. Each run is classified as completed, crashed, or stalled; the three are
/// different findings and are never collapsed into one.
/// </para>
/// <para>
/// Point <c>RTLSDR_LIBRARY_PATH</c> at a native build to cycle against it. Comparing two builds
/// means alternating them in one device state with a control included, which
/// <c>tools/test-compare.sh</c> does; a single run of one build establishes nothing.
/// </para>
/// </remarks>
internal static class Program
{
    /// <summary>
    /// Exit code reported when every run completed.
    /// </summary>
    private const int ExitOk = 0;

    /// <summary>
    /// Exit code reported when at least one run crashed or stalled.
    /// </summary>
    private const int ExitFailure = 1;

    /// <summary>
    /// Exit code reported when the command line made no sense.
    /// </summary>
    private const int ExitUsage = 2;

    /// <summary>
    /// Entry point. Supervises runs, or performs one when asked to be the worker.
    /// </summary>
    /// <param name="args">Command line arguments; see <see cref="StressOptions.UsageText"/>.</param>
    /// <returns>
    /// <see cref="ExitOk"/>, <see cref="ExitFailure"/>, or <see cref="ExitUsage"/>.
    /// </returns>
    public static int Main(string[] args)
    {
        if (!StressOptions.TryParse(args, out StressOptions options, out string? error))
        {
            Console.WriteLine(error);
            Console.WriteLine();
            Console.WriteLine(StressOptions.UsageText);

            return ExitUsage;
        }

        if (options.HelpRequested)
        {
            Console.WriteLine(StressOptions.UsageText);

            return ExitOk;
        }

        return options.IsWorker
            ? CycleWorker.Run(options.Pattern, options.Cycles, options.Index)
            : Supervise(options);
    }

    /// <summary>
    /// Run every requested run and summarize them.
    /// </summary>
    /// <param name="options">What to run and how.</param>
    /// <returns><see cref="ExitOk"/> or <see cref="ExitFailure"/>.</returns>
    private static int Supervise(StressOptions options)
    {
        Console.WriteLine($"HwStress: pattern={options.Pattern.ToString().ToLowerInvariant()} " +
                          $"cycles={options.Cycles} runs={options.Runs} " +
                          $"stall-budget={options.StallBudget.TotalSeconds:0}s");

        var supervisor = new RunSupervisor(options);
        int ok = 0, crashed = 0, stalled = 0, completedCycles = 0;

        for (int run = 1; run <= options.Runs; run++)
        {
            RunOutcome outcome = supervisor.RunOnce();
            completedCycles += outcome.CompletedCycles;

            switch (outcome.Status)
            {
                case RunStatus.Ok:
                    ok++;
                    break;

                case RunStatus.Crashed:
                    crashed++;
                    break;

                default:
                    stalled++;
                    break;
            }

            Console.WriteLine(outcome.Format(run));
        }

        Console.WriteLine($"  => ok={ok} crashed={crashed} stalled={stalled} " +
                          $"of {options.Runs} runs, {completedCycles} cycles completed");

        return crashed + stalled > 0 ? ExitFailure : ExitOk;
    }
}
