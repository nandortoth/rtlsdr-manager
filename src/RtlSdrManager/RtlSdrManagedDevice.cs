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
    /// </summary>
    private GCHandle _deviceContext;

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

    /// <summary>
    /// Executes a function with scoped console output suppression.
    /// Uses reference-counted global suppressor to prevent file descriptor corruption
    /// when multiple devices are being configured simultaneously.
    /// </summary>
    /// <typeparam name="T">Return type of the function.</typeparam>
    /// <param name="func">The function to execute.</param>
    /// <returns>The result of the function.</returns>
    private T ExecuteWithSuppression<T>(Func<T> func)
    {
        using var scope = new RtlSdrDeviceManager.SuppressionScope();
        return func();
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
        // Console output suppression is managed globally, not per-device
        _deviceHandle = LibRtlSdr.OpenDevice(deviceIndex, suppressConsoleOutput: false);

        // Set the device context.
        _deviceContext = GCHandle.Alloc(this);

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
    /// FrequencyDitheringModes, GPIOStates
    /// </summary>
    /// <exception cref="RtlSdrLibraryExecutionException"></exception>
    public KerberosSDRModes KerberosSDRMode
    {
        get => _kerberosSDRMode;
        init
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
                throw new ArgumentOutOfRangeException(
                    "Problem happened during setting the center frequency of the device. " +
                    $"Wrong frequency was given: {value}.");
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
                throw new ArgumentOutOfRangeException(
                    "Problem happened during setting the crystal frequencies of the device. " +
                    $"Wrong frequency was given: {value}.");
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
                throw new ArgumentOutOfRangeException(
                    "Problem happened during setting the sample rate of the device. " +
                    $"Wrong frequency was given: {value}.");
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
                                "Problem happened during setting the tuner gain mode of the device. " +
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
    /// <exception cref="RtlSdrLibraryExecutionException"></exception>
    /// <exception cref="ArgumentOutOfRangeException"></exception>
    public Frequency TunerBandwidth
    {
        get
        {
            // Check the tuner bandwidth selection mode.
            if (TunerBandwidthSelectionMode == TunerBandwidthSelectionModes.Automatic)
            {
                throw new RtlSdrLibraryExecutionException(
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
                throw new RtlSdrLibraryExecutionException(
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
    /// Get a list of gains supported by the tuner.
    /// </summary>
    /// <exception cref="RtlSdrLibraryExecutionException"></exception>
    public List<double> SupportedTunerGains
    {
        get
        {
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

            // Convert int to double (dB).
            var gains = supportedGains.Where(gain => gain != 0).Select(gain => gain / 10.0).ToList();

            // Return the value.
            return gains;
        }
    }

    /// <summary>
    /// Set the tuner gain for the device.
    /// Manual tuner gain mode must be enabled for this to work.
    /// </summary>
    /// <exception cref="RtlSdrLibraryExecutionException"></exception>
    /// <exception cref="ArgumentOutOfRangeException"></exception>
    public double TunerGain
    {
        get
        {
            // Check the tuner gain mode.
            if (TunerGainMode == TunerGainModes.AGC)
            {
                throw new RtlSdrLibraryExecutionException(
                    "AGC tuner gain mode is enabled, it is not possible to use the TunerGain property. " +
                    $"Device index: {DeviceInfo.Index}.");
            }

            // Get the value from the device.
            int returnValue = LibRtlSdr.rtlsdr_get_tuner_gain(_deviceHandle!);

            // If we got 0, there is an error.
            if (returnValue == 0)
            {
                throw new RtlSdrLibraryExecutionException(
                    "Problem happened during reading the tuner gain of the device. " +
                    $"Error code: {returnValue}, device index: {DeviceInfo.Index}.");
            }

            // Return the value.
            return returnValue / 10.0;
        }
        set
        {
            // Check the tuner gain mode.
            if (TunerGainMode == TunerGainModes.AGC)
            {
                throw new RtlSdrLibraryExecutionException(
                    "AGC tuner gain mode is enabled, it is not possible to use the TunerGain property. " +
                    $"Device index: {DeviceInfo.Index}.");
            }

            // Is the given value supported?
            if (!SupportedTunerGains.Contains(value))
            {
                throw new ArgumentOutOfRangeException(
                    "Problem happened during setting the tuner gain of the device. " +
                    $"Wrong tuner gain was given: {value}, it is not supported.");
            }

            // Convert double (dB) to int.
            int gain = (int)(value * 10);

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
    /// Set the maximum supported tuner gain.
    /// </summary>
    public void SetMaximumTunerGain() => TunerGain = SupportedTunerGains.Max();

    /// <summary>
    /// Set the minimum supported tuner gain.
    /// </summary>
    public void SetMinimumTunerGain() => TunerGain = SupportedTunerGains.Min();

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
    /// This method stops any async operations, disposes the device handle (which automatically
    /// calls rtlsdr_close via SafeHandle), and releases the GC handle for the device context.
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

        if (disposing)
        {
            // Dispose managed resources
            // Stop async reading if it's running
            StopReadSamplesAsync();

            // Dispose the safe handle, which automatically calls rtlsdr_close
            _deviceHandle?.Dispose();
        }

        // Release unmanaged resources
        // Release the device context GC handle
        if (_deviceContext.IsAllocated)
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
