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
    /// Struct to store RTL-SDR's crystal frequencies.
    /// </summary>
    public struct CrystalFrequency
    {
        /// <summary>
        /// Create a CrystalFrequency instance.
        /// </summary>
        /// <param name="rtl2832Frequency">Frequency value used to clock the RTL2832.</param>
        /// <param name="tunerFrequency">Frequency value used to clock the tuner IC.</param>
        public CrystalFrequency(Frequency rtl2832Frequency, Frequency tunerFrequency)
        {
            Rtl2832Frequency = rtl2832Frequency;
            TunerFrequency = tunerFrequency;
        }

        /// <summary>
        /// Frequency value used to clock the RTL2832.
        /// </summary>
        public Frequency Rtl2832Frequency { get; }

        /// <summary>
        /// Frequency value used to clock the tuner IC.
        /// </summary>
        public Frequency TunerFrequency { get; }

        /// <summary>
        /// Override ToString method.
        /// </summary>
        /// <returns>String value of the CrystalFrequency instance.</returns>
        public override string ToString()
        {
            return $"RTL2832: {Rtl2832Frequency.MHz} MHz; Tuner: {TunerFrequency.MHz} MHz";
        }
    }
}