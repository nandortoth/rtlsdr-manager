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
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using RtlSdrManager.Exceptions;
using RtlSdrManager.Types;

namespace RtlSdrManager
{
    /// <summary>
    /// Class for managing RTL-SDR devices.
    /// The class uses the "librtlsdr" shared library.
    /// </summary>
    /// <inheritdoc />
    public class RtlSdrDeviceManager : IEnumerable<RtlSdrManagedDevice>
    {
        #region Fields

        /// <summary>
        /// Lazy singleton pattern.
        /// </summary>
        private static readonly Lazy<RtlSdrDeviceManager> Singleton =
            new Lazy<RtlSdrDeviceManager>(() => new RtlSdrDeviceManager());
        
        /// <summary>
        /// Dictionary for the managed (opened) RTL-SDR devices.
        /// </summary>
        private readonly Dictionary<string, RtlSdrManagedDevice> _managedDevices;
        
        #endregion

        #region Constructor and Indexer

        /// <summary>
        /// Create a new RtlSdrDeviceManager instance for RTL-SDR devices.
        /// </summary>
        private RtlSdrDeviceManager()
        {
            // Initialize the dictionary for the managed devices.
            _managedDevices = new Dictionary<string, RtlSdrManagedDevice>();
            
            // Initialize the list for the devices on the system, and fill it up.
            Devices = new Dictionary<uint, DeviceInfo>();
            Devices = GetAllDeviceInfo();
        }

        /// <summary>
        /// Indexer to return the managed (opened) RTL-SDR device which has the given friendly name.
        /// </summary>
        /// <param name="friendlyName">Friendly name of the RTL-SDR device.</param>
        /// <exception cref="IndexOutOfRangeException"></exception>
        public RtlSdrManagedDevice this[string friendlyName]
        {
            get
            {
                // Check the dictionary because of the key.
                if (!_managedDevices.ContainsKey(friendlyName))
                {
                    throw new IndexOutOfRangeException("There is no managed (opened) RTL-SDR device with " +
                                                       $"the given friendly name ({friendlyName}.");
                }

                // Return the selected device.
                return _managedDevices[friendlyName];
            }
        }

        #endregion

        #region Properties
        
        /// <summary>
        /// Return the instance (singleton pattern)
        /// </summary>
        public static RtlSdrDeviceManager Instance => Singleton.Value;

        /// <summary>
        /// Return the amount of the managed devices.
        /// </summary>
        public int CountManagedDevices => _managedDevices.Count;

        /// <summary>
        /// Return the amount of the supported RTL-SDR devices on the system.
        /// </summary>
        public int CountDevices => Devices.Count;

        /// <summary>
        /// Return the basic data of the supported RTL-SDR devices on the system.
        /// </summary>
        public Dictionary<uint, DeviceInfo> Devices { get; }

        #endregion

        #region Methods
        
        /// <summary>
        /// Get fundamental information about the device.
        /// </summary>
        /// <exception cref="RtlSdrLibraryExecutionException"></exception>
        private static DeviceInfo GetDeviceInfo(uint deviceIndex)
        {
            // Create buffers, where the results will be stored.
            var serialBuffer = new StringBuilder(256);
            var manufacturerBuffer = new StringBuilder(256);
            var productBuffer = new StringBuilder(256);

            // Get the device name.  
            var nameBufferPtr = RtlSdrLibraryWrapper.rtlsdr_get_device_name(deviceIndex);
            var nameBuffer = Marshal.PtrToStringUTF8(nameBufferPtr);

            // Get the other data of the device.
            var returnCode = RtlSdrLibraryWrapper.rtlsdr_get_device_usb_strings(deviceIndex,
                manufacturerBuffer, productBuffer, serialBuffer);
            if (returnCode != 0)
            {
                throw new RtlSdrLibraryExecutionException(
                    "Problem happened during reading USB strings of the device. " +
                    $"Error code: {returnCode}, device index: {deviceIndex}.");
            }

            // If everything is good, fill the device info.
            return new DeviceInfo(deviceIndex, serialBuffer.ToString(), manufacturerBuffer.ToString(),
                productBuffer.ToString(), nameBuffer);
        }

        /// <summary>
        /// Get fundamental information about all the devices on the system.
        /// </summary>
        /// <returns></returns>
        /// <exception cref="RtlSdrDeviceException"></exception>
        private static Dictionary<uint, DeviceInfo> GetAllDeviceInfo()
        {
            // Check the number of the devices on the system.
            var deviceCount = RtlSdrLibraryWrapper.rtlsdr_get_device_count();

            // If there is no device on the system, throw an exception.
            if (deviceCount == 0)
            {
                throw new RtlSdrDeviceException("There is no supported RTL-SDR device on the system.");
            }

            // Create the list, which will contain the devices.
            var devices = new Dictionary<uint, DeviceInfo>();

            // Iterate the devices.
            for (uint i = 0; i < deviceCount; i++)
            {
                // If everything is good, add the device to the list.
                devices.Add(i, GetDeviceInfo(i));
            }

            // Return the list.
            return devices;
        }
        
        /// <summary>
        /// Open RTL-SDR device for further usage.
        /// </summary>
        /// <param name="index">Index of the device.</param>
        /// <param name="friendlyName">Friendly name of the device, later this can be used as a reference.</param>
        /// <returns>RtlSdrManagedDevice</returns>
        /// <exception cref="IndexOutOfRangeException"></exception>
        public RtlSdrManagedDevice OpenManagedDevice(uint index, string friendlyName)
        {
            // Do we have device with this name?
            if (_managedDevices.ContainsKey(friendlyName))
            {
                throw new IndexOutOfRangeException("There is a managed (opened) RTL-SDR device with " +
                                                   $"the given friendly name ({friendlyName}).");
            }

            // Create a new RtlSdrManagedDevice instance.
            var managedDevice = new RtlSdrManagedDevice(Devices[index]);

            // Add the device to the dictionary.
            _managedDevices.Add(friendlyName, managedDevice);

            return managedDevice;
        }

        /// <summary>
        /// Close the managed device.
        /// </summary>
        /// <param name="friendlyName">Friendly name of the device.</param>
        /// <exception cref="IndexOutOfRangeException"></exception>
        public void CloseManagedDevice(string friendlyName)
        {
            // Check the dictionary because of the key.
            if (!_managedDevices.ContainsKey(friendlyName))
            {
                throw new IndexOutOfRangeException("There is no managed (opened) RTL-SDR device with " +
                                                   $"the given friendly name ({friendlyName}).");
            }

            // Close the managed device.
            _managedDevices[friendlyName].Close();
            _managedDevices.Remove(friendlyName);
        }

        /// <summary>
        /// Close all the managed devices.
        /// </summary>
        /// <exception cref="IndexOutOfRangeException"></exception>
        public void CloseAllManagedDevice()
        {
            // Check how many managed device we have.
            if (_managedDevices.Count == 0)
            {
                throw new IndexOutOfRangeException("There is no managed (opened) RTL-SDR device.");
            }

            // Close all the devices.
            var managedDeviceKeys = _managedDevices.Keys.ToArray();
            foreach (var key in managedDeviceKeys)
            {
                CloseManagedDevice(key);
            }
        }

        #endregion

        #region Implement IEnumerable

        /// <summary>
        /// Implement IEnumerator interface.
        /// </summary>
        /// <returns>List of managed devices.</returns>
        /// <inheritdoc />
        public IEnumerator<RtlSdrManagedDevice> GetEnumerator()
        {
            return _managedDevices.Values.GetEnumerator();
        }

        /// <summary>
        /// Implement IEnumerator interface.
        /// </summary>
        /// <returns>Enumerator.</returns>
        /// <inheritdoc />
        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        #endregion
    }
}