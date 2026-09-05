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
# The release gate: everything that must hold before a version is tagged.
#
# This exists because the alternative is remembering several commands, in order, with the
# flags that matter: --no-incremental, because a repeated build does not re-report warnings
# and prints zero whether or not any exist, and the device health checks around the harness,
# because a worn dongle fails it in a way that looks like a code regression.
#
# What it verifies is that *this library* is releasable. A crash originating in librtlsdr is
# not a release blocker here: it is a documented limitation with a fix already submitted, and
# it fires at random, so testing against a stock native library would make the gate pass or
# fail by luck. The hardware step therefore runs against a known-good native layer, which the
# gate builds for itself from patches/ rather than asking the operator to remember to.
#
# The limitation of that is worth stating: this checks the library against a corrected
# librtlsdr, not against the one users currently have. That is the right question for a
# wrapper, and it is not end-to-end validation of what a user will experience.
#
# Needs a dongle and network. Use --skip-hardware or --skip-patches to run a subset, at the
# cost of the gate no longer being a release gate.

set -euo pipefail
source "$(dirname "${BASH_SOURCE[0]}")/hw-common.sh"

SKIP_HARDWARE=0
SKIP_PATCHES=0
LIBRARY=""

while [[ $# -gt 0 ]]; do
    case "$1" in
        --skip-hardware) SKIP_HARDWARE=1; shift ;;
        --skip-patches)  SKIP_PATCHES=1; shift ;;
        --library)       LIBRARY="${2:-}"; shift 2 ;;
        --help)
            echo "test-release.sh [--skip-hardware] [--skip-patches] [--library PATH]"
            echo
            echo "Runs everything that must pass before tagging a release:"
            echo "  1. The version agrees everywhere it is written"
            echo "  2. Debug build, no warnings   (--no-incremental, so warnings are re-reported)"
            echo "  3. Release build, no warnings"
            echo "  4. Unit tests"
            echo "  5. Vendored patches still apply and compile   (needs network)"
            echo "  6. Hardware harness between health checks     (needs a dongle)"
            echo
            echo "Step 6 runs against a native library the gate builds itself from patches/,"
            echo "so a librtlsdr defect cannot fail a release of this library. That means it"
            echo "verifies this library against a corrected native layer, not against the one"
            echo "users currently have."
            echo
            echo "Exits nonzero on the first failure. Skipping a step means the result is no"
            echo "longer a release gate; the summary says which steps ran."
            echo
            echo "--library overrides the built library, for checking against something else."
            exit 0
            ;;
        *) echo "Unknown option: $1" >&2; exit 2 ;;
    esac
done

RAN=()
FAILED=""

WORKDIR="$(mktemp -d)"
trap 'rm -rf "$WORKDIR"' EXIT

fail() {
    FAILED="$1"
    echo
    echo "  FAIL  $1"
    echo
    echo "Steps that ran: ${RAN[*]:-none}"
    echo "This is not a releasable state."
    exit 1
}

# Builds and refuses to continue if the compiler reported anything at all.
# --no-incremental is the point: without it a build that has nothing to do prints
# "0 Warning(s)" regardless of whether warnings exist.
build_clean() {
    local config="$1"
    local log
    log="$(mktemp)"

    if ! dotnet build -c "$config" --no-incremental --nologo > "$log" 2>&1; then
        sed 's/^/        /' "$log" | tail -20
        fail "the $config build did not succeed"
    fi

    local warnings
    warnings="$(grep -oE '[0-9]+ Warning\(s\)' "$log" | head -1 | grep -oE '^[0-9]+' || echo 0)"

    if [[ "$warnings" != "0" ]]; then
        grep -E "warning " "$log" | sed 's/^/        /' | head -15
        fail "the $config build produced $warnings warning(s); zero is the standard"
    fi

    echo "  PASS  $config builds with no warnings"
    RAN+=("$config-build")
}

hw_heading "1. Version consistency"
if "$HW_ROOT/tools/test-version.sh" > "$WORKDIR/version.log" 2>&1; then
    echo "  PASS  the version agrees everywhere"
    RAN+=(version)
else
    sed 's/^/        /' "$WORKDIR/version.log"
    fail "the version does not agree everywhere it is written"
fi

hw_heading "2. Debug build"
build_clean Debug

hw_heading "3. Release build"
build_clean Release

hw_heading "4. Unit tests"
if dotnet test -v q --nologo > "$WORKDIR/tests.log" 2>&1; then
    echo "  PASS  $(grep -oE 'Passed:[[:space:]]*[0-9]+' "$WORKDIR/tests.log" | head -1) "
    RAN+=(tests)
else
    tail -20 "$WORKDIR/tests.log" | sed 's/^/        /'
    fail "the unit tests did not pass"
fi

hw_heading "5. Vendored patches"
if [[ $SKIP_PATCHES -eq 1 ]]; then
    echo "  SKIP  --skip-patches was passed"
else
    if "$HW_ROOT/tools/test-patches.sh" > "$WORKDIR/patches.log" 2>&1; then
        echo "  PASS  every patch still applies and compiles"
        RAN+=(patches)
    else
        sed 's/^/        /' "$WORKDIR/patches.log" | tail -15
        fail "a vendored patch no longer applies"
    fi
fi

hw_heading "6. Hardware verification"
if [[ $SKIP_HARDWARE -eq 1 ]]; then
    echo "  SKIP  --skip-hardware was passed"
else
    # The gate provisions its own native layer rather than asking the operator to remember
    # one. Anything else means a librtlsdr defect can fail a release of this library, at
    # random, and that a mistyped path silently falls back to the system copy.
    if [[ -z "$LIBRARY" ]]; then
        echo "  ....  building a corrected librtlsdr to verify against"

        if "$HW_ROOT/tools/build-native.sh" --output "$WORKDIR/native.dylib" \
                --patch "$HW_ROOT/patches/librtlsdr-async-cancel-fix.diff" \
                > "$WORKDIR/native.log" 2>&1; then
            LIBRARY="$WORKDIR/native.dylib"
            echo "  PASS  built a corrected native library for this run"
        else
            sed 's/^/        /' "$WORKDIR/native.log" | tail -15
            fail "could not build a native library to verify against"
        fi
    else
        echo "  NOTE  using the library given with --library: $LIBRARY"
    fi

    verify_args=(--library "$LIBRARY")

    if "$HW_ROOT/tools/test-verify.sh" "${verify_args[@]}" \
            > "$WORKDIR/verify.log" 2>&1; then
        echo "  PASS  $(grep -oE 'passed=[0-9]+ failed=[0-9]+ skipped=[0-9]+' "$WORKDIR/verify.log" | head -1)"
        RAN+=(hardware)
    else
        sed 's/^/        /' "$WORKDIR/verify.log" | tail -20

        # A signal rather than a check failure means the process was killed, which on a stock
        # librtlsdr is the known use-after-free rather than anything about this release.
        if grep -qE "harness exited (139|134)" "$WORKDIR/verify.log"; then
            echo
            echo "        The harness was killed by a signal, not failed by a check. That is"
            echo "        the native use-after-free, reached by the close-and-reopen the"
            echo "        harness performs, and it means the library in use is not patched."
            echo "        Drop --library and the gate will build a corrected one itself."
        fi

        fail "the hardware harness did not pass"
    fi
fi

hw_heading "Result"

if [[ $SKIP_HARDWARE -eq 1 || $SKIP_PATCHES -eq 1 ]]; then
    echo "  INCOMPLETE  everything that ran passed, but a step was skipped."
    echo "              Steps that ran: ${RAN[*]}"
    echo "              Run without --skip-* before tagging."
    exit 0
fi

echo "  PASS  version, build, tests, patches and hardware all clean"
echo "        Remaining before tagging: set the CHANGELOG date and its summary row."
exit 0
