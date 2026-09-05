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

namespace RtlSdrManager.Tools.HwStress;

/// <summary>
/// The device lifecycles worth cycling, each of which has told us something different.
/// </summary>
internal enum CyclePattern
{
    /// <summary>
    /// Open once, then start and stop a reading repeatedly on the same instance.
    /// </summary>
    /// <remarks>
    /// The shape a consumer reaches for when scanning by restarting rather than retuning.
    /// </remarks>
    Restart,

    /// <summary>
    /// Open, read, stop and close on every iteration.
    /// </summary>
    /// <remarks>
    /// The ordinary device lifecycle, and the one that fails fastest. Also, what drives a
    /// dongle into the degraded state where it opens but will not sustain a stream.
    /// </remarks>
    Reopen,

    /// <summary>
    /// Open and close, with no reading at all.
    /// </summary>
    /// <remarks>
    /// The control. It is clean where the other two are not, which is what established that a
    /// reading is required rather than the open and close churn being at fault. A pattern that
    /// proves the absence of a cause earns its place as much as one that reproduces it.
    /// </remarks>
    OpenClose
}
