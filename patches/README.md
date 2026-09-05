# Native patches

Patches against the native `librtlsdr`, kept here so the hardware tools can be pointed at a
fixed build without needing a checkout of anything outside this repository.

Apply one with `tools/build-native.sh`, which clones upstream, patches it, and produces a
library the tools honor through `RTLSDR_LIBRARY_PATH`:

```bash
tools/build-native.sh --output /tmp/fixed.dylib \
    --patch patches/librtlsdr-async-cancel-fix.diff
```

## `librtlsdr-async-cancel-fix.diff`

Applies to **v2.0.3**, the default ref of `build-native.sh`.

Fixes a use-after-free in `rtlsdr_read_async`: the cancel loop treats a failed
`libusb_cancel_transfer` as though the transfer had finished, so the transfer buffers can be
released while transfers are still in flight. A late completion then writes into freed memory
and the process dies, with no opportunity for a managed caller to intervene. Two commits:

1. Apply the transfer-cancel settle delay on all platforms. The pause that lets a cancellation
   status propagate is inside an `#ifdef _WIN32`, so elsewhere there is none.
2. Don't treat a failed transfer cancel as completion. A cancel that failed does not mean the
   transfer finished, so keep waiting for it, bounded so a transfer that never reports a
   terminal status cannot block the caller.

Submitted upstream to `steve-m/librtlsdr` from `nandortoth/librtlsdr`, branch
`fix-async-cancel-use-after-free`. **It is a snapshot, not a substitute for the pull request**:
if upstream revises the change, this file does not follow, so regenerate it from the branch
rather than treating it as the source of truth.

Consumers do not need this. It exists so the defect can be reproduced and the fix measured on
real hardware:

```bash
tools/build-native.sh --output /tmp/pristine.dylib
tools/build-native.sh --output /tmp/fixed.dylib \
    --patch patches/librtlsdr-async-cancel-fix.diff
tools/test-compare.sh --library /tmp/fixed.dylib --library /tmp/pristine.dylib
```

Pristine crashes within a few hundred cycles; the patched build does not.
