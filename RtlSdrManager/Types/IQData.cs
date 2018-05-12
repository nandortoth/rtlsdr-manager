// RTL-SDR Manager Library for .NET Core
// Copyright (C) 2018 Nandor Toth <dev@nandortoth.eu>
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

namespace RtlSdrManager.Types
{
    /// <summary>
    /// Struct to store I/Q data (analytic signal), representing the signal from RTL-SDR device.
    /// </summary>
    public struct IQData
    {
        /// <summary>
        /// Create new I/Q data instance.
        /// </summary>
        /// <param name="i">In-Phase signal component.</param>
        /// <param name="q">Quadrature signal component.</param>
        internal IQData(int i, int q)
        {
            I = i;
            Q = q;
        }

        /// <summary>
        /// In-Phase signal component of I/Q data.
        /// </summary>
        public int I { get; }

        /// <summary>
        /// Quadrature signal component of I/Q data.
        /// </summary>
        public int Q { get; }

        /// <summary>
        /// Override ToString method.
        /// </summary>
        /// <returns>String value of the I/Q data instance.</returns>
        public override string ToString()
        {
            return $"I: {I,3}, Q: {Q,3}";
        }
    }
}