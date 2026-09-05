#!/usr/bin/env bash
#
# RTL-SDR Manager Library for .NET
# Copyright (C) 2018-2026 Nandor Toth <dev@nandortoth.com>
#
# This program is free software: you can redistribute it and/or modify
# it under the terms of the GNU General Public License as published by
# the Free Software Foundation, either version 3 of the License, or
# (at your option) any later version.
#
# This program is distributed in the hope that it will be useful,
# but WITHOUT ANY WARRANTY; without even the implied warranty of
# MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
# GNU General Public License for more details.
#
# You should have received a copy of the GNU General Public License
# along with this program.  If not, see http://www.gnu.org/licenses.
#
# Build a native librtlsdr to test against.
#
# The hardware tools all honor RTLSDR_LIBRARY_PATH, so a library built here can be
# substituted without installing anything system-wide. That closes the loop for native
# investigations: patch, rebuild, measure, on the actual hardware.
#
# The upstream project builds with cmake, which is not needed for this: compiling
# librtlsdr.c and the five tuner sources against libusb produces a usable library, and
# skipping cmake keeps this script short enough to read.

set -euo pipefail

REPO="https://github.com/steve-m/librtlsdr.git"
REF="v2.0.3"
SOURCE=""
OUTPUT=""
KEEP=0
PATCHES=()

usage() {
    cat <<'USAGE'
build-native.sh - build a native librtlsdr to test against.

  --output PATH   where to write the library (required)
  --ref REF       tag, branch or commit to build (default v2.0.3)
  --repo URL      repository to clone (default steve-m/librtlsdr)
  --source DIR    build this existing tree instead of cloning
  --patch FILE    apply a patch after checkout; may be repeated
  --keep          keep the clone and print where it is
  --help          print this text

Examples:
  tools/build-native.sh --output /tmp/pristine.dylib
  tools/build-native.sh --output /tmp/local.dylib --source ../librtlsdr

  # Patches ship in patches/, so no other checkout is needed.
  tools/build-native.sh --output /tmp/fixed.dylib \
      --patch patches/librtlsdr-async-cancel-fix.diff

Then point the hardware tools at it:
  RTLSDR_LIBRARY_PATH=/tmp/fixed.dylib dotnet run --project tools/HwStress
USAGE
}

while [[ $# -gt 0 ]]; do
    case "$1" in
        --output) OUTPUT="${2:-}"; shift 2 ;;
        --ref)    REF="${2:-}"; shift 2 ;;
        --repo)   REPO="${2:-}"; shift 2 ;;
        --source) SOURCE="${2:-}"; shift 2 ;;
        --patch)  PATCHES+=("${2:-}"); shift 2 ;;
        --keep)   KEEP=1; shift ;;
        --help)   usage; exit 0 ;;
        *)        echo "Unknown option: $1" >&2; usage >&2; exit 2 ;;
    esac
done

if [[ -z "$OUTPUT" ]]; then
    echo "--output is required." >&2
    usage >&2
    exit 2
fi

# Absolute, because the build runs from inside the source tree.
mkdir -p "$(dirname "$OUTPUT")"
OUTPUT="$(cd "$(dirname "$OUTPUT")" && pwd)/$(basename "$OUTPUT")"

WORKDIR=""

cleanup() {
    if [[ -n "$WORKDIR" && $KEEP -eq 0 && -d "$WORKDIR" ]]; then
        rm -rf "$WORKDIR"
    fi
}
trap cleanup EXIT

# --source builds a tree the caller owns, so patching it would edit their working copy
# behind their back. Refused rather than ignored: an option that silently does nothing is
# worse than one that fails.
if [[ -n "$SOURCE" && ${#PATCHES[@]} -gt 0 ]]; then
    echo "--patch cannot be combined with --source: patching would modify your tree." >&2
    echo "Either drop --source so a fresh clone is patched, or apply the patch yourself." >&2
    exit 2
fi

if [[ -n "$SOURCE" ]]; then
    TREE="$(cd "$SOURCE" && pwd)"
    echo "Source:  $TREE (existing tree, not modified)"
else
    WORKDIR="$(mktemp -d)"
    TREE="$WORKDIR/librtlsdr"
    echo "Cloning: $REPO at $REF"

    # Shallow, because only one revision is ever built here.
    git clone --quiet --depth 1 --branch "$REF" "$REPO" "$TREE"

    for patch in "${PATCHES[@]+"${PATCHES[@]}"}"; do
        if [[ ! -s "$patch" ]]; then
            # An empty file applies cleanly and changes nothing, so the build would be
            # pristine while reporting that it was patched. That is the one outcome this
            # script must never produce: it is a redirect from a failed git command away.
            echo "Patch is missing or empty: $patch" >&2
            echo "Check the command that produced it; an empty patch would build pristine." >&2
            exit 2
        fi

        echo "Patch:   $patch"
        git -C "$TREE" apply "$(cd "$(dirname "$patch")" && pwd)/$(basename "$patch")"
    done
fi

if [[ ! -f "$TREE/src/librtlsdr.c" ]]; then
    echo "No src/librtlsdr.c under $TREE; is that a librtlsdr checkout?" >&2
    exit 1
fi

# libusb moves around by platform and package manager, so ask pkg-config first and fall
# back to the usual Homebrew prefix rather than hardcoding one answer.
if command -v pkg-config >/dev/null 2>&1 && pkg-config --exists libusb-1.0; then
    USB_CFLAGS="$(pkg-config --cflags libusb-1.0)"
    USB_LIBS="$(pkg-config --libs libusb-1.0)"
elif [[ -d /opt/homebrew/include/libusb-1.0 ]]; then
    USB_CFLAGS="-I/opt/homebrew/include/libusb-1.0"
    USB_LIBS="-L/opt/homebrew/lib -lusb-1.0"
else
    echo "libusb-1.0 not found: install it, or make it visible to pkg-config." >&2
    exit 1
fi

case "$(uname -s)" in
    Darwin) SHARED_FLAG="-dynamiclib" ;;
    *)      SHARED_FLAG="-shared -fPIC" ;;
esac

CC="${CC:-clang}"

echo "Building with $CC ..."

# shellcheck disable=SC2086
"$CC" $SHARED_FLAG -O2 \
    -I"$TREE/include" $USB_CFLAGS \
    "$TREE/src/librtlsdr.c" \
    "$TREE/src/tuner_e4k.c" \
    "$TREE/src/tuner_fc0012.c" \
    "$TREE/src/tuner_fc0013.c" \
    "$TREE/src/tuner_fc2580.c" \
    "$TREE/src/tuner_r82xx.c" \
    $USB_LIBS \
    -o "$OUTPUT"

echo "Built:   $OUTPUT"

if [[ $KEEP -eq 1 && -n "$WORKDIR" ]]; then
    echo "Kept:    $TREE"
fi

echo
echo "Use it with:"
echo "  RTLSDR_LIBRARY_PATH=$OUTPUT dotnet run --project tools/HwHealth"
