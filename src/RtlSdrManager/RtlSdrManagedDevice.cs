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

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using RtlSdrManager.Exceptions;
using RtlSdrManager.Hardware;
using RtlSdrManager.Interop;
using RtlSdrManager.Modes;

namespace RtlSdrManager;

/// <summary>
/// Class for a managed (opened) RTL-SDR device.
/// </summary>
/// <inheritdoc />
public sealed partial class RtlSdrManagedDevice : IDisposable
{
    #region Fields

    /// <summary>
    /// Device Handle, used by RTL-SDR wrapper library.
    /// </summary>
    private readonly SafeRtlSdrHandle? _deviceHandle;

    /// <summary>
    /// Gain mode of the tuner.
    /// </summary>
    private TunerGainModes _deviceTunerGainMode;

    /// <summary>
    /// AGC mode of the RTL-SDR device.
    /// </summary>
    private AGCModes _deviceAGCMode;

    /// <summary>
    /// Test mode of the RTL-SDR device.
    /// </summary>
    private TestModes _deviceTestMode;

    /// <summary>
    /// Frequency dithering of the RTL-SDR device.
    /// </summary>
    private readonly FrequencyDitheringModes _frequencyDitheringMode;

    /// <summary>
    /// Tuner bandwidth selection mode of the RTL-SDR device.
    /// </summary>
    private TunerBandwidthSelectionModes _tunerBandwidthSelectionMode;

    /// <summary>
    /// Tuner bandwidth of the RTL-SDR device.
    /// </summary>
    private Frequency _tunerBandwidth;

    /// <summary>
    /// Enablement of KerberosSDR functionalities.
    /// </summary>
    private KerberosSDRModes _kerberosSDRMode;

    /// <summary>
    /// Device context for async read.
    /// Allocated by StartReadSamplesAsync and released by StopReadSamplesAsync:
    /// it intentionally roots the device only while the native callback may use it.
    /// A permanent handle would prevent the device from ever being finalized.
    /// </summary>
    private GCHandle _deviceContext;

    /// <summary>
    /// Cached supported tuner gains from librtlsdr (raw int values).
    /// </summary>
    private int[]? _supportedTunerGainsCache;

    /// <summary>
    /// Last tuner gain (in tenths of a dB) successfully written to the device, or null when
    /// no manual gain has been set in this session. librtlsdr returns 0 from
    /// rtlsdr_get_tuner_gain both for "never set" and for a legitimate 0.0 dB gain, so the
    /// return value alone cannot tell them apart; this field does.
    /// </summary>
    private int? _lastSetTunerGain;

    /// <summary>
    /// Private field to implement IDispose interface.
    /// </summary>
    private bool _disposed;

    #endregion

    #region Helper Methods

    /// <summary>
    /// Executes an action with scoped console output suppression.
    /// Uses reference-counted global suppressor to prevent file descriptor corruption
    /// when multiple devices are being configured simultaneously.
    /// </summary>
    /// <param name="action">The action to execute.</param>
    private void ExecuteWithSuppression(Action action)
    {
        using var scope = new RtlSdrDeviceManager.SuppressionScope();
        action();
    }

    #endregion

    #region Constructor and DeviceInfo

    /// <summary>
    /// Create an instance of managed RTL-SDR device.
    /// </summary>
    /// <param name="deviceInfo">Fundamental information of the device.</param>
    internal RtlSdrManagedDevice(DeviceInfo deviceInfo)
    {
        // Console output suppression is handled by RtlSdrDeviceManager.OpenManagedDevice()
        // using scoped suppression with reference counting (v0.5.2+).
        // This constructor is called within that suppression scope.

        // Store the index number of the device.
        uint deviceIndex = deviceInfo.Index;

        // Open the device and get a safe handle.
        // OpenDevice will throw an exception if the device cannot be opened.
        _deviceHandle = LibRtlSdr.OpenDevice(deviceIndex);

        // Set the test mode to disable.
        // Private variable is used for initialization, because this function is
        // supported only by KerberosSDR devices.
        _frequencyDitheringMode = FrequencyDitheringModes.NotSet;

        // Enablement of KerberosSDR functionalities.
        // The initialization is necessary, to be sure that it will happen once.
        KerberosSDRMode = KerberosSDRModes.Disabled;

        // Set the tuner gain mode to automatic.
        // The initialization is necessary, to be sure that it will happen once.
        TunerGainMode = TunerGainModes.AGC;

        // Set the AGC mode to disable.
        // The initialization is necessary, to be sure that it will happen once.
        AGCMode = AGCModes.Disabled;

        // Set the test mode to disable.
        // The initialization is necessary, to be sure that it will happen once.
        TestMode = TestModes.Disabled;

        // Set the bandwidth selection mode to automatic.
        // The initialization is necessary, to be sure that it will happen once.
        TunerBandwidthSelectionMode = TunerBandwidthSelectionModes.Automatic;

        // Set the default value of maximum async I/Q buffer.
        // The initialization is necessary, to be sure that it will happen once.
        MaxAsyncBufferSize = AsyncDefaultReadLength * 4;

        // Set the default value of behavior when the buffer is full.
        // The initialization is necessary, to be sure that it will happen once.
        DropSamplesOnFullBuffer = false;

        // Run GetDeviceInfo to put fundamental data of device to the cache.
        DeviceInfo = deviceInfo;
    }

    #endregion

    #region Properties

    /// <summary>
    /// Fundamental information about the managed device.
    /// </summary>
    public DeviceInfo DeviceInfo { get; }

    /// <summary>
    /// Enablement of KerberosSDR functionalities:
    /// FrequencyDitheringModes, GPIOStates.
    /// Once it is enabled, it cannot be disabled.
    /// </summary>
    /// <exception cref="RtlSdrLibraryExecutionException"></exception>
    public KerberosSDRModes KerberosSDRMode
    {
        get => _kerberosSDRMode;
        set
        {
            // KerberosSDR functionalities cannot be disabled once it is enabled.
            if (_kerberosSDRMode == KerberosSDRModes.Enabled &&
                value == KerberosSDRModes.Disabled)
            {
                throw new RtlSdrLibraryExecutionException(
                    "KerberosSDR functionalities cannot be disabled once it is enabled. " +
                    $"Device index: {DeviceInfo.Index}.");
            }

            // Set the new value.
            _kerberosSDRMode = value;
        }
    }

    /// <summary>
    /// Get the tuner type of the managed device.
    /// </summary>
    /// <exception cref="RtlSdrLibraryExecutionException"></exception>
    public TunerTypes TunerType
    {
        get
        {
            // Get the value from the device.
            TunerTypes tunerType = LibRtlSdr.rtlsdr_get_tuner_type(_deviceHandle!);

            // If we got RtlSdrTunerType.Unknown, there is an error.
            if (tunerType == TunerTypes.Unknown)
            {
                throw new RtlSdrLibraryExecutionException(
                    "The tuner type of the device isn't known." +
                    $"Error code: {tunerType}, device index: {DeviceInfo.Index}.");
            }

            // Return the value.
            return tunerType;
        }
    }

    /// <summary>
    /// Set and get the center frequency of the device.
    /// The value must be in range which was defined on the page: http://osmocom.org/projects/sdr/wiki/rtl-sdr.
    /// </summary>
    /// <exception cref="RtlSdrLibraryExecutionException"></exception>
    public Frequency CenterFrequency
    {
        get
        {
            // Get the value from the device.
            uint returnValue = LibRtlSdr.rtlsdr_get_center_freq(_deviceHandle!);

            // If we got 0, there is an error.
            if (returnValue == 0)
            {
                throw new RtlSdrLibraryExecutionException(
                    "Problem happened during reading the center frequency of the device. " +
                    $"Error code: {returnValue}, device index: {DeviceInfo.Index}.");
            }

            // Return the value.
            return new Frequency(returnValue);
        }
        set
        {
            // Check the frequency range (http://osmocom.org/projects/sdr/wiki/rtl-sdr).
            bool wrongFrequency = false;
            switch (TunerType)
            {
                // Elonics E4000.
                case TunerTypes.E4000:
                    if ((value.MHz < 52 || value.MHz >= 1100) &&
                        (value.MHz <= 1250 || value.MHz > 2200))
                    {
                        wrongFrequency = true;
                    }

                    break;
                // Rafael Micro R820T(2)/R828D
                case TunerTypes.R828D:
                case TunerTypes.R820T:
                    if (value.MHz < 24 || value.MHz > 1766)
                    {
                        wrongFrequency = true;
                    }

                    break;
                // Fitipower FC0012
                case TunerTypes.FC0012:
                    if (value.MHz < 22 || value.MHz > 948.6)
                    {
                        wrongFrequency = true;
                    }

                    break;
                // Fitipower FC0013
                case TunerTypes.FC0013:
                    if (value.MHz < 22 || value.MHz > 1100)
                    {
                        wrongFrequency = true;
                    }

                    break;
                // FCI FC2580
                case TunerTypes.FC2580:
                    if ((value.MHz < 146 || value.MHz > 308) &&
                        (value.MHz < 438 || value.MHz > 924))
                    {
                        wrongFrequency = true;
                    }

                    break;
                // Unknown
                default:
                    wrongFrequency = true;
                    break;
            }

            // If the frequency is wrong, throw an exception.
            if (wrongFrequency)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value,
                    "Problem happened during setting the center frequency of the device. " +
                    "The given frequency is outside the range supported by the tuner.");
            }

            // Set the new value on the device with console suppression.
            ExecuteWithSuppression(() =>
            {
                int returnValue = LibRtlSdr.rtlsdr_set_center_freq(_deviceHandle!, value.Hz);

                // If we did not get 0, there is an error.
                if (returnValue != 0)
                {
                    throw new RtlSdrLibraryExecutionException(
                        "Problem happened during setting the center frequency of the device. " +
                        $"Error code: {returnValue}, device index: {DeviceInfo.Index}.");
                }
            });
        }
    }

    /// <summary>
    /// Set and get the crystal frequencies of the device.
    /// </summary>
    /// <exception cref="RtlSdrLibraryExecutionException"></exception>
    public CrystalFrequency CrystalFrequency
    {
        get
        {
            // Get the value from the device.
            int returnValue = LibRtlSdr.rtlsdr_get_xtal_freq(_deviceHandle!,
                out uint rtl2832Frequency, out uint tunerFrequency);

            // If we didn't get 0, there is an error.
            if (returnValue != 0)
            {
                throw new RtlSdrLibraryExecutionException(
                    "Problem happened during reading the crystal frequencies of the device. " +
                    $"Error code: {returnValue}, device index: {DeviceInfo.Index}.");
            }

            // Return the value.
            return new CrystalFrequency(new Frequency(rtl2832Frequency), new Frequency(tunerFrequency));
        }
        set
        {
            // Crystal frequencies cannot be higher than 28.8 MHz.
            if (value.Rtl2832Frequency.MHz > 28.8 || value.TunerFrequency.MHz > 28.8)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value,
                    "Problem happened during setting the crystal frequencies of the device. " +
                    "Crystal frequencies cannot be higher than 28.8 MHz.");
            }

            // Set the new value on the device.
            ExecuteWithSuppression(() =>
            {
                int returnValue = LibRtlSdr.rtlsdr_set_xtal_freq(_deviceHandle!,
                    value.Rtl2832Frequency.Hz, value.TunerFrequency.Hz);

                // If we did not get 0, there is an error.
                if (returnValue != 0)
                {
                    throw new RtlSdrLibraryExecutionException(
                        "Problem happened during setting the crystal frequencies of the device. " +
                        $"Error code: {returnValue}, device index: {DeviceInfo.Index}.");
                }
            });
        }
    }

    /// <summary>
    /// Set the sample rate of the device.
    ///   225001 - 300000 Hz
    ///   900001 - 3200000 Hz
    ///   Sample loss is to be expected for rates more than 2400000 Hz.
    /// </summary>
    /// <exception cref="RtlSdrLibraryExecutionException"></exception>
    public Frequency SampleRate
    {
        get
        {
            // Get the value from the device.
            uint returnValue = LibRtlSdr.rtlsdr_get_sample_rate(_deviceHandle!);

            // If we got 0, there is an error.
            if (returnValue == 0)
            {
                throw new RtlSdrLibraryExecutionException(
                    "Problem happened during reading the sample rate of the device. " +
                    $"Error code: {returnValue}, device index: {DeviceInfo.Index}.");
            }

            // Return the value.
            return new Frequency(returnValue);
        }
        set
        {
            // Check the sample rate range.
            if ((value.Hz < 225001 || value.Hz > 300000) &&
                (value.Hz < 900001 || value.Hz > 3200000))
            {
                throw new ArgumentOutOfRangeException(nameof(value), value,
                    "Problem happened during setting the sample rate of the device. " +
                    "Supported ranges: 225001 - 300000 Hz and 900001 - 3200000 Hz.");
            }

            // Set the new value on the device with console suppression.
            ExecuteWithSuppression(() =>
            {
                int returnValue = LibRtlSdr.rtlsdr_set_sample_rate(_deviceHandle!, value.Hz);

                // If we did not get 0, there is an error.
                if (returnValue != 0)
                {
                    throw new RtlSdrLibraryExecutionException(
                        "Problem happened during setting the sample rate of the device. " +
                        $"Error code: {returnValue}, device index: {DeviceInfo.Index}.");
                }
            });
        }
    }

    /// <summary>
    /// Set the gain mode of the tuner.
    /// </summary>
    /// <exception cref="RtlSdrLibraryExecutionException"></exception>
    public TunerGainModes TunerGainMode
    {
        get => _deviceTunerGainMode;
        set
        {
            // Set the new value on the device.
            ExecuteWithSuppression(() =>
            {
                int returnValue = LibRtlSdr.rtlsdr_set_tuner_gain_mode(_deviceHandle!, (int)value);

                // If we did not get 0, there is an error.
                if (returnValue != 0)
                {
                    throw new RtlSdrLibraryExecutionException(
                        "Problem happened during setting the tuner gain mode of the device. " +
                        $"Error code: {returnValue}, device index: {DeviceInfo.Index}.");
                }
            });

            // Since there is no get function in librtlsdr, store the value.
            _deviceTunerGainMode = value;
        }
    }

    /// <summary>
    /// Set the tuner bandwidth selection mode for the device.
    /// </summary>
    /// <exception cref="RtlSdrLibraryExecutionException"></exception>
    public TunerBandwidthSelectionModes TunerBandwidthSelectionMode
    {
        get => _tunerBandwidthSelectionMode;
        set
        {
            // Check which mode was selected.
            switch (value)
            {
                // Automatic.
                case TunerBandwidthSelectionModes.Automatic:
                    // Set the new value on the device.
                    ExecuteWithSuppression(() =>
                    {
                        int returnValue = LibRtlSdr.rtlsdr_set_tuner_bandwidth(_deviceHandle!, 0);

                        // If we did not get 0, there is an error.
                        if (returnValue != 0)
                        {
                            throw new RtlSdrLibraryExecutionException(
                                "Problem happened during setting the tuner bandwidth of the device. " +
                                $"Error code: {returnValue}, device index: {DeviceInfo.Index}.");
                        }
                    });

                    break;

                // Manual.
                case TunerBandwidthSelectionModes.Manual:
                    // Set the bandwidth to zero.
                    _tunerBandwidth = new Frequency(0);
                    break;
            }

            // Since there is no get function in librtlsdr, store the value.
            _tunerBandwidthSelectionMode = value;
        }
    }

    /// <summary>
    /// Set the tuner bandwidth for the device.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when automatic tuner bandwidth selection mode is enabled.</exception>
    /// <exception cref="RtlSdrLibraryExecutionException"></exception>
    public Frequency TunerBandwidth
    {
        get
        {
            // Check the tuner bandwidth selection mode.
            if (TunerBandwidthSelectionMode == TunerBandwidthSelectionModes.Automatic)
            {
                throw new InvalidOperationException(
                    "Automatic tuner bandwidth selection mode is enabled, " +
                    "it is not possible to use the TunerBandwidth property. " +
                    $"Device index: {DeviceInfo.Index}.");
            }

            // Return the current settings.
            return _tunerBandwidth;
        }
        set
        {
            // Check the tuner bandwidth selection mode.
            if (TunerBandwidthSelectionMode == TunerBandwidthSelectionModes.Automatic)
            {
                throw new InvalidOperationException(
                    "Automatic tuner bandwidth selection mode is enabled, " +
                    "it is not possible to use the TunerBandwidth property. " +
                    $"Device index: {DeviceInfo.Index}.");
            }

            // Set the tuner bandwidth for the device
            ExecuteWithSuppression(() =>
            {
                int returnValue = LibRtlSdr.rtlsdr_set_tuner_bandwidth(_deviceHandle!, value.Hz);

                // If we did not get 0, there is an error.
                if (returnValue != 0)
                {
                    throw new RtlSdrLibraryExecutionException(
                        "Problem happened during setting the tuner bandwidth of the device. " +
                        $"Error code: {returnValue}, device index: {DeviceInfo.Index}.");
                }
            });

            // Since there is no get function in librtlsdr, store the value.
            _tunerBandwidth = value;
        }
    }

    /// <summary>
    /// Get a list of gains supported by the tuner, in dB.
    /// </summary>
    /// <remarks>
    /// These are the only values <see cref="TunerGain"/> accepts. The list is
    /// hardware-determined and queried once per device.
    /// <para>
    /// The list is empty for tuners which have no manual gain control: the FC2580, and a
    /// tuner the device does not recognize. On the R820T/R828D the list starts at
    /// <c>0.0</c> dB; that is the tuner's lowest gain step, not an absence of gain.
    /// </para>
    /// </remarks>
    /// <exception cref="RtlSdrLibraryExecutionException">Thrown when the supported gains cannot be read.</exception>
    public List<double> SupportedTunerGains =>
        ToSupportedGains(EnsureSupportedTunerGainsRaw());

    /// <summary>
    /// Decide whether a raw gain table from librtlsdr means "this tuner has no gain control".
    /// </summary>
    /// <param name="rawGains">Raw gain table (tenths of a dB) as reported by librtlsdr.</param>
    /// <returns>True when the table is a no-gain placeholder.</returns>
    /// <remarks>
    /// librtlsdr returns a single-entry <c>{ 0 }</c> table for the FC2580 and for an unknown
    /// tuner, commented "no gain values". Every tuner with real gain control reports at least
    /// five entries, so the table's shape identifies the placeholder unambiguously. Testing
    /// the shape rather than the tuner type matters: the <see cref="TunerType"/> getter throws
    /// for an unknown tuner, which is one of the two cases that must be handled here.
    /// </remarks>
    internal static bool HasNoGainValues(int[] rawGains) =>
        rawGains.Length == 1 && rawGains[0] == 0;

    /// <summary>
    /// Convert a raw librtlsdr gain table (tenths of a dB) to the public dB list.
    /// </summary>
    /// <param name="rawGains">Raw gain table as reported by librtlsdr.</param>
    /// <returns>Supported gains in dB, or an empty list when the tuner has no gain control.</returns>
    internal static List<double> ToSupportedGains(int[] rawGains) =>
        HasNoGainValues(rawGains)
            ? []
            : rawGains.Select(gain => gain / 10.0).ToList();

    /// <summary>
    /// Decide whether a gain (in tenths of a dB) is one of the tuner's supported steps.
    /// </summary>
    /// <param name="rawGains">Raw gain table as reported by librtlsdr.</param>
    /// <param name="gainTenths">Requested gain in tenths of a dB.</param>
    /// <returns>True when the gain is supported by the tuner.</returns>
    /// <remarks>
    /// Compares in integer tenths of a dB, so no floating point rounding is involved.
    /// A tuner with no gain control supports nothing, not even 0.
    /// </remarks>
    internal static bool IsSupportedGain(int[] rawGains, int gainTenths)
    {
        if (HasNoGainValues(rawGains))
        {
            return false;
        }

        foreach (int supportedGain in rawGains)
        {
            if (supportedGain == gainTenths)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Return the raw supported tuner gains (in tenths of a dB) from librtlsdr, caching them.
    /// Gains are hardware-determined and never change, so the query happens at most once.
    /// </summary>
    /// <exception cref="RtlSdrLibraryExecutionException"></exception>
    private int[] EnsureSupportedTunerGainsRaw()
    {
        // Use cached raw gains if available.
        if (_supportedTunerGainsCache != null)
        {
            return _supportedTunerGainsCache;
        }

        // Get the amount of the supported gains.
        int amountTunerGains = LibRtlSdr.rtlsdr_get_tuner_gains(_deviceHandle!, null!);

        // If we got less or equal to 0, there is an error.
        if (amountTunerGains <= 0)
        {
            throw new RtlSdrLibraryExecutionException(
                "Problem happened during querying the amount of the supported gains by the device. " +
                $"Error code: {amountTunerGains}, device index: {DeviceInfo.Index}.");
        }

        // Get the supported gains.
        int[] supportedGains = new int[amountTunerGains];
        int returnCode = LibRtlSdr.rtlsdr_get_tuner_gains(_deviceHandle!, supportedGains);

        // If we got 0 or less, there is an error.
        if (returnCode <= 0)
        {
            throw new RtlSdrLibraryExecutionException(
                "Problem happened during querying the supported gains by the device. " +
                $"Error code: {returnCode}, device index: {DeviceInfo.Index}.");
        }

        // Cache the raw gains from librtlsdr.
        _supportedTunerGainsCache = supportedGains;
        return _supportedTunerGainsCache;
    }

    /// <summary>
    /// Set and get the tuner gain of the device, in dB.
    /// Manual tuner gain mode must be enabled for this to work.
    /// </summary>
    /// <remarks>
    /// Only the steps listed by <see cref="SupportedTunerGains"/> are accepted; on the
    /// R820T/R828D those include <c>0.0</c> dB, which selects the tuner's minimum-gain
    /// configuration.
    /// <para>
    /// The device reports no gain until one has been set, so the getter throws in that case
    /// rather than returning a misleading <c>0.0</c> dB. Set <see cref="TunerGain"/>, or use
    /// <see cref="SetMinimumTunerGain"/> / <see cref="SetMaximumTunerGain"/>, before reading.
    /// </para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">Thrown when AGC tuner gain mode is enabled,
    /// or when no manual gain has been set yet in this session.</exception>
    /// <exception cref="RtlSdrLibraryExecutionException">Thrown when the device rejects the gain.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the gain is not a step supported by the tuner.</exception>
    public double TunerGain
    {
        get
        {
            // Check the tuner gain mode.
            if (TunerGainMode == TunerGainModes.AGC)
            {
                throw new InvalidOperationException(
                    "AGC tuner gain mode is enabled, it is not possible to use the TunerGain property. " +
                    $"Device index: {DeviceInfo.Index}.");
            }

            // librtlsdr returns its cached dev->gain, which stays 0 until a gain is written
            // (rtlsdr_set_tuner_gain_mode does not touch it). Without a gain to report, the
            // device has nothing to say: this is a usage error, not a device failure.
            if (_lastSetTunerGain == null)
            {
                throw new InvalidOperationException(
                    "No tuner gain has been set yet, it is not possible to read the TunerGain property. " +
                    $"Set TunerGain first. Device index: {DeviceInfo.Index}.");
            }

            // Get the value from the device.
            int returnValue = LibRtlSdr.rtlsdr_get_tuner_gain(_deviceHandle!);

            // Return the value. 0 is a valid reading here (see remarks), so it is not
            // treated as an error code.
            return returnValue / 10.0;
        }
        set
        {
            // Check the tuner gain mode.
            if (TunerGainMode == TunerGainModes.AGC)
            {
                throw new InvalidOperationException(
                    "AGC tuner gain mode is enabled, it is not possible to use the TunerGain property. " +
                    $"Device index: {DeviceInfo.Index}.");
            }

            // Convert double (dB) to int (tenths of dB). Rounding avoids floating point
            // truncation with a plain cast (e.g. 49.6 * 10 would truncate to 495).
            int gain = (int)Math.Round(value * 10);

            // Is the given value supported? Compare in integer tenths of dB against the raw
            // cache (no allocation, no floating point).
            int[] rawGains = EnsureSupportedTunerGainsRaw();
            if (!IsSupportedGain(rawGains, gain))
            {
                // Distinguish "this tuner has no gain control at all" from "wrong step",
                // because SupportedTunerGains is empty in the first case and pointing the
                // caller at it would be useless.
                throw new ArgumentOutOfRangeException(nameof(value), value,
                    HasNoGainValues(rawGains)
                        ? "Problem happened during setting the tuner gain of the device. " +
                          "The tuner does not support manual gain control."
                        : "Problem happened during setting the tuner gain of the device. " +
                          "The given tuner gain is not supported, see SupportedTunerGains.");
            }

            // Set the gain for the device
            ExecuteWithSuppression(() =>
            {
                int returnValue = LibRtlSdr.rtlsdr_set_tuner_gain(_deviceHandle!, gain);

                // If we did not get 0, there is an error.
                if (returnValue != 0)
                {
                    throw new RtlSdrLibraryExecutionException(
                        "Problem happened during setting the tuner gain of the device. " +
                        $"Error code: {returnValue}, device index: {DeviceInfo.Index}.");
                }
            });

            // Remember what the device now holds, so the getter can tell a real 0.0 dB
            // reading apart from "never set". Only reached when the write succeeded.
            _lastSetTunerGain = gain;
        }
    }

    /// <summary>
    /// Enable or disable the internal digital AGC of the device.
    /// </summary>
    /// <exception cref="RtlSdrLibraryExecutionException"></exception>
    public AGCModes AGCMode
    {
        get => _deviceAGCMode;
        set
        {
            // Set the new value on the device.
            ExecuteWithSuppression(() =>
            {
                int returnValue = LibRtlSdr.rtlsdr_set_agc_mode(_deviceHandle!, (int)value);

                // If we did not get 0, there is an error.
                if (returnValue != 0)
                {
                    throw new RtlSdrLibraryExecutionException(
                        "Problem happened during setting AGC mode of the device. " +
                        $"Error code: {returnValue}, device index: {DeviceInfo.Index}.");
                }
            });

            // Since there is no get function in librtlsdr, store the value.
            _deviceAGCMode = value;
        }
    }

    /// <summary>
    /// Enable or disable the test mode of the device.
    /// Enable test mode that returns an 8 bit counter instead of the samples.
    /// The counter is generated inside the RTL2832.
    /// </summary>
    /// <exception cref="RtlSdrLibraryExecutionException"></exception>
    public TestModes TestMode
    {
        get => _deviceTestMode;
        set
        {
            // Set the new value on the device.
            ExecuteWithSuppression(() =>
            {
                int returnValue = LibRtlSdr.rtlsdr_set_testmode(_deviceHandle!, (int)value);

                // If we did not get 0, there is an error.
                if (returnValue != 0)
                {
                    throw new RtlSdrLibraryExecutionException(
                        "Problem happened during setting test mode of the device. " +
                        $"Error code: {returnValue}, device index: {DeviceInfo.Index}.");
                }
            });

            // Since there is no get function in librtlsdr, store the value.
            _deviceTestMode = value;
        }
    }

    /// <summary>
    /// Frequency correction value (in ppm) for the device.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"></exception>
    /// <exception cref="RtlSdrLibraryExecutionException"></exception>
    public int FrequencyCorrection
    {
        get
        {
            // Get the value from the device.
            int returnValue = LibRtlSdr.rtlsdr_get_freq_correction(_deviceHandle!);

            // Return the value.
            return returnValue;
        }
        set =>
            // Set the new value on the device.
            ExecuteWithSuppression(() =>
            {
                int returnValue = LibRtlSdr.rtlsdr_set_freq_correction(_deviceHandle!, value);

                // If the returned value is -2, it means, that the value was same as previously.
                // Hide this error.
                if (returnValue == -2)
                {
                    returnValue = 0;
                }

                // If we did not get 0, there is an error.
                if (returnValue != 0)
                {
                    throw new RtlSdrLibraryExecutionException(
                        "Problem happened during setting frequency correction of the device. " +
                        $"Error code: {returnValue}, device index: {DeviceInfo.Index}.");
                }
            });
    }

    /// <summary>
    /// Direct Sampling mode for the device.
    /// </summary>
    /// <exception cref="RtlSdrLibraryExecutionException"></exception>
    public DirectSamplingModes DirectSamplingMode
    {
        get
        {
            // Get the value from the device.
            int returnValue = LibRtlSdr.rtlsdr_get_direct_sampling(_deviceHandle!);

            // If we got less than 0, there is an error.
            if (returnValue < 0)
            {
                throw new RtlSdrLibraryExecutionException(
                    "Problem happened during reading the direct sampling mode of the device. " +
                    $"Error code: {returnValue}, device index: {DeviceInfo.Index}.");
            }

            // Return the value.
            return (DirectSamplingModes)returnValue;
        }
        set =>
            // Set the new value on the device.
            ExecuteWithSuppression(() =>
            {
                int returnValue = LibRtlSdr.rtlsdr_set_direct_sampling(_deviceHandle!, (int)value);

                // If we did not get 0, there is an error.
                if (returnValue != 0)
                {
                    throw new RtlSdrLibraryExecutionException(
                        "Problem happened during setting direct sampling mode of the device. " +
                        $"Error code: {returnValue}, device index: {DeviceInfo.Index}.");
                }
            });
    }

    /// <summary>
    /// Offset tuning mode for zero-IF tuners.
    /// </summary>
    /// <exception cref="RtlSdrLibraryExecutionException"></exception>
    public OffsetTuningModes OffsetTuningMode
    {
        get
        {
            // Get the value from the device.
            int returnValue = LibRtlSdr.rtlsdr_get_offset_tuning(_deviceHandle!);

            // If we got less than 0, there is an error.
            if (returnValue < 0)
            {
                throw new RtlSdrLibraryExecutionException(
                    "Problem happened during reading the offset tuning mode of the device. " +
                    $"Error code: {returnValue}, device index: {DeviceInfo.Index}.");
            }

            // Return the value.
            return (OffsetTuningModes)returnValue;
        }
        set =>
            // Set the new value on the device.
            ExecuteWithSuppression(() =>
            {
                int returnValue = LibRtlSdr.rtlsdr_set_offset_tuning(_deviceHandle!, (int)value);

                // If we did not get 0, there is an error.
                if (returnValue != 0)
                {
                    throw new RtlSdrLibraryExecutionException(
                        "Problem happened during setting offset tuning mode of the device. " +
                        $"Error code: {returnValue}, device index: {DeviceInfo.Index}.");
                }
            });
    }

    /// <summary>
    /// Frequency dithering for R820T tuners.
    /// Can be used only with the modified RTL-SDR library for KerberosSDR:
    /// https://github.com/rtlsdrblog/rtl-sdr-kerberos/
    /// </summary>
    /// <exception cref="RtlSdrLibraryExecutionException"></exception>
    public FrequencyDitheringModes FrequencyDitheringMode
    {
        get => _frequencyDitheringMode;
        set
        {
            // This property can be used if the KerberosSDR mode is enabled and R820T is used.
            if (KerberosSDRMode == KerberosSDRModes.Disabled ||
                TunerType != TunerTypes.R820T)
            {
                throw new RtlSdrLibraryExecutionException(
                    "FrequencyDitheringMode property can be used if the KerberosSDR mode is enabled and " +
                    $"R820T is used. Tuner Type: {TunerType}, KerberosSDRMode: {KerberosSDRMode}, " +
                    $"device index: {DeviceInfo.Index}.");
            }

            // The NotSet value cannot be used, it is for internal usage.
            if (value == FrequencyDitheringModes.NotSet)
            {
                throw new RtlSdrLibraryExecutionException(
                    "FrequencyDitheringMode.NotSet value cannot be used, it is for internal usage. " +
                    $"Device index: {DeviceInfo.Index}.");
            }

            // Set the new value on the device.
            ExecuteWithSuppression(() =>
            {
                int returnValue = LibRtlSdr.rtlsdr_set_dithering(_deviceHandle!, (int)value);

                // If we did not get 0, there is an error.
                if (returnValue != 0)
                {
                    throw new RtlSdrLibraryExecutionException(
                        "Problem happened during setting frequency dithering mode of the device. " +
                        $"Error code: {returnValue}, device index: {DeviceInfo.Index}.");
                }
            });
        }
    }

    #endregion

    #region Methods

    /// <summary>
    /// Reset the device buffer.
    /// </summary>
    /// <exception cref="RtlSdrLibraryExecutionException"></exception>
    public void ResetDeviceBuffer()
    {
        // Reset the device's buffer.
        int returnCode = LibRtlSdr.rtlsdr_reset_buffer(_deviceHandle!);

        // Other error was happened.
        if (returnCode != 0)
        {
            throw new RtlSdrLibraryExecutionException(
                "Problem happened during resetting buffer of the RTL-SDR device. " +
                $"Error code: {returnCode}, device index: {DeviceInfo.Index}.");
        }
    }

    /// <summary>
    /// Set the maximum tuner gain supported by the tuner.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when AGC tuner gain mode is enabled.</exception>
    /// <exception cref="RtlSdrLibraryExecutionException">Thrown when the tuner has no gain control.</exception>
    public void SetMaximumTunerGain() => TunerGain = RequireSupportedTunerGains().Max();

    /// <summary>
    /// Set the minimum tuner gain supported by the tuner.
    /// </summary>
    /// <remarks>
    /// On the R820T/R828D the minimum is <c>0.0</c> dB: the tuner's lowest gain step, not an
    /// absence of gain.
    /// </remarks>
    /// <exception cref="InvalidOperationException">Thrown when AGC tuner gain mode is enabled.</exception>
    /// <exception cref="RtlSdrLibraryExecutionException">Thrown when the tuner has no gain control.</exception>
    public void SetMinimumTunerGain() => TunerGain = RequireSupportedTunerGains().Min();

    /// <summary>
    /// Return the supported tuner gains, failing with a clear message when the tuner has none.
    /// </summary>
    /// <returns>Supported gains in dB, never empty.</returns>
    /// <exception cref="RtlSdrLibraryExecutionException">Thrown when the tuner has no gain control.</exception>
    private List<double> RequireSupportedTunerGains()
    {
        List<double> supportedGains = SupportedTunerGains;

        // Guard the empty list here: letting Max()/Min() throw would surface as a bare
        // "Sequence contains no elements" with nothing pointing at the tuner.
        // The message deliberately does not name the tuner type: an unknown tuner is one of
        // the two cases which land here, and the TunerType getter throws for exactly that.
        if (supportedGains.Count == 0)
        {
            throw new RtlSdrLibraryExecutionException(
                "The tuner does not support manual gain control, so no minimum or maximum " +
                $"gain can be set. Device index: {DeviceInfo.Index}.");
        }

        return supportedGains;
    }

    /// <summary>
    /// Enable or disable the Bias Tee on the GPIO pin 0.
    /// </summary>
    /// <param name="mode">Enabled, Disabled</param>
    /// <exception cref="RtlSdrLibraryExecutionException"></exception>
    public void SetBiasTee(BiasTeeModes mode)
    {
        // Set the new value on the device.
        ExecuteWithSuppression(() =>
        {
            int returnValue = LibRtlSdr.rtlsdr_set_bias_tee(_deviceHandle!, (int)mode);

            // If we did not get 0, there is an error.
            if (returnValue != 0)
            {
                throw new RtlSdrLibraryExecutionException(
                    "Problem happened during setting Bias Tee mode of the device. " +
                    $"Error code: {returnValue}, device index: {DeviceInfo.Index}.");
            }
        });
    }

    /// <summary>
    /// Enable or disable the Bias Tee on the given GPIO pin.
    /// The function is implemented for R820T only.
    /// </summary>
    /// <param name="gpio">The GPIO pin to configure as a Bias Tee control (0..7).</param>
    /// <param name="mode">Enabled, Disabled</param>
    /// <exception cref="RtlSdrLibraryExecutionException"></exception>
    public void SetBiasTeeGPIO(int gpio, BiasTeeModes mode)
    {
        // This method can be executed if R820T is used.
        if (TunerType != TunerTypes.R820T)
        {
            throw new RtlSdrLibraryExecutionException(
                "SetBiasTeeGPIO can be executed if R820T is used. " +
                $"Tuner Type: {TunerType}, device index: {DeviceInfo.Index}.");
        }

        // Check the GPIO number. R820T has 8 GPIO (0..7).
        if (gpio < 0 || gpio > 7)
        {
            throw new RtlSdrLibraryExecutionException(
                "Wrong GPIO is used. R820T has 8 GPIO (0..7)." +
                $"GPIO: {gpio}, device index: {DeviceInfo.Index}.");
        }

        // Set the new value on the device.
        ExecuteWithSuppression(() =>
        {
            int returnValue = LibRtlSdr.rtlsdr_set_bias_tee_gpio(_deviceHandle!, gpio, (int)mode);

            // If we did not get 0, there is an error.
            if (returnValue != 0)
            {
                throw new RtlSdrLibraryExecutionException(
                    "Problem happened during setting Bias Tee mode of the device. " +
                    $"Error code: {returnValue}, GPIO: {gpio}, device index: {DeviceInfo.Index}.");
            }
        });
    }

    /// <summary>
    /// Generic GPIO enable or disable.
    /// Can be used only with the modified RTL-SDR library for KerberosSDR:
    /// https://github.com/rtlsdrblog/rtl-sdr-kerberos/
    /// </summary>
    /// <param name="gpio">The GPIO pin to.</param>
    /// <param name="mode">Enabled, Disabled</param>
    public void SetGPIO(int gpio, GPIOModes mode)
    {
        // This property can be used if the KerberosSDR mode is enabled and R820T is used.
        if (KerberosSDRMode == KerberosSDRModes.Disabled)
        {
            throw new RtlSdrLibraryExecutionException(
                "SetGPIO method can be used if the KerberosSDR mode is enabled. " +
                $"KerberosSDRMode: {KerberosSDRMode}, device index: {DeviceInfo.Index}.");
        }

        // This method can be executed if R820T is used.
        if (TunerType != TunerTypes.R820T)
        {
            throw new RtlSdrLibraryExecutionException(
                "SetGPIO can be executed if R820T is used. " +
                $"Tuner Type: {TunerType}, device index: {DeviceInfo.Index}.");
        }

        // Check the GPIO number. R820T has 8 GPIO (0..7).
        if (gpio is < 0 or > 7)
        {
            throw new RtlSdrLibraryExecutionException(
                "Wrong GPIO is used. R820T has 8 GPIO (0..7)." +
                $"GPIO: {gpio}, device index: {DeviceInfo.Index}.");
        }

        // Set the new value on the device.
        ExecuteWithSuppression(() =>
        {
            int returnValue = LibRtlSdr.rtlsdr_set_gpio(_deviceHandle!, (int)mode, gpio);

            // If we did not get 0, there is an error.
            if (returnValue != 0)
            {
                throw new RtlSdrLibraryExecutionException(
                    "Problem happened during setting GPIO status of the device. " +
                    $"Error code: {returnValue}, GPIO: {gpio}, device index: {DeviceInfo.Index}.");
            }
        });
    }

    #endregion

    #region Implementing IDispose and ToString

    /// <summary>
    /// Releases all resources used by the RTL-SDR managed device.
    /// </summary>
    /// <remarks>
    /// Stops any asynchronous reading in progress, closes the device, and releases the
    /// resources held on its behalf. Safe to call more than once.
    /// </remarks>
    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Releases the unmanaged resources used by the RTL-SDR managed device and optionally releases the managed resources.
    /// </summary>
    /// <param name="disposing">
    /// true to release both managed and unmanaged resources; false to release only unmanaged resources.
    /// </param>
    /// <remarks>
    /// This method is called by the public Dispose method and the finalizer.
    /// When called from Dispose, disposing is true and both managed and unmanaged resources are released.
    /// When called from the finalizer, disposing is false and only unmanaged resources are released.
    /// </remarks>
    private void Dispose(bool disposing)
    {
        // Check to see if Dispose has already been called.
        if (_disposed)
        {
            return;
        }

        // Whether the async worker is provably stopped. Stays true unless Stop times out
        // below, in which case the worker (and native callback) may still be running and
        // the callback GC handle / device handle must NOT be freed under it.
        bool workerStopped = true;

        if (disposing)
        {
            // Dispose managed resources
            // Stop async reading if it's running. Dispose must not throw: keep the
            // error observable through AsyncReadException instead.
            try
            {
                StopReadSamplesAsync();
            }
            catch (Exception ex)
            {
                // RecordAsyncError keeps the first error, so a genuine async fault
                // captured earlier is preserved rather than overwritten by this wrapper.
                RecordAsyncError(ex);

                // Stop leaves the worker reference set only when the bounded join timed
                // out (the device is wedged and the callback may still fire).
                workerStopped = _asyncWorker == null;
            }

            // Only dispose the device handle when the worker has provably stopped.
            // On a Join timeout, deliberately leak it: freeing it under a live native
            // callback would crash the process.
            if (workerStopped)
            {
                // Dispose the safe handle, which automatically calls rtlsdr_close
                _deviceHandle?.Dispose();
            }
        }

        // Release unmanaged resources
        // Release the device context GC handle, unless the worker may still be running
        // (see above): the callback dereferences this handle, so it must outlive it.
        if (workerStopped && _deviceContext.IsAllocated)
        {
            _deviceContext.Free();
        }

        // Mark as disposed
        _disposed = true;
    }

    /// <summary>
    /// Finalizer for the RTL-SDR managed device.
    /// </summary>
    /// <remarks>
    /// This finalizer ensures cleanup if Dispose is not called explicitly.
    /// However, best practice is to always call Dispose or use a using statement.
    /// </remarks>
    ~RtlSdrManagedDevice()
    {
        Dispose(disposing: false);
    }

    /// <summary>
    /// Override ToString method.
    /// </summary>
    /// <returns>String value of the RtlSdrManagedDevice instance.</returns>
    public override string ToString()
    {
        return
            $"Index: {DeviceInfo.Index}; Name: {DeviceInfo.Name}; Manufacturer: {DeviceInfo.Manufacturer}; " +
            $"Type: {DeviceInfo.ProductType}; Serial: {DeviceInfo.Serial}";
    }

    #endregion
}
