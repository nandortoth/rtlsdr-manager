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
# Shared helpers for the test-*.sh scripts. Sourced, never run directly.

HW_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

# Build every tool once, so the scripts can use --no-build and a hardware run is never
# waiting on the compiler.
hw_build() {
    echo "Building tools ..."

    if ! dotnet build "$HW_ROOT" -v q --nologo >/dev/null; then
        echo "Build failed; nothing was measured." >&2
        exit 2
    fi
}

# Run one of the hardware tools.
#   hw_tool HwHealth --seconds 3
hw_tool() {
    local name="$1"
    shift

    dotnet run --project "$HW_ROOT/tools/$name" --no-build -- "$@"
}

# Report device health, printing the verdict with a label.
# Returns whatever HwHealth returned, so callers can decide what a failure means.
hw_health() {
    local label="$1"
    local seconds="${2:-3}"
    local output

    output="$(hw_tool HwHealth --seconds "$seconds" 2>&1)"
    local status=$?

    printf '  %-8s %s\n' "$label" "$output"

    return $status
}

# Report health and stop unless the device is delivering.
#
# Every hardware result in this project is only as good as the device that produced it: a
# dongle that has been cycled a few hundred times still opens and still reads once, while
# failing to sustain a stream, and measurements taken in that state have repeatedly looked
# like software faults. So a run that cannot confirm health does not continue.
hw_require_healthy() {
    local label="$1"

    if hw_health "$label"; then
        return 0
    fi

    echo
    echo "The device is not delivering, so nothing measured here would mean anything."
    echo "Replug the dongle and run again."
    exit 1
}

# Accept --library PATH from a script's arguments and point the tools at it.
hw_use_library() {
    local path="$1"

    if [[ ! -f "$path" ]]; then
        echo "No such library: $path" >&2
        exit 2
    fi

    export RTLSDR_LIBRARY_PATH="$(cd "$(dirname "$path")" && pwd)/$(basename "$path")"
    echo "Library: $RTLSDR_LIBRARY_PATH"
}

# Print a heading between phases of a run.
hw_heading() {
    echo
    echo "=== $* ==="
}
