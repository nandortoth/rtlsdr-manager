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

namespace RtlSdrManager;

/// <summary>
/// Immutable record to store I/Q data (analytic signal), representing the signal from RTL-SDR device.
/// </summary>
/// <remarks>
/// The RTL2832U delivers each component as an unsigned 8-bit sample (0-255), so the values are
/// stored internally as two <see cref="byte"/> fields (2 bytes per sample instead of 8). The
/// <see cref="I"/> and <see cref="Q"/> accessors remain <see cref="int"/> for source compatibility;
/// a value outside the 0-255 range passed to the constructor or an <c>init</c> setter is truncated
/// to a byte (device samples are always within range, so real data is unaffected).
/// </remarks>
public readonly record struct IQData
{
    /// <summary>
    /// In-Phase signal component (8-bit backing storage).
    /// </summary>
    private readonly byte _i;

    /// <summary>
    /// Quadrature signal component (8-bit backing storage).
    /// </summary>
    private readonly byte _q;

    /// <summary>
    /// Create an instance of <see cref="IQData"/> from its I/Q components.
    /// </summary>
    /// <param name="I">In-Phase signal component (0-255).</param>
    /// <param name="Q">Quadrature signal component (0-255).</param>
    public IQData(int I, int Q)
    {
        _i = (byte)I;
        _q = (byte)Q;
    }

    /// <summary>
    /// Create an instance of <see cref="IQData"/> directly from the raw device bytes.
    /// Avoids the widen/narrow round-trip on the sample-acquisition hot path.
    /// </summary>
    /// <param name="i">In-Phase signal component.</param>
    /// <param name="q">Quadrature signal component.</param>
    internal IQData(byte i, byte q)
    {
        _i = i;
        _q = q;
    }

    /// <summary>
    /// In-Phase signal component.
    /// </summary>
    public int I
    {
        get => _i;
        init => _i = (byte)value;
    }

    /// <summary>
    /// Quadrature signal component.
    /// </summary>
    public int Q
    {
        get => _q;
        init => _q = (byte)value;
    }

    /// <summary>
    /// Deconstruct the I/Q data into its components.
    /// </summary>
    /// <param name="I">In-Phase signal component.</param>
    /// <param name="Q">Quadrature signal component.</param>
    public void Deconstruct(out int I, out int Q)
    {
        I = _i;
        Q = _q;
    }

    /// <summary>
    /// Returns a string representation of the I/Q data.
    /// </summary>
    /// <returns>String value of the I/Q data instance.</returns>
    public override string ToString() => $"I: {I,3}, Q: {Q,3}";
}
