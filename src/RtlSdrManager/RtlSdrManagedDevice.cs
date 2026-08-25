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
/// <remarks>
/// After the device is disposed it can still be inspected but no longer operated. Exactly
/// four things keep working, so a cleanup or logging path is safe: <see cref="DeviceInfo"/>,
/// <see cref="ToString()"/>, <see cref="AsyncReadException"/> and
/// <see cref="DroppedSamplesCount"/>, plus reading back the configuration properties.
/// Everything else throws <see cref="ObjectDisposedException"/>, including
/// <see cref="TunerType"/>, which would otherwise answer from its cache and so depend on
/// whether it happened to be read earlier.
/// <para>
/// The class is not safe to use from several threads at once. Disposing it while another
/// thread is calling into it is undefined regardless of the checks described here.
/// </para>
/// </remarks>
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
    /// Cached tuner type. The tuner cannot change while the device is open, and the value is
    /// read on every center frequency change, so it is queried at most once.
    /// </summary>
    private TunerTypes? _tunerTypeCache;

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
    /// <remarks>
    /// Volatile because <see cref="Dispose()"/> writes it while callers read it through
    /// <see cref="ThrowIfDisposed"/> from their own threads. That buys a fresh read and
    /// nothing more: checking the flag and then using the device is two steps, and a
    /// disposal landing between them is caught by the device handle's own reference
    /// counting rather than by this flag. The class is still not safe to dispose while
    /// another thread is using it.
    /// </remarks>
    private volatile bool _disposed;

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
    /// Throw if the device has been disposed.
    /// </summary>
    /// <exception cref="ObjectDisposedException">Thrown when the device is disposed.</exception>
    /// <remarks>
    /// Applied only where the behavior after disposal would otherwise be wrong. Most members
    /// reach the device on the caller's thread and already fail with this exception, raised
    /// by the handle itself; guarding those would change only the reported object name.
    /// <para>
    /// Never call this from anything the native callback thread reaches.
    /// <c>ProcessSamplesFromCallback</c> reads <see cref="MaxAsyncBufferSize"/> and
    /// <see cref="DropSamplesOnFullBuffer"/> on that thread, so those getters are
    /// deliberately unguarded: an exception crossing the native boundary terminates the
    /// process. Their setters are guarded, which the callback never touches.
    /// </para>
    /// </remarks>
    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

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
    /// <remarks>
    /// Readable after the device is disposed, deliberately. This describes the device that was
    /// opened and cannot go stale, so logging it from a cleanup path is legitimate.
    /// </remarks>
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
    /// <remarks>
    /// Queried once and cached: the tuner is part of the device and cannot change while it is
    /// open. An unrecognized tuner is not cached, so the failure is reported on every access
    /// rather than being remembered.
    /// <para>
    /// Unavailable once the device is disposed, even though the value was cached. Answering
    /// from the cache would make the result depend on whether it happened to be read earlier.
    /// </para>
    /// </remarks>
    /// <exception cref="ObjectDisposedException">Thrown when the device is disposed.</exception>
    /// <exception cref="RtlSdrLibraryExecutionException">Thrown when the tuner is not recognized.</exception>
    public TunerTypes TunerType
    {
        get
        {
            // Guarded so the answer does not depend on call history: the cache would otherwise
            // keep serving a disposed device if the type had been read before, and fail if it
            // had not.
            ThrowIfDisposed();

            if (_tunerTypeCache != null)
            {
                return _tunerTypeCache.Value;
            }

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
            _tunerTypeCache = tunerType;
            return tunerType;
        }
    }

    /// <summary>
    /// Get the frequency ranges this device can currently reach.
    /// </summary>
    /// <remarks>
    /// These are the ranges <see cref="CenterFrequency"/> accepts right now, so the answer
    /// depends on <see cref="DirectSamplingMode"/>. With the tuner in circuit these are its
    /// ranges: most tuners report one, the FC2580 reports two with a gap between them. While
    /// direct sampling is active the tuner is bypassed and the single reported range is the
    /// ADC's instead.
    /// <para>
    /// Falling inside a range does not guarantee that this particular device will lock to a
    /// given frequency: some tuners have regions where that varies between individual devices,
    /// and those are reported by <see cref="TunerCapabilities.GetUnreliableRanges"/>.
    /// </para>
    /// </remarks>
    /// <exception cref="RtlSdrLibraryExecutionException">
    /// Thrown when the tuner is not recognized. Direct sampling does not use the tuner, so it
    /// reports its range even then.
    /// </exception>
    public IReadOnlyList<FrequencyRange> SupportedFrequencyRanges =>
        DirectSamplingMode != DirectSamplingModes.Disabled
            ? [DemodulatorCapabilities.GetDirectSamplingRange(CrystalFrequency.Rtl2832Frequency)]
            : TunerCapabilities.GetTunableRanges(TunerType);

    /// <summary>
    /// Explain a tuning failure when the cause is known, for appending to an error message.
    /// </summary>
    /// <param name="directSampling">Direct sampling mode in effect when the attempt was made.</param>
    /// <param name="frequency">The frequency that was refused.</param>
    /// <returns>An explanatory sentence, or an empty string when there is nothing to add.</returns>
    /// <remarks>
    /// The device writes its own diagnostics to the console, which callers often suppress, so
    /// anything a caller needs in order to act has to be part of the exception instead.
    /// </remarks>
    private string DescribeTuningFailure(DirectSamplingModes directSampling, Frequency frequency)
    {
        // Direct sampling does not fail this way: the value is written to a register without
        // any check, so a rejection here is not about the frequency.
        if (directSampling != DirectSamplingModes.Disabled)
        {
            return string.Empty;
        }

        return TunerCapabilities.IsUnreliable(TunerType, frequency)
            ? " The tuner could not lock to this frequency. It falls within " +
              $"{string.Join(" and ", TunerCapabilities.GetUnreliableRanges(TunerType))}, " +
              "where this tuner often cannot lock; the exact boundaries vary between " +
              "individual devices."
            : string.Empty;
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

            // 0 Hz is a real frequency while direct sampling is active, and it is where the
            // device sits until something else is chosen. With the tuner in circuit no tuner
            // reaches 0, so there the value means no frequency has been set, or the last
            // attempt failed. That is a usage problem rather than a device fault.
            if (returnValue == 0 && DirectSamplingMode == DirectSamplingModes.Disabled)
            {
                throw new InvalidOperationException(
                    "No center frequency has been set yet, so there is nothing to read. " +
                    $"Set CenterFrequency first. Device index: {DeviceInfo.Index}.");
            }

            // Return the value.
            return new Frequency(returnValue);
        }
        set
        {
            DirectSamplingModes directSampling = DirectSamplingMode;

            if (directSampling != DirectSamplingModes.Disabled)
            {
                // The tuner is out of circuit, so its coverage is irrelevant and its type is
                // deliberately not consulted: direct sampling works even where the tuner is
                // not recognized. The limit here is the ADC's, and it must be enforced,
                // because writing past it silently truncates instead of reporting an error.
                FrequencyRange samplingRange =
                    DemodulatorCapabilities.GetDirectSamplingRange(CrystalFrequency.Rtl2832Frequency);

                if (!samplingRange.Contains(value))
                {
                    throw new ArgumentOutOfRangeException(nameof(value), value,
                        "Problem happened during setting the center frequency of the device. " +
                        "Direct sampling is active, so the frequency is set on the ADC, which " +
                        $"covers {samplingRange}. Receive higher frequencies by aliasing: tune " +
                        "to the crystal frequency minus the wanted frequency.");
                }
            }
            else
            {
                TunerTypes tuner = TunerType;

                // Reject only what the tuner physically cannot reach. Regions where a tuner is
                // merely unreliable are left to the device: their boundaries vary between
                // individual devices, so a fixed check here would refuse frequencies that this
                // particular device handles perfectly well.
                if (!TunerCapabilities.IsTunable(tuner, value))
                {
                    throw new ArgumentOutOfRangeException(nameof(value), value,
                        "Problem happened during setting the center frequency of the device. " +
                        "The given frequency is outside the range supported by the tuner. " +
                        $"Supported: {TunerCapabilities.DescribeTunableRanges(tuner)}.");
                }
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
                        $"Error code: {returnValue}, device index: {DeviceInfo.Index}." +
                        DescribeTuningFailure(directSampling, value));
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
    /// Set and get the sample rate of the device.
    ///   225001 - 300000 Hz
    ///   900001 - 3200000 Hz
    ///   Sample loss is to be expected for rates more than 2400000 Hz.
    /// </summary>
    /// <remarks>
    /// A device reports no sample rate until one is set, so read this only after setting it.
    /// The value read back is the rate the device settled on, which is the closest it can
    /// reach to the one requested rather than the request itself.
    /// </remarks>
    /// <exception cref="InvalidOperationException">Thrown when no sample rate has been set yet.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the rate is outside the supported ranges.</exception>
    /// <exception cref="RtlSdrLibraryExecutionException">Thrown when the device rejects the rate.</exception>
    public Frequency SampleRate
    {
        get
        {
            // Get the value from the device.
            uint returnValue = LibRtlSdr.rtlsdr_get_sample_rate(_deviceHandle!);

            // The device holds no rate until one is written, and reports it as zero. No rate
            // in the supported ranges is zero, so this is unambiguous: it means nothing has
            // been set, which is a usage problem rather than a device fault.
            if (returnValue == 0)
            {
                throw new InvalidOperationException(
                    "No sample rate has been set yet, so there is nothing to read. " +
                    $"Set SampleRate first. Device index: {DeviceInfo.Index}.");
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
        rawGains is [0];

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
            // reading apart from "never set". Only reached when write succeeded.
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
    /// <remarks>
    /// Direct sampling bypasses the tuner and feeds the antenna straight to the ADC, which is
    /// how frequencies below the tuner's range are received. See
    /// <see cref="SupportedFrequencyRanges"/> for what <see cref="CenterFrequency"/> accepts
    /// while it is active.
    /// <para>
    /// Switching mode re-applies the current center frequency, which is usually meaningless in
    /// the mode being entered. Turning direct sampling on therefore resets the center
    /// frequency to 0 Hz whenever the current one is out of the ADC's reach, because the
    /// device would otherwise quietly receive the wrong frequency. Set
    /// <see cref="CenterFrequency"/> after switching, not before.
    /// </para>
    /// <para>
    /// Turning direct sampling off re-applies the current frequency to the tuner, which fails
    /// if it is one only the ADC could reach. The mode still changes; set a frequency the
    /// tuner supports afterward.
    /// </para>
    /// </remarks>
    /// <exception cref="RtlSdrLibraryExecutionException">Thrown when the mode cannot be set.</exception>
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
        set
        {
            // Changing mode re-applies the current center frequency through the path being
            // entered. Read it before the switch to decide whether that will be meaningful:
            // the raw call is used because 0 Hz is a legitimate answer here, not an error.
            uint currentFrequencyHz = LibRtlSdr.rtlsdr_get_center_freq(_deviceHandle!);
            bool enabling = value != DirectSamplingModes.Disabled;

            // Set the new value on the device.
            ExecuteWithSuppression(() =>
            {
                int returnValue = LibRtlSdr.rtlsdr_set_direct_sampling(_deviceHandle!, (int)value);

                // If we did not get 0, there is an error.
                if (returnValue != 0)
                {
                    // Turning direct sampling off re-tunes the tuner to the current frequency,
                    // which fails when that frequency is one only the ADC could reach. The
                    // mode has already changed by then, so rather than implying the
                    // whole operation was rejected.
                    string detail = !enabling
                        ? " The mode was changed, but the current center frequency could not " +
                          "be re-applied to the tuner. Set a frequency the tuner supports."
                        : string.Empty;

                    throw new RtlSdrLibraryExecutionException(
                        "Problem happened during setting direct sampling mode of the device. " +
                        $"Error code: {returnValue}, device index: {DeviceInfo.Index}.{detail}");
                }
            });

            if (!enabling)
            {
                return;
            }

            // Enabling re-applied the old frequency to the ADC. Anything beyond the ADC's
            // reach is silently truncated rather than refused, so the device would now be
            // receiving something other than what it reports. Reset to a defined 0 Hz instead.
            // Refusing to enable would be worse: the tuner cannot reach a low enough frequency
            // to satisfy the check, so the caller would have no way out.
            FrequencyRange samplingRange =
                DemodulatorCapabilities.GetDirectSamplingRange(CrystalFrequency.Rtl2832Frequency);

            if (!samplingRange.Contains(new Frequency(currentFrequencyHz)))
            {
                CenterFrequency = Frequency.FromHz(0u);
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
    /// Validate a GPIO pin number.
    /// </summary>
    /// <param name="gpio">The GPIO pin number to validate.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the pin is outside 0..7.</exception>
    /// <remarks>
    /// The range comes from the RTL2832U's GPO register, which is a single byte written as
    /// <c>1 &lt;&lt; gpio</c>, giving pins 0..7. It is a property of the demodulator and is
    /// therefore the same for every tuner.
    /// </remarks>
    internal static void ValidateGpioPin(int gpio)
    {
        if (gpio is < 0 or > 7)
        {
            throw new ArgumentOutOfRangeException(nameof(gpio), gpio,
                "The GPIO pin must be between 0 and 7.");
        }
    }

    /// <summary>
    /// Enable or disable the Bias Tee on the GPIO pin 0.
    /// </summary>
    /// <remarks>
    /// Equivalent to <see cref="SetBiasTeeGPIO"/> with GPIO pin 0, which is where the bias
    /// tee is wired on most dongles. Works on every supported tuner.
    /// <para>
    /// The bias tee stays powered when the device is closed, and after the process exits:
    /// nothing clears the pin on teardown. Call this with
    /// <see cref="BiasTeeModes.Disabled"/> before disposing the device if the attached
    /// hardware should not remain powered.
    /// </para>
    /// </remarks>
    /// <param name="mode">Enabled, Disabled</param>
    /// <exception cref="RtlSdrLibraryExecutionException">Thrown when the device rejects the request.</exception>
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
    /// </summary>
    /// <remarks>
    /// The GPIO pins belong to the demodulator, not to the tuner, so this works on every
    /// supported tuner. Most dongles wire the bias tee to pin 0; see
    /// <see cref="SetBiasTee"/> for that common case.
    /// <para>
    /// Two pins are reserved for internal use and should not be driven as a bias tee
    /// control: pin 4 is pulsed to reset the tuner while the device is opened, and pin 6
    /// selects the band filter on FC0012 tuners, where it is rewritten on every retune.
    /// They are not blocked here, matching the reference tooling, but choosing them will
    /// produce confusing behavior.
    /// </para>
    /// <para>
    /// The bias tee stays powered when the device is closed, and after the process exits:
    /// nothing clears the pin on teardown. Set the pin back to
    /// <see cref="BiasTeeModes.Disabled"/> before disposing the device if the attached
    /// hardware should not remain powered.
    /// </para>
    /// </remarks>
    /// <param name="gpio">The GPIO pin to configure as a Bias Tee control (0..7).</param>
    /// <param name="mode">Enabled, Disabled</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the GPIO pin is outside 0..7.</exception>
    /// <exception cref="RtlSdrLibraryExecutionException">Thrown when the device rejects the request.</exception>
    public void SetBiasTeeGPIO(int gpio, BiasTeeModes mode)
    {
        // The GPIO pins are on the demodulator, so no tuner check applies here.
        ValidateGpioPin(gpio);

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
        // NOTE: the equivalent guard was removed from SetBiasTeeGPIO, because GPIO pins
        // belong to the demodulator rather than the tuner. The same reasoning probably
        // applies here, but this method targets the KerberosSDR fork's entry point, which
        // is not available to verify against. The guard stays until it can be checked.
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
    /// <remarks>
    /// Reads only <see cref="DeviceInfo"/>, so it works on a disposed device and must keep
    /// doing so: debuggers call this while inspecting a variable, where a throwing override
    /// shows up as an unexplained error instead of a value.
    /// </remarks>
    public override string ToString()
    {
        return
            $"Index: {DeviceInfo.Index}; Name: {DeviceInfo.Name}; Manufacturer: {DeviceInfo.Manufacturer}; " +
            $"Type: {DeviceInfo.ProductType}; Serial: {DeviceInfo.Serial}";
    }

    #endregion
}
