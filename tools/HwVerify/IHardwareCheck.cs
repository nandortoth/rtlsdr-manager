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

namespace RtlSdrManager.Tools.HwVerify;

/// <summary>
/// One group of related checks against an open device.
/// </summary>
/// <remarks>
/// Implementations are registered in the list in <c>Program.Main</c>, which prints the title,
/// runs the check, and separates it from the next one. Adding a group of checks means adding
/// a class here and a line to that list.
/// <para>
/// A check may change device state, but must put back whatever it changes, on the failure
/// path as well. The harness is expected to leave the device as it found it.
/// </para>
/// </remarks>
internal interface IHardwareCheck
{
    /// <summary>
    /// Heading printed above this group's results.
    /// </summary>
    string Title { get; }

    /// <summary>
    /// Run the checks against the device under test.
    /// </summary>
    /// <param name="device">The device under test, opened by the caller.</param>
    /// <param name="report">Report collecting the outcomes.</param>
    void Run(RtlSdrManagedDevice device, VerificationReport report);
}
