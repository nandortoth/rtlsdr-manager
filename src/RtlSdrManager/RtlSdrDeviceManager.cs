// RTL-SDR Manager Library for .NET
// Copyright (C) 2018-2025 Nandor Toth <dev@nandortoth.com>
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
using System.Threading;
using RtlSdrManager.Exceptions;
using RtlSdrManager.Interop;

namespace RtlSdrManager;

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
    private static readonly Lazy<RtlSdrDeviceManager> Singleton = new(() => new RtlSdrDeviceManager());

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
    /// <exception cref="ArgumentNullException">Thrown when friendlyName is null.</exception>
    /// <exception cref="ArgumentException">Thrown when friendlyName is empty or whitespace, or when no device with the given friendly name exists.</exception>
    public RtlSdrManagedDevice this[string friendlyName]
    {
        get
        {
            // Validate friendlyName parameter
            if (friendlyName == null)
            {
                throw new ArgumentNullException(nameof(friendlyName), "Friendly name cannot be null.");
            }

            if (string.IsNullOrWhiteSpace(friendlyName))
            {
                throw new ArgumentException("Friendly name cannot be empty or whitespace.", nameof(friendlyName));
            }

            // Check the dictionary because of the key.
            if (!_managedDevices.TryGetValue(friendlyName, out RtlSdrManagedDevice? device))
            {
                throw new ArgumentException(
                    $"No managed device with the friendly name '{friendlyName}' exists.",
                    nameof(friendlyName));
            }

            // Return the selected device.
            return device;
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

    // Global console output suppressor (singleton pattern)
    // CRITICAL: Must be a singleton to avoid file descriptor corruption when multiple devices are open
    private static ConsoleOutputSuppressor? _globalSuppressor;
    private static readonly Lock SuppressorLock = new();

    /// <summary>
    /// Gets or sets the global console output suppression setting.
    /// Default is true (console output is suppressed).
    /// This affects messages like "Found Rafael Micro R820T tuner" and "[R82XX] PLL not locked!".
    /// IMPORTANT: Uses a global singleton suppressor to prevent file descriptor corruption
    /// when multiple devices are opened. Changing this value while devices are open is safe.
    /// </summary>
    public static bool SuppressLibraryConsoleOutput
    {
        get => _globalSuppressor != null;
        set
        {
            lock (SuppressorLock)
            {
                switch (value)
                {
                    case true when _globalSuppressor == null:
                        // Enable suppression globally
                        _globalSuppressor = new ConsoleOutputSuppressor();
                        break;
                    case false when _globalSuppressor != null:
                        // Disable suppression globally
                        _globalSuppressor.Dispose();
                        _globalSuppressor = null;
                        break;
                }
            }
        }
    }

    /// <summary>
    /// Static constructor to initialize global console output suppression.
    /// Suppression is enabled by default (v0.5.0+).
    /// </summary>
    static RtlSdrDeviceManager()
    {
        // Enable suppression by default
        SuppressLibraryConsoleOutput = true;
    }

    #endregion

    #region Methods

    /// <summary>
    /// Get fundamental information about the device.
    /// </summary>
    /// <exception cref="RtlSdrLibraryExecutionException"></exception>
    private static DeviceInfo GetDeviceInfo(uint deviceIndex)
    {
        // Create buffers, where the results will be stored.
        byte[] serialBuffer = new byte[256];
        byte[] manufacturerBuffer = new byte[256];
        byte[] productBuffer = new byte[256];

        // Get the device name.
        IntPtr nameBufferPtr = LibRtlSdr.rtlsdr_get_device_name(deviceIndex);
        string? nameBuffer = Marshal.PtrToStringUTF8(nameBufferPtr);

        // Get the other data of the device.
        int returnCode = LibRtlSdr.rtlsdr_get_device_usb_strings(deviceIndex,
            manufacturerBuffer, productBuffer, serialBuffer);
        if (returnCode != 0)
        {
            throw new RtlSdrLibraryExecutionException(
                "Problem happened during reading USB strings of the device. " +
                $"Error code: {returnCode}, device index: {deviceIndex}.");
        }

        // Convert byte arrays to strings
        string serial = Encoding.UTF8.GetString(serialBuffer).TrimEnd('\0');
        string manufacturer = Encoding.UTF8.GetString(manufacturerBuffer).TrimEnd('\0');
        string product = Encoding.UTF8.GetString(productBuffer).TrimEnd('\0');

        // If everything is good, fill the device info.
        return new DeviceInfo(deviceIndex, serial, manufacturer, product, nameBuffer ?? string.Empty);
    }

    /// <summary>
    /// Get fundamental information about all the devices on the system.
    /// </summary>
    /// <returns></returns>
    /// <exception cref="RtlSdrDeviceException"></exception>
    private static Dictionary<uint, DeviceInfo> GetAllDeviceInfo()
    {
        // Check the number of the devices on the system.
        uint deviceCount = LibRtlSdr.rtlsdr_get_device_count();

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
    /// <exception cref="ArgumentNullException">Thrown when friendlyName is null.</exception>
    /// <exception cref="ArgumentException">Thrown when friendlyName is empty or whitespace, or when a device with the given friendly name already exists.</exception>
    /// <exception cref="RtlSdrDeviceException">Thrown when the device index does not exist.</exception>
    public void OpenManagedDevice(uint index, string friendlyName)
    {
        // Validate friendlyName parameter
        if (friendlyName == null)
        {
            throw new ArgumentNullException(nameof(friendlyName), "Friendly name cannot be null.");
        }

        if (string.IsNullOrWhiteSpace(friendlyName))
        {
            throw new ArgumentException("Friendly name cannot be empty or whitespace.", nameof(friendlyName));
        }

        // Do we have device with this name?
        if (_managedDevices.ContainsKey(friendlyName))
        {
            throw new ArgumentException(
                $"A managed device with the friendly name '{friendlyName}' already exists.",
                nameof(friendlyName));
        }

        // Check if the device index exists
        if (!Devices.TryGetValue(index, out DeviceInfo device))
        {
            throw new RtlSdrDeviceException(
                $"RTL-SDR device with index {index} does not exist. " +
                $"Available device indices: {string.Join(", ", Devices.Keys)}.");
        }

        // Create a new RtlSdrManagedDevice instance.
        var managedDevice = new RtlSdrManagedDevice(device);

        // Add the device to the dictionary.
        _managedDevices.Add(friendlyName, managedDevice);
    }

    /// <summary>
    /// Close the managed device.
    /// </summary>
    /// <param name="friendlyName">Friendly name of the device.</param>
    /// <exception cref="ArgumentNullException">Thrown when friendlyName is null.</exception>
    /// <exception cref="ArgumentException">Thrown when friendlyName is empty or whitespace, or when no device with the given friendly name exists.</exception>
    public void CloseManagedDevice(string friendlyName)
    {
        // Validate friendlyName parameter
        if (friendlyName == null)
        {
            throw new ArgumentNullException(nameof(friendlyName), "Friendly name cannot be null.");
        }

        if (string.IsNullOrWhiteSpace(friendlyName))
        {
            throw new ArgumentException("Friendly name cannot be empty or whitespace.", nameof(friendlyName));
        }

        // Check the dictionary because of the key.
        if (!_managedDevices.TryGetValue(friendlyName, out RtlSdrManagedDevice? device))
        {
            throw new ArgumentException(
                $"No managed device with the friendly name '{friendlyName}' exists.",
                nameof(friendlyName));
        }

        // Dispose the managed device (which closes it).
        device.Dispose();
        _managedDevices.Remove(friendlyName);
    }

    /// <summary>
    /// Close all the managed devices.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when there are no managed devices to close.</exception>
    public void CloseAllManagedDevice()
    {
        // Check how many managed device we have.
        if (_managedDevices.Count == 0)
        {
            throw new InvalidOperationException("There are no managed (opened) RTL-SDR devices to close.");
        }

        // Close all the devices.
        string[] managedDeviceKeys = _managedDevices.Keys.ToArray();
        foreach (string key in managedDeviceKeys)
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
    public IEnumerator<RtlSdrManagedDevice> GetEnumerator() =>
        _managedDevices.Values.GetEnumerator();

    /// <summary>
    /// Implement IEnumerator interface.
    /// </summary>
    /// <returns>Enumerator.</returns>
    /// <inheritdoc />
    IEnumerator IEnumerable.GetEnumerator() =>
        GetEnumerator();

    #endregion
}
