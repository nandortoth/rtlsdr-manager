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
and the process dies, with no opportunity for a managed caller to intervene.

The patch stops judging completion by what a cancel returns, and waits for the callbacks
instead:

1. It counts the transfers in flight: one more on every successful submit, one fewer when a
   transfer's callback runs.
2. Once a stop has begun, the callback does not resubmit, so no transfer goes back in flight
   uncanceled.
3. Every transfer is canceled once, then events are handled with a 100 ms timeout until the
   count reaches zero. The device-lost path waits the same way.
4. If 50 polls in a row bring no progress, the remaining transfers and their buffers are
   leaked rather than freed, because libusb may still write to them.

A requested stop makes `rtlsdr_read_async` return 0 rather than the result of the last
cancel, often `-5`. This library ignores the return value of a stop it requested, so that
changes nothing here.

This is [steve-m/librtlsdr#93](https://github.com/steve-m/librtlsdr/pull/93) by
wonderingStars, at commit `1fcd2c5`. It supersedes #90, the fix this project submitted, which
was closed in its favor: both stop the crash on macOS, and #93 also covers the device-lost path
and never frees a transfer that libusb may still complete. The measurements behind that choice
are in [the results comment on
#93](https://github.com/steve-m/librtlsdr/pull/93#issuecomment-6101393162).

**It is a snapshot, not a substitute for the pull request**: if upstream revises the change,
this file does not follow. Regenerate it from the pull request rather than treating it as the
source of truth, keeping the header comments at the top:

```bash
gh pr diff 93 -R steve-m/librtlsdr
```

Once a `librtlsdr` release carries the fix, this patch has served its purpose.

Consumers do not need this. It exists so the defect can be reproduced and the fix measured on
real hardware:

```bash
tools/build-native.sh --output /tmp/pristine.dylib
tools/build-native.sh --output /tmp/fixed.dylib \
    --patch patches/librtlsdr-async-cancel-fix.diff
tools/test-compare.sh --library /tmp/fixed.dylib --library /tmp/pristine.dylib \
    --health-library /tmp/fixed.dylib
```

On macOS the unpatched build crashed 6 times in about 6,800 restart cycles, unevenly and mostly
early in a session; the patched build crashed in none of 7,600. Because the crash rate varies
that much, compare builds within the same rounds, with the unpatched build in every one.
