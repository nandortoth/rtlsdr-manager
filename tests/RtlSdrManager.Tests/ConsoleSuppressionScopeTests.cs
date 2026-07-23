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

using Xunit;

namespace RtlSdrManager.Tests;

/// <summary>
/// Unit tests for the console output suppression scope reference counting.
/// The tests manipulate process-global state (the suppression flag and, while a scope
/// with the flag enabled is active, the real stdout/stderr file descriptors), so the
/// collection disables parallelization.
/// </summary>
[Collection(nameof(ConsoleSuppressionScopeTests))]
[CollectionDefinition(nameof(ConsoleSuppressionScopeTests), DisableParallelization = true)]
public class ConsoleSuppressionScopeTests
{
    [Fact]
    public void ScopeWithFlagDisabled_DoesNotCount()
    {
        RtlSdrDeviceManager.SuppressLibraryConsoleOutput = false;
        try
        {
            using (new RtlSdrDeviceManager.SuppressionScope())
            {
                Assert.Equal(0, RtlSdrDeviceManager.ActiveSuppressionScopeCount);
            }

            Assert.Equal(0, RtlSdrDeviceManager.ActiveSuppressionScopeCount);
        }
        finally
        {
            RtlSdrDeviceManager.SuppressLibraryConsoleOutput = false;
        }
    }

    [Fact]
    public void ScopeWithFlagEnabled_CountsAndReleases()
    {
        RtlSdrDeviceManager.SuppressLibraryConsoleOutput = true;
        try
        {
            using (new RtlSdrDeviceManager.SuppressionScope())
            {
                Assert.Equal(1, RtlSdrDeviceManager.ActiveSuppressionScopeCount);

                using (new RtlSdrDeviceManager.SuppressionScope())
                {
                    Assert.Equal(2, RtlSdrDeviceManager.ActiveSuppressionScopeCount);
                }

                Assert.Equal(1, RtlSdrDeviceManager.ActiveSuppressionScopeCount);
            }

            Assert.Equal(0, RtlSdrDeviceManager.ActiveSuppressionScopeCount);
        }
        finally
        {
            RtlSdrDeviceManager.SuppressLibraryConsoleOutput = false;
        }
    }

    [Fact]
    public void DisablingFlagWhileScopeActive_StillReleasesScope()
    {
        // Regression test: previously the exit path consulted the flag, so disabling
        // the flag while a scope was active leaked the suppression (stdout stayed
        // redirected to the null device for the rest of the process).
        RtlSdrDeviceManager.SuppressLibraryConsoleOutput = true;
        try
        {
            var scope = new RtlSdrDeviceManager.SuppressionScope();
            Assert.Equal(1, RtlSdrDeviceManager.ActiveSuppressionScopeCount);

            RtlSdrDeviceManager.SuppressLibraryConsoleOutput = false;
            scope.Dispose();

            Assert.Equal(0, RtlSdrDeviceManager.ActiveSuppressionScopeCount);
        }
        finally
        {
            RtlSdrDeviceManager.SuppressLibraryConsoleOutput = false;
        }
    }

    [Fact]
    public void EnablingFlagWhileScopeActive_DoesNotCorruptTheCount()
    {
        // Regression test: previously the exit path consulted the flag, so enabling
        // the flag while a no-op scope was active made the count go negative,
        // corrupting all subsequent scopes.
        RtlSdrDeviceManager.SuppressLibraryConsoleOutput = false;
        try
        {
            var scope = new RtlSdrDeviceManager.SuppressionScope();
            Assert.Equal(0, RtlSdrDeviceManager.ActiveSuppressionScopeCount);

            RtlSdrDeviceManager.SuppressLibraryConsoleOutput = true;
            scope.Dispose();

            Assert.Equal(0, RtlSdrDeviceManager.ActiveSuppressionScopeCount);
        }
        finally
        {
            RtlSdrDeviceManager.SuppressLibraryConsoleOutput = false;
        }
    }
}
