# RTL-SDR and Native-Interop Documentation Standards

Reference material for the `comment-reviewer` skill. Examples are drawn from this codebase
and from the upstream C source at `../rtl-sdr` (tag `v2.0.3`).

---

## 1. Units — the single most common source of confusion

This API converts units at nearly every boundary. Document the unit on **both** sides of
every conversion.

| Concept | librtlsdr unit | This library's unit |
|---|---|---|
| Frequency | `uint` Hz | `Frequency` (Hz internally, `.MHz` etc. accessors) |
| Tuner gain | `int` tenths of a dB (`115` = 11.5 dB) | `double` dB |
| Frequency correction | `int` PPM | `int` PPM (unchanged) |
| Sample counts | bytes (`len`, `buf_len`) | I/Q sample pairs (bytes ÷ 2) |
| I/Q components | unsigned byte, offset binary | `int` 0–255 via `IQData.I` / `.Q` |

**Always state the unit in `<param>` and `<returns>`:**

```csharp
/// <param name="gain">Gain in tenths of a dB, 115 means 11.5 dB.</param>
/// <returns>0 on error, gain in tenths of a dB.</returns>
```

**Sample-vs-byte boundaries deserve an explicit note**, because the factor of two is easy to
lose:

```csharp
/// <param name="requestedSamples">Amount of requested samples by one device read.
/// The byte size (requested samples * 2) must be a multiple of 512.</param>
```

**The I/Q offset-binary encoding should be stated wherever raw samples are exposed.** The
device centres its 8-bit output near 127; consumers must subtract it before any DSP.
Omitting this produces a huge false DC component that looks like a hardware fault.

---

## 2. Native error-code semantics

Never document a native return as merely "0 on success" when the failure codes carry
meaning. Cite what the upstream function actually does.

**Counter-intuitive cases in this codebase — all verified against `../rtl-sdr`:**

| Function | Code | Meaning |
|---|---|---|
| `rtlsdr_set_freq_correction` | `-2` | Value unchanged (`dev->corr == ppm`) — **not an error** (`librtlsdr.c:922`) |
| `rtlsdr_set_freq_correction` | `-3` | Crystal frequency read-back failed |
| `rtlsdr_cancel_async` | nonzero | Often `-5` on a *normal* cancel; only an error if the stop was not requested |
| `rtlsdr_read_sync` | `-8` | libusb `LIBUSB_ERROR_OVERFLOW` — device supplied more data than requested |
| `rtlsdr_get_tuner_gain` | `0` | Ambiguous: either an error **or** a legitimate 0.0 dB gain on R820T/R828D |
| `rtlsdr_get_center_freq` | `0` | Ambiguous: never tuned, last tune failed, or a legal 0 Hz in direct sampling |
| `rtlsdr_get_index_by_serial` | `-1`/`-2`/`-3` | NULL serial / no devices / no match |
| `rtlsdr_open` | `-1`/`-6` | Device not found / already in use |

**Where a code is ambiguous, say so** rather than implying the check is sound:

```csharp
/// <returns>
/// 0 on error, gain in tenths of a dB.
/// NOTE: 0 is also a valid gain for R820T/R828D (r82xx_gains starts at 0), so this
/// return value cannot by itself distinguish success from failure.
/// </returns>
```

---

## 3. Thread affinity

Anything reachable from `SamplesAvailableCallback` runs on the **native USB callback
thread**, not a managed thread-pool thread. Document it, and document what that implies.

```csharp
/// <summary>
/// Event to notify subscribers that new samples are available.
/// </summary>
/// <remarks>
/// Raised synchronously on the native USB callback thread. A slow handler blocks the
/// transfer pipeline and causes sample drops — hand work off to a queue and return.
/// An exception thrown by a handler is captured and stops the reading; it must never
/// escape to the native caller.
/// </remarks>
```

**Also document:**
- Fields written from the callback and read elsewhere (and what synchronization applies)
- Whether a member is safe to call while an asynchronous read is running
- Any single-writer / single-reader assumption (e.g. the `Channel` options in raw mode)

---

## 4. Lifetime and ownership

Three kinds of resource cross the managed/native line. Each needs an ownership statement.

**`SafeHandle`** — say who closes the device and when:
```csharp
/// Disposes the safe handle, which automatically calls rtlsdr_close.
```

**`GCHandle`** — say what it roots and why it must outlive the callback:
```csharp
/// Device context for the native callback. Allocated by StartReadSamplesAsync and
/// released by StopReadSamplesAsync: it intentionally roots the device only while the
/// native callback may use it. A permanent handle would prevent finalization.
```

**`ArrayPool` rentals** — say who returns the buffer, and that it is exactly once:
```csharp
/// The caller MUST call <see cref="RawSampleBuffer.Return"/> after processing.
/// Data.Length may exceed ByteLength because the array is rented from the pool.
```

**Deliberate leaks must be documented as deliberate**, with the reasoning — otherwise the
next reader "fixes" them into a crash:
```csharp
// On a Join timeout, deliberately leak the handle: freeing it under a live native
// callback would crash the process.
```

---

## 5. Tuner and fork applicability

Several members work only on particular hardware, or only against the KerberosSDR fork of
librtlsdr. State which, and *why* — an unexplained restriction is indistinguishable from a
bug (and at least one such restriction in this codebase is wrong).

```csharp
/// <summary>
/// Frequency dithering for R820T tuners.
/// Can be used only with the modified RTL-SDR library for KerberosSDR:
/// https://github.com/rtlsdrblog/rtl-sdr-kerberos/
/// </summary>
```

**Be precise about which chip owns a feature.** GPIO pins belong to the **RTL2832U** (an
8-bit `GPO` register, `librtlsdr.c:554-562`), not to the tuner. A comment attributing them
to the R820T is factually wrong, and such a comment has already led to an over-restrictive
runtime guard in `SetBiasTeeGPIO` — verify the claim against `../rtl-sdr` before repeating
it. Similarly:

- Gain tables are per-tuner (`librtlsdr.c:959-970`); FC2580 and unknown tuners genuinely
  have none.
- Frequency ranges are per-tuner, but are bypassed entirely in direct sampling mode.
- Bias tee works on any dongle; only the *fork's* generic `SetGPIO` is R820T-specific.

---

## 6. Upstream references

When behaviour is inherited from librtlsdr, cite it so the next reader can verify without
re-deriving. Prefer the function name over a bare line number, since upstream moves.

✅ `// librtlsdr silently falls back to the default buffer length if buf_len % 512 != 0`
✅ `// Matches rtlsdr_set_freq_correction, which returns -2 when the value is unchanged`
❌ `// see the C source`

For hardware behaviour that is not in librtlsdr, cite the osmocom wiki or the datasheet:
```csharp
// Check the frequency range (http://osmocom.org/projects/sdr/wiki/rtl-sdr).
```

---

## 7. Hot-path and allocation notes

The sample paths are performance-sensitive and have been optimized deliberately. Comments
should record the intent so it is not undone.

```csharp
// Construct and enqueue each I/Q sample in a single pass.
// Avoids the intermediate IQData[] allocation (GC pressure in the hot path).
```

Document when something is intentionally *not* the obvious implementation — a pooled rent,
a fast-path check that skips a lock, a struct chosen over a class:

```csharp
// Fast path: with suppression disabled (the default), do not touch the global lock.
// Every device property setter enters a scope, so locking here would serialize
// configuration across devices/threads for no reason.
```

---

## 8. Exception documentation

Every exception that can escape a public member needs an `<exception>` tag. This library
distinguishes three custom types plus BCL types, and the distinction is meaningful to
consumers:

| Type | Use for |
|---|---|
| `RtlSdrLibraryExecutionException` | A native librtlsdr call failed |
| `RtlSdrDeviceException` | Device-level problem (e.g. index does not exist) |
| `RtlSdrManagedDeviceException` | Managed-side problem with an open device (e.g. buffer full) |
| `InvalidOperationException` | Wrong state (reading a buffer before starting, `TunerGain` in AGC mode) |
| `ArgumentOutOfRangeException` | Caller passed an out-of-range value |
| `ObjectDisposedException` | Member used after `Dispose` |

`ArgumentOutOfRangeException` must be constructed with the **parameter name first** — a
past bug passed the message into the `paramName` slot. Check this whenever you see one.

---

## 9. File headers

Every `.cs` file carries the GPLv3 header block with the current copyright range
(`2018-2026`). Files derived from or wrapping upstream `rtl-sdr` code additionally carry
the upstream GPLv2 attribution — see `Interop/LibRtlSdr.cs` and `Interop/LibResolver.cs`
for the established form. A new file in `Interop/` that wraps upstream declarations should
carry it too.
