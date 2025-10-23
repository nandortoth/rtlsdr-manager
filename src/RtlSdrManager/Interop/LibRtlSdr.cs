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
//
// The source code uses "rtl-sdr" that was released under  GPLv2 license.
// The original work that may be used under the terms of that license,
// please see https://github.com/steve-m/librtlsdr.
//
//   rtl-sdr, turns your Realtek RTL2832 based DVB dongle into a SDR receiver
//   Copyright (C) 2012-2013 by Steve Markgraf <steve@steve-m.de>
//   Copyright (C) 2012 by Dimitri Stolnikov <horiz0n@gmx.net>
//
//   This program is free software: you can redistribute it and/or modify
//   it under the terms of the GNU General Public License as published by
//   the Free Software Foundation, either version 2 of the License, or
//   (at your option) any later version.
//
//   This program is distributed in the hope that it will be useful,
//   but WITHOUT ANY WARRANTY; without even the implied warranty of
//   MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
//   GNU General Public License for more details.
//
//   You should have received a copy of the GNU General Public License
//   along with this program.  If not, see http://www.gnu.org/licenses.

using System;
using System.Runtime.InteropServices;
using RtlSdrManager.Exceptions;
using RtlSdrManager.Hardware;

namespace RtlSdrManager.Interop;

/// <summary>
/// Wrapper class for RTL-SDR library.
/// </summary>
internal static partial class LibRtlSdr
{
    /// <summary>
    /// The default RTL-SDR library.
    /// </summary>
    private const string RtlSdrLibrary = "librtlsdr";

    /// <summary>
    /// Static constructor to set up native library resolution.
    /// </summary>
    static LibRtlSdr()
    {
        LibResolver.Register(typeof(LibRtlSdr).Assembly);
    }

    /// <summary>
    /// Delegate function for async reader.
    /// </summary>
    /// <param name="buf">Buffer to store samples.</param>
    /// <param name="len">Length of the buffer.</param>
    /// <param name="ctx">Device context.</param>
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal unsafe delegate void RtlSdrReadAsyncDelegate(byte* buf, uint len, IntPtr ctx);

    /// <summary>
    /// Get the amount of RTL-SDR devices on the system.
    /// </summary>
    /// <returns>Amount of RTL-SDR devices.</returns>
    [LibraryImport(RtlSdrLibrary, EntryPoint = "rtlsdr_get_device_count")]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    internal static partial uint rtlsdr_get_device_count();

    /// <summary>
    /// Get the name of the RTL-SDR device by its index on the system.
    /// </summary>
    /// <param name="index">Device index.</param>
    /// <returns>Name of the device.</returns>
    [LibraryImport(RtlSdrLibrary, EntryPoint = "rtlsdr_get_device_name")]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    internal static partial IntPtr rtlsdr_get_device_name(uint index);

    /// <summary>
    /// Get USB device strings.
    /// NOTE: The string arguments must provide space for up to 256 bytes.
    /// </summary>
    /// <param name="index">Device index.</param>
    /// <param name="manufact">Buffer for manufacturer name, may be NULL.</param>
    /// <param name="product">Buffer for product name, may be NULL.</param>
    /// <param name="serial">Buffer for serial number, may be NULL.</param>
    /// <returns>0 on success.</returns>
    [LibraryImport(RtlSdrLibrary, EntryPoint = "rtlsdr_get_device_usb_strings", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    internal static partial int rtlsdr_get_device_usb_strings(uint index,
        byte[]? manufact, byte[]? product, byte[]? serial);

    /// <summary>
    /// Open the RTL-SDR device for further usage (internal raw version).
    /// </summary>
    /// <param name="dev">Device pointer.</param>
    /// <param name="index">Device Index.</param>
    /// <returns>
    /// 0 on success.
    /// -1 if the device cannot be found.
    /// -6 if the device is already used.
    /// </returns>
    [LibraryImport(RtlSdrLibrary, EntryPoint = "rtlsdr_open")]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    private static partial int rtlsdr_open(out IntPtr dev, uint index);

    /// <summary>
    /// Open the RTL-SDR device for further usage.
    /// </summary>
    /// <param name="index">Device Index.</param>
    /// <param name="suppressConsoleOutput">If true, suppresses all console output from librtlsdr (internal use only).</param>
    /// <returns>SafeRtlSdrHandle on success.</returns>
    /// <exception cref="RtlSdrLibraryExecutionException">Thrown when device cannot be opened.</exception>
    internal static SafeRtlSdrHandle OpenDevice(uint index, bool suppressConsoleOutput = false)
    {
        int result = rtlsdr_open(out IntPtr rawHandle, index);
        if (result != 0)
        {
            throw new RtlSdrLibraryExecutionException(
                $"Failed to open RTL-SDR device at index {index}. Error code: {result}");
        }
        return new SafeRtlSdrHandle(rawHandle, ownsHandle: true);
    }

    /// <summary>
    /// Close the RTL-SDR device (internal - called by SafeHandle).
    /// </summary>
    /// <param name="dev">Device pointer.</param>
    /// <returns>0 on success.</returns>
    [LibraryImport(RtlSdrLibrary, EntryPoint = "rtlsdr_close")]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    internal static partial int rtlsdr_close(IntPtr dev);

    /// <summary>
    /// Set crystal oscillator frequencies used for the RTL2832 and the tuner IC.
    /// Usually both ICs use the same clock. Changing the clock may make sense if
    /// you are applying an external clock to the tuner or to compensate the
    /// frequency (and sample rate) error caused by the original (cheap) crystal.
    ///
    /// NOTE: Call this function only if you fully understand the implications.
    /// </summary>
    /// <param name="dev">Device handle.</param>
    /// <param name="rtlFreq">Frequency value used to clock the RTL2832 in Hz.</param>
    /// <param name="tunerFreq">frequency value used to clock the tuner IC in Hz.</param>
    /// <returns>0 on success.</returns>
    [LibraryImport(RtlSdrLibrary, EntryPoint = "rtlsdr_set_xtal_freq")]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    internal static partial int rtlsdr_set_xtal_freq(SafeRtlSdrHandle dev, uint rtlFreq, uint tunerFreq);

    /// <summary>
    /// Get crystal oscillator frequencies used for the RTL2832 and the tuner IC.
    /// Usually both ICs use the same clock.
    /// </summary>
    /// <param name="dev">Device pointer.</param>
    /// <param name="rtlFreq">Buffer for frequency value used to clock the RTL2832 in Hz.</param>
    /// <param name="tunerFreq">Buffer for frequency value used to clock the tuner IC in Hz.</param>
    /// <returns>0 on success.</returns>
    [LibraryImport(RtlSdrLibrary, EntryPoint = "rtlsdr_get_xtal_freq")]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    internal static partial int rtlsdr_get_xtal_freq(SafeRtlSdrHandle dev, out uint rtlFreq, out uint tunerFreq);

    /// <summary>
    /// Set frequency the device must be tuned to.
    /// </summary>
    /// <param name="dev">Device pointer.</param>
    /// <param name="freq">Frequency in Hz.</param>
    /// <returns>0 on success.</returns>
    [LibraryImport(RtlSdrLibrary, EntryPoint = "rtlsdr_set_center_freq")]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    internal static partial int rtlsdr_set_center_freq(SafeRtlSdrHandle dev, uint freq);

    /// <summary>
    /// Get actual frequency the device is tuned to.
    /// </summary>
    /// <param name="dev">Device pointer.</param>
    /// <returns>0 on error, frequency in Hz otherwise.</returns>
    [LibraryImport(RtlSdrLibrary, EntryPoint = "rtlsdr_get_center_freq")]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    internal static partial uint rtlsdr_get_center_freq(SafeRtlSdrHandle dev);

    /// <summary>
    /// Set the frequency correction value for the device.
    /// </summary>
    /// <param name="dev">Device pointer.</param>
    /// <param name="ppm">Correction value in parts per million (ppm).</param>
    /// <returns>0 on success.</returns>
    [LibraryImport(RtlSdrLibrary, EntryPoint = "rtlsdr_set_freq_correction")]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    internal static partial int rtlsdr_set_freq_correction(SafeRtlSdrHandle dev, int ppm);

    /// <summary>
    /// Get actual frequency correction value of the device
    /// </summary>
    /// <param name="dev">Device pointer.</param>
    /// <returns>Correction value in parts per million (ppm).</returns>
    [LibraryImport(RtlSdrLibrary, EntryPoint = "rtlsdr_get_freq_correction")]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    internal static partial int rtlsdr_get_freq_correction(SafeRtlSdrHandle dev);

    /// <summary>
    /// Set the bandwidth for the device.
    /// </summary>
    /// <param name="dev">Device pointer.</param>
    /// <param name="bw">Bandwidth in Hz. Zero means automatic BW selection.</param>
    /// <returns>0 on success.</returns>
    [LibraryImport(RtlSdrLibrary, EntryPoint = "rtlsdr_set_tuner_bandwidth")]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    internal static partial int rtlsdr_set_tuner_bandwidth(SafeRtlSdrHandle dev, uint bw);

    /// <summary>
    /// Get the tuner type.
    /// </summary>
    /// <param name="dev">Device pointer.</param>
    /// <returns>RTLSDR_TUNER_UNKNOWN on error, tuner type otherwise.</returns>
    [LibraryImport(RtlSdrLibrary, EntryPoint = "rtlsdr_get_tuner_type")]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    internal static partial TunerTypes rtlsdr_get_tuner_type(SafeRtlSdrHandle dev);

    /// <summary>
    /// Get a list of gains supported by the tuner.
    /// NOTE: The gains argument must be allocated by the caller. If NULL is
    /// being given instead, the number of available gain values will be returned.
    /// </summary>
    /// <param name="dev">Device pointer.</param>
    /// <param name="gains">Buffer for array of gain values. In tenths of a dB, 115 means 11.5 dB.</param>
    /// <returns>Less than 0 on error, number of available (returned) gain values otherwise</returns>
    [LibraryImport(RtlSdrLibrary, EntryPoint = "rtlsdr_get_tuner_gains")]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    internal static partial int rtlsdr_get_tuner_gains(SafeRtlSdrHandle dev, [In, Out] int[] gains);

    /// <summary>
    /// Set the gain for the device.
    /// Manual gain mode must be enabled for this to work.
    /// Valid gain values may be queried with rtlsdr_get_tuner_gains function.
    /// </summary>
    /// <param name="dev">Device pointer.</param>
    /// <param name="gain">Gain in tenths of a dB, 115 means 11.5 dB.</param>
    /// <returns>0 on success.</returns>
    [LibraryImport(RtlSdrLibrary, EntryPoint = "rtlsdr_set_tuner_gain")]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    internal static partial int rtlsdr_set_tuner_gain(SafeRtlSdrHandle dev, int gain);

    /// <summary>
    /// Get actual gain the device is configured to.
    /// </summary>
    /// <param name="dev">Device pointer.</param>
    /// <returns>0 on error, gain in tenths of a dB, 115 means 11.5 dB.</returns>
    [LibraryImport(RtlSdrLibrary, EntryPoint = "rtlsdr_get_tuner_gain")]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    internal static partial int rtlsdr_get_tuner_gain(SafeRtlSdrHandle dev);

    /// <summary>
    /// Set the gain mode (automatic/manual) for the device.
    /// Manual gain mode must be enabled for the gain setter function to work.
    /// </summary>
    /// <param name="dev">Device pointer.</param>
    /// <param name="manual">Gain mode, 1 means manual gain mode shall be enabled.</param>
    /// <returns>0 on success.</returns>
    [LibraryImport(RtlSdrLibrary, EntryPoint = "rtlsdr_set_tuner_gain_mode")]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    internal static partial int rtlsdr_set_tuner_gain_mode(SafeRtlSdrHandle dev, int manual);

    /// <summary>
    /// Enable or disable the internal digital AGC of the RTL2832.
    /// </summary>
    /// <param name="dev">Device pointer.</param>
    /// <param name="on">AGC mode, 1 means enabled, 0 disabled.</param>
    /// <returns>0 on success.</returns>
    [LibraryImport(RtlSdrLibrary, EntryPoint = "rtlsdr_set_agc_mode")]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    internal static partial int rtlsdr_set_agc_mode(SafeRtlSdrHandle dev, int on);

    /// <summary>
    /// Enable or disable the direct sampling mode. When enabled, the IF mode
    /// of the RTL2832 is activated, and rtlsdr_set_center_freq() will control
    /// the IF-frequency of the DDC, which can be used to tune from 0 to 28.8 MHz
    /// (crystal frequency of the RTL2832).
    /// </summary>
    /// <param name="dev">Device pointer.</param>
    /// <param name="on">0 means disabled, 1 I-ADC input enabled, 2 Q-ADC input enabled.</param>
    /// <returns>0 on success.</returns>
    [LibraryImport(RtlSdrLibrary, EntryPoint = "rtlsdr_set_direct_sampling")]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    internal static partial int rtlsdr_set_direct_sampling(SafeRtlSdrHandle dev, int on);

    /// <summary>
    /// Get state of the direct sampling mode.
    /// </summary>
    /// <param name="dev">Device pointer.</param>
    /// <returns>
    /// -1 on error
    /// 0 means disabled
    /// 1 I-ADC input enabled
    /// 2 Q-ADC input enabled
    /// </returns>
    [LibraryImport(RtlSdrLibrary, EntryPoint = "rtlsdr_get_direct_sampling")]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    internal static partial int rtlsdr_get_direct_sampling(SafeRtlSdrHandle dev);

    /// <summary>
    /// Enable or disable offset tuning for zero-IF tuners, which allows to avoid
    /// problems caused by the DC offset of the ADCs and 1/f noise.
    /// </summary>
    /// <param name="dev">Device pointer.</param>
    /// <param name="on">0 means disabled, 1 enabled.</param>
    /// <returns>0 on success.</returns>
    [LibraryImport(RtlSdrLibrary, EntryPoint = "rtlsdr_set_offset_tuning")]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    internal static partial int rtlsdr_set_offset_tuning(SafeRtlSdrHandle dev, int on);

    /// <summary>
    /// Get state of the offset tuning mode.
    /// </summary>
    /// <param name="dev">Device pointer.</param>
    /// <returns>
    /// -1 on error
    /// 0 means disabled
    /// 1 enabled
    /// </returns>
    [LibraryImport(RtlSdrLibrary, EntryPoint = "rtlsdr_get_offset_tuning")]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    internal static partial int rtlsdr_get_offset_tuning(SafeRtlSdrHandle dev);

    /// <summary>
    /// Set the sample rate for the device, also selects the base band filters
    /// according to the requested sample rate for tuners where this is possible.
    /// </summary>
    /// <param name="dev">Device pointer.</param>
    /// <param name="rate">
    /// The sample rate to be set, possible values are:
    ///   225001 - 300000 Hz
    ///   900001 - 3200000 Hz
    ///   Sample loss is to be expected for rates more than 2400000 Hz.
    /// </param>
    /// <returns>0 on success, -EINVAL on invalid rate.</returns>
    [LibraryImport(RtlSdrLibrary, EntryPoint = "rtlsdr_set_sample_rate")]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    internal static partial int rtlsdr_set_sample_rate(SafeRtlSdrHandle dev, uint rate);

    /// <summary>
    /// Get actual sample rate the device is configured to.
    /// </summary>
    /// <param name="dev">Device pointer.</param>
    /// <returns>0 on error, sample rate in Hz otherwise.</returns>
    [LibraryImport(RtlSdrLibrary, EntryPoint = "rtlsdr_get_sample_rate")]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    internal static partial uint rtlsdr_get_sample_rate(SafeRtlSdrHandle dev);

    /// <summary>
    /// Enable test mode that returns an 8 bit counter instead of the samples.
    /// The counter is generated inside the RTL2832.
    /// </summary>
    /// <param name="dev">Device pointer.</param>
    /// <param name="on">Test mode, 1 means enabled, 0 disabled.</param>
    /// <returns></returns>
    [LibraryImport(RtlSdrLibrary, EntryPoint = "rtlsdr_set_testmode")]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    internal static partial int rtlsdr_set_testmode(SafeRtlSdrHandle dev, int on);

    /// <summary>
    /// Reset the buffer of the device.
    /// </summary>
    /// <param name="dev">Device pointer.</param>
    /// <returns>0 on success.</returns>
    [LibraryImport(RtlSdrLibrary, EntryPoint = "rtlsdr_reset_buffer")]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    internal static partial int rtlsdr_reset_buffer(SafeRtlSdrHandle dev);

    /// <summary>
    /// Read samples from the device synchronously.
    /// </summary>
    /// <param name="dev">Device pointer.</param>
    /// <param name="buf">Buffer where data will be stored as int[].</param>
    /// <param name="len">Requested samples.</param>
    /// <param name="nRead">Received samples.</param>
    /// <returns>0 on success.</returns>
    [LibraryImport(RtlSdrLibrary, EntryPoint = "rtlsdr_read_sync")]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    internal static partial int rtlsdr_read_sync(SafeRtlSdrHandle dev, IntPtr buf, int len, out int nRead);

    /// <summary>
    /// Read samples from the device asynchronously. This function will block until
    /// it is being canceled using rtlsdr_cancel_async().
    /// </summary>
    /// <param name="dev">Device pointer.</param>
    /// <param name="cb">Callback function to return received samples.</param>
    /// <param name="ctx">User specific context to pass via the callback function.</param>
    /// <param name="bufNum">
    /// Optional buffer count, buf_num * buf_len = overall buffer size set to 0 for default buffer count (15).
    /// </param>
    /// <param name="bufLen">
    /// Optional buffer length, must be multiple of 512, should be a multiple of 16384 (URB size), set to 0
    /// for default buffer length (16 * 32 * 512).
    /// </param>
    /// <returns>0 on success.</returns>
    [LibraryImport(RtlSdrLibrary, EntryPoint = "rtlsdr_read_async")]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    internal static partial int rtlsdr_read_async(SafeRtlSdrHandle dev, RtlSdrReadAsyncDelegate cb,
        IntPtr ctx, uint bufNum, uint bufLen);

    /// <summary>
    /// Cancel all pending asynchronous operations on the device.
    /// </summary>
    /// <param name="dev">Device pointer.</param>
    /// <returns>0 on success.</returns>
    [LibraryImport(RtlSdrLibrary, EntryPoint = "rtlsdr_cancel_async")]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    internal static partial int rtlsdr_cancel_async(SafeRtlSdrHandle dev);

    /// <summary>
    /// Enable or disable the Bias Tee on GPIO pin 0.
    /// </summary>
    /// <param name="dev">Device pointer.</param>
    /// <param name="on">1 for Bias Tee on. 0 for Bias Tee off.</param>
    /// <returns>-1 if device is not initialized. 0 otherwise.</returns>
    [LibraryImport(RtlSdrLibrary, EntryPoint = "rtlsdr_set_bias_tee")]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    internal static partial int rtlsdr_set_bias_tee(SafeRtlSdrHandle dev, int on);

    /// <summary>
    /// Enable or disable the Bias Tee on the given GPIO pin.
    /// </summary>
    /// <param name="dev">Device pointer.</param>
    /// <param name="gpio">The GPIO pin to configure as a Bias Tee control.</param>
    /// <param name="on">1 for Bias Tee on. 0 for Bias Tee off.</param>
    /// <returns>-1 if device is not initialized. 0 otherwise.</returns>
    [LibraryImport(RtlSdrLibrary, EntryPoint = "rtlsdr_set_bias_tee_gpio")]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    internal static partial int rtlsdr_set_bias_tee_gpio(SafeRtlSdrHandle dev, int gpio, int on);

    /// <summary>
    /// Generic GPIO enable or disable.
    /// Can be used only with the modified RTL-SDR library for KerberosSDR:
    /// https://github.com/rtlsdrblog/rtl-sdr-kerberos/
    /// </summary>
    /// <param name="dev">Device pointer.</param>
    /// <param name="on">0 means disabled, 1 enabled.</param>
    /// <param name="gpio">Number of the GPIO pin to enable or disable.</param>
    /// <returns>
    /// -1 on error
    /// 0 on success
    /// </returns>
    [LibraryImport(RtlSdrLibrary, EntryPoint = "rtlsdr_set_gpio")]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    internal static partial int rtlsdr_set_gpio(SafeRtlSdrHandle dev, int on, int gpio);

    /// <summary>
    /// Enable or disable frequency dithering for R820T tuners. Fails for other tuners.
    /// Must be performed before setting the central frequency.
    /// Can be used only with the modified RTL-SDR library for KerberosSDR:
    /// https://github.com/rtlsdrblog/rtl-sdr-kerberos/
    /// </summary>
    /// <param name="dev">Device pointer.</param>
    /// <param name="dither">0 means disabled, 1 enabled.</param>
    /// <returns>0 on success.</returns>
    [LibraryImport(RtlSdrLibrary, EntryPoint = "rtlsdr_set_dithering")]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    internal static partial int rtlsdr_set_dithering(SafeRtlSdrHandle dev, int dither);
}
