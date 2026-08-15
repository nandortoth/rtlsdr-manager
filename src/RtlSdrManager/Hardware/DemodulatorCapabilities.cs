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

namespace RtlSdrManager.Hardware;

/// <summary>
/// Capabilities of the RTL2832U demodulator, which is common to every supported device.
/// </summary>
/// <remarks>
/// These are properties of the demodulator rather than of the tuner, so they do not change
/// with the tuner fitted to a device. Tuner-specific coverage lives in
/// <see cref="TunerCapabilities"/>.
/// </remarks>
public static class DemodulatorCapabilities
{
    /// <summary>
    /// Get the frequency range reachable in direct sampling mode.
    /// </summary>
    /// <param name="crystalFrequency">
    /// Crystal frequency clocking the RTL2832U, which is also its sampling rate. Read it from
    /// the device rather than assuming the usual 28.8 MHz: the value is adjustable, and any
    /// frequency correction is applied to it.
    /// </param>
    /// <returns>The reachable range, from 0 Hz to half the crystal frequency.</returns>
    /// <remarks>
    /// Direct sampling bypasses the tuner and feeds the antenna straight to the ADC, so the
    /// reachable range is the ADC's first Nyquist zone: DC up to half the sampling rate. With
    /// the usual 28.8 MHz crystal that is 0 to 14.4 MHz.
    /// <para>
    /// Higher frequencies are still receivable, but by aliasing rather than by tuning. A
    /// signal above half the sampling rate folds down, so it is received by tuning to the
    /// crystal frequency minus the wanted frequency. Asking for the higher frequency directly
    /// does not work, and does not report an error either: the value goes into a 22 bit
    /// register, so anything beyond this range is truncated and the device quietly receives
    /// something else.
    /// </para>
    /// </remarks>
    public static FrequencyRange GetDirectSamplingRange(Frequency crystalFrequency) =>
        new(Frequency.FromHz(0u), Frequency.FromHz(crystalFrequency.Hz / 2));
}
