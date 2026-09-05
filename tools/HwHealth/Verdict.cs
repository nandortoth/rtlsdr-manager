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

namespace RtlSdrManager.Tools.HwHealth;

/// <summary>
/// What a probe concluded about a device.
/// </summary>
/// <remarks>
/// Kept separate from the process exit codes on purpose: the verdict is what was observed,
/// the exit code is how that is reported to a shell, and only <see cref="Program"/> needs to
/// know the second.
/// </remarks>
internal enum Verdict
{
    /// <summary>
    /// The device sustained delivery for the whole measurement window.
    /// </summary>
    Healthy,

    /// <summary>
    /// The device is attached but did not sustain delivery.
    /// </summary>
    Unhealthy,

    /// <summary>
    /// No device was attached, so nothing could be measured. Inconclusive rather than bad.
    /// </summary>
    NoDevice
}
