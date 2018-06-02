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
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using RtlSdrManager.Exceptions;
using RtlSdrManager.Types;

namespace RtlSdrManager
{
    /// <summary>
    /// Class for a managed (opened) RTL-SDR device.
    /// </summary>
    /// <inheritdoc />
    public sealed partial class RtlSdrManagedDevice : IDisposable
    {
        #region Fields

        /// <summary>
        /// Device Pointer, used by RTL-SDR wrapper library.
        /// </summary>
        private IntPtr _devicePointer;

        /// <summary>
        /// Index of the RTL-SDR device.
        /// </summary>
        private readonly uint _deviceIndex;

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
        /// Tuner bandwidth selection mode of the RTL-SDR device;
        /// </summary>
        private TunerBandwidthSelectionModes _tunerBandwidthSelectionMode;
        
        /// <summary>
        /// Tuner bandwidth of the RTL-SDR device;
        /// </summary>
        private Frequency _tunerBandwidth;

        /// <summary>
        /// Device context for async read.
        /// </summary>
        private GCHandle _deviceContext;

        /// <summary>
        /// Private field to implement IDispose interface.
        /// </summary>
        private bool _disposed;

        #endregion

        #region Constructor and DeviceInfo

        /// <summary>
        /// Create an instance of managed RTL-SDR device.
        /// </summary>
        /// <param name="index">Index of the device on the system.</param>
        internal RtlSdrManagedDevice(uint index)
        {
            // Get the other data of the device.
            var returnCode = RtlSdrLibraryWrapper.rtlsdr_open(out _devicePointer, index);

            // The index doesn't exists on the system.
            if (returnCode == -1)
            {
                throw new RtlSdrLibraryExecutionException(
                    "RTL-SDR device cannot be found with the given index. " +
                    $"Error code: {returnCode}, device index: {index}.");
            }

            // The device is already managed.
            if (returnCode == -6)
            {
                throw new RtlSdrLibraryExecutionException(
                    "The RTL-SDR device is already managed (opened). " +
                    $"Error code: {returnCode}, device index: {index}.");
            }

            // Other error was happened.
            if (returnCode != 0)
            {
                throw new RtlSdrLibraryExecutionException(
                    "Problem happened during reading opening the RTL-SDR device. " +
                    $"Error code: {returnCode}, device index: {index}.");
            }

            // Store the index number of the device.
            _deviceIndex = index;

            // Set the device context.
            _deviceContext = GCHandle.Alloc(this);

            // Set the tuner gain mode to automatic.
            // The initialization is necessary, to be sure that it will happen once.
            TunerGainMode = TunerGainModes.AGC;

            // Set the AGC mode to disabled.
            // The initialization is necessary, to be sure that it will happen once.
            AGCMode = AGCModes.Disabled;

            // Set the test mode to disabled.
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
            DeviceInfo = RtlSdrDeviceManager.GetDeviceInfo(index);
        }

        #endregion

        #region Properties

        /// <summary>
        /// Fundamental information about the managed device.
        /// </summary>
        public DeviceInfo DeviceInfo { get; }

        /// <summary>
        /// Get the tuner type of the managed device.
        /// </summary>
        /// <exception cref="RtlSdrLibraryExecutionException"></exception>
        public TunerTypes TunerType
        {
            get
            {
                // Get the value from the device.
                var tunerType = RtlSdrLibraryWrapper.rtlsdr_get_tuner_type(_devicePointer);

                // If we got RtlSdrTunerType.Unknown, there is an error.
                if (tunerType == TunerTypes.Unknown)
                {
                    throw new RtlSdrLibraryExecutionException(
                        "The tunner type of the device isn't known." +
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
                var returnValue = RtlSdrLibraryWrapper.rtlsdr_get_center_freq(_devicePointer);

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
                var wrongFrequency = false;
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

                // Set the new value on the device.
                var returnValue = RtlSdrLibraryWrapper.rtlsdr_set_center_freq(_devicePointer, value.Hz);

                // If we did not get 0, there is an error.
                if (returnValue != 0)
                {
                    throw new RtlSdrLibraryExecutionException(
                        "Problem happened during setting the center frequency of the device. " +
                        $"Error code: {returnValue}, device index: {DeviceInfo.Index}.");
                }
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
                var returnValue = RtlSdrLibraryWrapper.rtlsdr_get_xtal_freq(_devicePointer,
                    out var rtl2832Frequency, out var tunerFrequency);

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
                var returnValue = RtlSdrLibraryWrapper.rtlsdr_set_xtal_freq(_devicePointer,
                    value.Rtl2832Frequency.Hz, value.TunerFrequency.Hz);

                // If we did not get 0, there is an error.
                if (returnValue != 0)
                {
                    throw new RtlSdrLibraryExecutionException(
                        "Problem happened during setting the crystal frequencies of the device. " +
                        $"Error code: {returnValue}, device index: {DeviceInfo.Index}.");
                }
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
                var returnValue = RtlSdrLibraryWrapper.rtlsdr_get_sample_rate(_devicePointer);

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

                // Set the new value on the device.
                var returnValue = RtlSdrLibraryWrapper.rtlsdr_set_sample_rate(_devicePointer, value.Hz);

                // If we did not get 0, there is an error.
                if (returnValue != 0)
                {
                    throw new RtlSdrLibraryExecutionException(
                        "Problem happened during setting the sample rate of the device. " +
                        $"Error code: {returnValue}, device index: {DeviceInfo.Index}.");
                }
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
                var returnValue = RtlSdrLibraryWrapper.rtlsdr_set_tuner_gain_mode(_devicePointer, (int) value);

                // If we did not get 0, there is an error.
                if (returnValue != 0)
                {
                    throw new RtlSdrLibraryExecutionException(
                        "Problem happened during setting the tuner gain mode of the device. " +
                        $"Error code: {returnValue}, device index: {DeviceInfo.Index}.");
                }

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
                        var returnValue = RtlSdrLibraryWrapper.rtlsdr_set_tuner_bandwidth(_devicePointer, 0);
                        
                        // If we did not get 0, there is an error.
                        if (returnValue != 0)
                        {
                            throw new RtlSdrLibraryExecutionException(
                                "Problem happened during setting the tuner gain mode of the device. " +
                                $"Error code: {returnValue}, device index: {DeviceInfo.Index}.");
                        }
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
                var returnValue = RtlSdrLibraryWrapper.rtlsdr_set_tuner_bandwidth(_devicePointer, value.Hz);

                // If we did not get 0, there is an error.
                if (returnValue != 0)
                {
                    throw new RtlSdrLibraryExecutionException(
                        "Problem happened during setting the tuner bandwidth of the device. " +
                        $"Error code: {returnValue}, device index: {DeviceInfo.Index}.");
                }
                
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
                var amountTunerGains = RtlSdrLibraryWrapper.rtlsdr_get_tuner_gains(_devicePointer, null);

                // If we got less or equal to 0, there is an error.
                if (amountTunerGains <= 0)
                {
                    throw new RtlSdrLibraryExecutionException(
                        "Problem happened during querying the amount of the supported gains by the device. " +
                        $"Error code: {amountTunerGains}, device index: {DeviceInfo.Index}.");
                }

                // Get the supported gains.
                var supportedGains = new int[amountTunerGains];
                var returnCode = RtlSdrLibraryWrapper.rtlsdr_get_tuner_gains(_devicePointer, supportedGains);

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
                var returnValue = RtlSdrLibraryWrapper.rtlsdr_get_tuner_gain(_devicePointer);

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
                var gain = (int) (value * 10);

                // Set the gain for the device
                var returnValue = RtlSdrLibraryWrapper.rtlsdr_set_tuner_gain(_devicePointer, gain);

                // If we did not get 0, there is an error.
                if (returnValue != 0)
                {
                    throw new RtlSdrLibraryExecutionException(
                        "Problem happened during setting the tuner gain of the device. " +
                        $"Error code: {returnValue}, device index: {DeviceInfo.Index}.");
                }
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
                var returnValue = RtlSdrLibraryWrapper.rtlsdr_set_agc_mode(_devicePointer, (int) value);

                // If we did not get 0, there is an error.
                if (returnValue != 0)
                {
                    throw new RtlSdrLibraryExecutionException(
                        "Problem happened during setting AGC mode of the device. " +
                        $"Error code: {returnValue}, device index: {DeviceInfo.Index}.");
                }

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
                var returnValue = RtlSdrLibraryWrapper.rtlsdr_set_testmode(_devicePointer, (int) value);

                // If we did not get 0, there is an error.
                if (returnValue != 0)
                {
                    throw new RtlSdrLibraryExecutionException(
                        "Problem happened during setting test mode of the device. " +
                        $"Error code: {returnValue}, device index: {DeviceInfo.Index}.");
                }

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
                var returnValue = RtlSdrLibraryWrapper.rtlsdr_get_freq_correction(_devicePointer);

                // Return the value.
                return returnValue;
            }
            set
            {
                // Set the new value on the device.
                var returnValue = RtlSdrLibraryWrapper.rtlsdr_set_freq_correction(_devicePointer, value);
                
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
            }
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
                var returnValue = RtlSdrLibraryWrapper.rtlsdr_get_direct_sampling(_devicePointer);

                // If we got less than 0, there is an error.
                if (returnValue < 0)
                {
                    throw new RtlSdrLibraryExecutionException(
                        "Problem happened during reading the direct sampling mode of the device. " +
                        $"Error code: {returnValue}, device index: {DeviceInfo.Index}.");
                }

                // Return the value.
                return (DirectSamplingModes) returnValue;
            }
            set
            {
                // Set the new value on the device.
                var returnValue = RtlSdrLibraryWrapper.rtlsdr_set_direct_sampling(_devicePointer, (int) value);
                
                // If we did not get 0, there is an error.
                if (returnValue != 0)
                {
                    throw new RtlSdrLibraryExecutionException(
                        "Problem happened during setting direct sampling mode of the device. " +
                        $"Error code: {returnValue}, device index: {DeviceInfo.Index}.");
                }
            }
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
                var returnValue = RtlSdrLibraryWrapper.rtlsdr_get_offset_tuning(_devicePointer);

                // If we got less than 0, there is an error.
                if (returnValue < 0)
                {
                    throw new RtlSdrLibraryExecutionException(
                        "Problem happened during reading the offset tuning mode of the device. " +
                        $"Error code: {returnValue}, device index: {DeviceInfo.Index}.");
                }

                // Return the value.
                return (OffsetTuningModes) returnValue;
            }
            set
            {
                // Set the new value on the device.
                var returnValue = RtlSdrLibraryWrapper.rtlsdr_set_offset_tuning(_devicePointer, (int) value);
                
                // If we did not get 0, there is an error.
                if (returnValue != 0)
                {
                    throw new RtlSdrLibraryExecutionException(
                        "Problem happened during setting offset tuning mode of the device. " +
                        $"Error code: {returnValue}, device index: {DeviceInfo.Index}.");
                }
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
            var returnCode = RtlSdrLibraryWrapper.rtlsdr_reset_buffer(_devicePointer);

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
        public void SetMaximumTunerGain()
        {
            TunerGain = SupportedTunerGains.Max();
        }

        /// <summary>
        /// Set the minimum supported tuner gain.
        /// </summary>
        public void SetMinimumTunerGain()
        {
            TunerGain = SupportedTunerGains.Min();
        }

        #endregion

        #region Close and Implementing IDispose and ToString

        internal void Close()
        {
            // Close the managed device.
            var returnCode = RtlSdrLibraryWrapper.rtlsdr_close(_devicePointer);

            // Other error was happened.
            if (returnCode != 0)
            {
                throw new RtlSdrLibraryExecutionException(
                    "Problem happened during closing the RTL-SDR device. " +
                    $"Error code: {returnCode}, device index: {_deviceIndex}.");
            }
        }

        /// <summary>
        /// Public implementation of Dispose pattern callable by consumers.
        /// </summary>
        /// <inheritdoc />
        public void Dispose()
        {
            // Check to see if Dispose has already been called.
            if (_disposed)
                return;
            
            // Stop async reading
            StopReadSamplesAsync();

            // Close the current device.
            Close();

            // Release the device context.
            if (_deviceContext.IsAllocated)
            {
                _deviceContext.Free();
            }

            // Release the device pointer.
            _devicePointer = IntPtr.Zero;

            // Set disposed to true.
            _disposed = true;

            // Sign for GC, that the object can be drop.
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Implement the destructor.
        /// </summary>
        ~RtlSdrManagedDevice()
        {
            Dispose();
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
}
