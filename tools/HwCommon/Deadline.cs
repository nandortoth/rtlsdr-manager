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
using System.Runtime.ExceptionServices;
using System.Threading;

namespace RtlSdrManager.Tools.Common;

/// <summary>
/// Bounds how long the harness will wait on a device.
/// </summary>
/// <remarks>
/// A device that stops delivering can block a read forever, because the driver's own read has
/// no timeout. Nothing here may wait indefinitely: a check that overruns has to be reported as
/// a failure so the rest of the run still happens.
/// </remarks>
public static class Deadline
{
    /// <summary>How often to re-test a condition while waiting for it.</summary>
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(20);

    /// <summary>
    /// Run an action on a background thread, waiting no longer than the deadline.
    /// </summary>
    /// <param name="action">The action to run.</param>
    /// <param name="deadline">How long to wait for it.</param>
    /// <exception cref="TimeoutException">Thrown when the action does not finish in time.</exception>
    /// <remarks>
    /// Anything the action throws is re-thrown on the calling thread with its original stack,
    /// so the reporting layer can name it. The worker is a background thread: if it never
    /// finishes it still cannot keep the process alive.
    /// </remarks>
    public static void Run(Action action, TimeSpan deadline)
    {
        Exception? failure = null;

        var worker = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        })
        {
            IsBackground = true
        };

        worker.Start();

        if (!worker.Join(deadline))
        {
            throw new TimeoutException(
                $"The device did not respond within {deadline.TotalSeconds:0} s.");
        }

        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    /// <summary>
    /// Poll a condition until it holds or the deadline passes.
    /// </summary>
    /// <param name="condition">Condition to wait for.</param>
    /// <param name="deadline">How long to wait.</param>
    /// <returns>True when the condition held in time.</returns>
    public static bool WaitUntil(Func<bool> condition, TimeSpan deadline)
    {
        var elapsed = Stopwatch.StartNew();

        while (elapsed.Elapsed < deadline)
        {
            if (condition())
            {
                return true;
            }

            Thread.Sleep(PollInterval);
        }

        return false;
    }
}
