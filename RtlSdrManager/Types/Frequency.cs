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

using System;

namespace RtlSdrManager.Types
{
    /// <summary>
    /// Class to handle frequencies in Hertz.
    /// </summary>
    public class Frequency
    {
        /// <summary>
        /// Create a frequency instance.
        /// </summary>
        /// <param name="hz"></param>
        public Frequency(uint hz)
        {
            Hz = hz;
        }

        /// <summary>
        /// Create a frequency instance, the default value is 0 Hz.
        /// </summary>
        /// <inheritdoc />
        public Frequency() : this(0)
        {
        }

        /// <summary>
        /// Property for handling frequenczy in Hz.
        /// </summary>
        public uint Hz { get; set; }

        /// <summary>
        /// Property for handling frequenczy in KHz.
        /// </summary>
        public double KHz
        {
            get => (double) Hz / 1000;
            set
            {
                if (value < 0 || value > uint.MaxValue)
                {
                    throw new ArgumentOutOfRangeException(
                        "Problem happened during setting frequency. " +
                        $"Wrong frequency was given: {value}.");
                }

                Hz = (uint) (value * 1000);
            }
        }

        /// <summary>
        /// Property for handling frequenczy in MHz.
        /// </summary>
        public double MHz
        {
            get => (double) Hz / 1000000;
            set
            {
                if (value < 0 || value > uint.MaxValue)
                {
                    throw new ArgumentOutOfRangeException(
                        "Problem happened during setting frequency. " +
                        $"Wrong frequency was given: {value}.");
                }

                Hz = (uint) (value * 1000000);
            }
        }

        /// <summary>
        /// Property for handling frequenczy in GHz.
        /// </summary>
        public double GHz
        {
            get => (double) Hz / 1000000000;
            set
            {
                if (value < 0 || value > uint.MaxValue)
                {
                    throw new ArgumentOutOfRangeException(
                        "Problem happened during setting frequency. " +
                        $"Wrong frequency was given: {value}.");
                }

                Hz = (uint) (value * 1000000000);
            }
        }

        /// <summary>
        /// Override ToString method.
        /// </summary>
        /// <returns>String value of the Frequency instance.</returns>
        public override string ToString()
        {
            return string.Format($"{Hz} Hz; {KHz} KHz; {MHz} MHz; {GHz} GHz");
        }
    }
}