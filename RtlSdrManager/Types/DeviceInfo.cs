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
    /// Struct for an RTL-SDR device to store its fundamental information.
    /// </summary>
    public struct DeviceInfo
    {
        #region Constructor

        /// <summary>
        /// Create new RTL-SDR device instance.
        /// </summary>
        /// <param name="index">Index of the device on the system.</param>
        /// <param name="serial">Serial number of the device.</param>
        /// <param name="manufacturer">Manufacturer of the device.</param>
        /// <param name="productType">Type of the device.</param>
        /// <param name="name">Name of the device.</param>
        internal DeviceInfo(uint index, string serial, string manufacturer, string productType, string name)
        {
            Index = index;
            Serial = serial;
            Manufacturer = manufacturer;
            ProductType = productType;
            Name = name;
        }

        #endregion

        #region Properties

        /// <summary>
        /// Get the index of the device.
        /// </summary>
        public uint Index { get; }

        /// <summary>
        /// Get the serial of the device.
        /// </summary>
        public string Serial { get; }

        /// <summary>
        /// Get the manufacturer of the device.
        /// </summary>
        public string Manufacturer { get; }

        /// <summary>
        /// Get the type of the device.
        /// </summary>
        public string ProductType { get; }

        /// <summary>
        /// Get the name of the device.
        /// </summary>
        public string Name { get; }

        #endregion
    }
}