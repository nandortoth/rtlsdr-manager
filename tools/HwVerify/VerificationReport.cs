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

namespace RtlSdrManager.Tools.HwVerify;

/// <summary>
/// Runs the individual hardware checks, prints their outcome, and tallies the results.
/// </summary>
/// <remarks>
/// A check reports one of three outcomes. <c>PASS</c> and <c>FAIL</c> are the obvious ones;
/// <c>SKIP</c> exists because the harness runs against whatever dongle happens to be
/// attached, and some behavior can only be proven on a particular tuner. Recording the skip
/// with its reason keeps a partial run from being mistaken for a complete one.
/// </remarks>
internal sealed class VerificationReport
{
    /// <summary>
    /// Number of checks that passed.
    /// </summary>
    public int Passed { get; private set; }

    /// <summary>
    /// Number of checks that failed, either by returning false or by throwing.
    /// </summary>
    public int Failed { get; private set; }

    /// <summary>
    /// Number of checks that could not be run against the attached hardware.
    /// </summary>
    public int Skipped { get; private set; }

    /// <summary>
    /// True when at least one check failed, which the process exit code reflects.
    /// </summary>
    public bool HasFailures => Failed > 0;

    /// <summary>
    /// Run a single check and record its outcome.
    /// </summary>
    /// <param name="name">What the check asserts, phrased as a statement of expected behavior.</param>
    /// <param name="probe">The check itself. Returns true when the behavior is correct.</param>
    /// <param name="expectation">
    /// What should have happened, printed only on failure. Where the behavior changed in a
    /// release, say what it used to be: a failure is then immediately legible as a regression
    /// rather than as an unexplained mismatch.
    /// </param>
    public void Check(string name, Func<bool> probe, string expectation)
    {
        try
        {
            if (probe())
            {
                Console.WriteLine($"  PASS  {name}");
                Passed++;
                return;
            }

            Console.WriteLine($"  FAIL  {name}");
            Console.WriteLine($"        expected: {expectation}");
            Failed++;
        }
        catch (Exception ex)
        {
            // An unexpected exception is a failure, not a crash: the remaining checks still
            // carry information, and the run should end with a complete report. The type and
            // message are printed because they are usually the whole diagnosis.
            Console.WriteLine($"  FAIL  {name}");
            Console.WriteLine($"        expected: {expectation}");
            Console.WriteLine($"        threw:    {ex.GetType().Name}: {ex.Message}");
            Failed++;
        }
    }

    /// <summary>
    /// Record a check that the attached hardware cannot exercise.
    /// </summary>
    /// <param name="name">What the check would have asserted.</param>
    /// <param name="reason">
    /// Why it could not run, and what hardware would be needed. Without this the reader
    /// cannot tell which gaps in a green run still matter.
    /// </param>
    public void Skip(string name, string reason)
    {
        Console.WriteLine($"  SKIP  {name}");
        Console.WriteLine($"        {reason}");
        Skipped++;
    }

    /// <summary>
    /// Record a failure that has no probe to run.
    /// </summary>
    /// <param name="name">What could not be established, phrased like any other check.</param>
    /// <param name="reason">Why it could not be established.</param>
    /// <remarks>
    /// <see cref="Check"/> covers a check that ran and gave the wrong answer. This covers the
    /// other case: a group that could not run at all, usually because the device stopped being
    /// openable partway through a run. That has to count as a failure rather than a note,
    /// because the alternative is an incomplete run that exits zero and reads as a clean one.
    /// </remarks>
    public void Fail(string name, string reason)
    {
        Console.WriteLine($"  FAIL  {name}");
        Console.WriteLine($"        {reason}");
        Failed++;
    }

    /// <summary>
    /// Print the totals line that closes a run.
    /// </summary>
    public void PrintSummary() =>
        Console.WriteLine($"passed={Passed} failed={Failed} skipped={Skipped}");

    /// <summary>
    /// Determine whether an action throws the expected exception type.
    /// </summary>
    /// <typeparam name="TException">The exception type the action is expected to throw.</typeparam>
    /// <param name="action">The action to run.</param>
    /// <returns>True when the expected exception was thrown.</returns>
    /// <remarks>
    /// Any other exception type propagates to <see cref="Check"/>, which reports it. That is
    /// deliberate: catching the wrong type here would hide exactly the mismatch the check
    /// exists to detect, because several fixes in this library changed which type is thrown.
    /// </remarks>
    public static bool Throws<TException>(Action action) where TException : Exception
    {
        try
        {
            action();
            return false;
        }
        catch (TException)
        {
            return true;
        }
    }
}
