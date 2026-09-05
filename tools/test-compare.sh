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
# Compare native builds by alternating them in one device state.
#
# Running all of A and then all of B does not work here. Device state drifts as a dongle is
# cycled, by enough to reverse a conclusion, so the builds have to be interleaved and the
# comparison repeated. Every wrong conclusion this project has recorded came from a single
# run of one build with no control beside it.

set -euo pipefail
source "$(dirname "${BASH_SOURCE[0]}")/hw-common.sh"

PATTERN="restart"
CYCLES=300
RUNS=2
ROUNDS=2
LIBRARIES=()

while [[ $# -gt 0 ]]; do
    case "$1" in
        --library) LIBRARIES+=("${2:-}"); shift 2 ;;
        --pattern) PATTERN="${2:-}"; shift 2 ;;
        --cycles)  CYCLES="${2:-}"; shift 2 ;;
        --runs)    RUNS="${2:-}"; shift 2 ;;
        --rounds)  ROUNDS="${2:-}"; shift 2 ;;
        --help)
            echo "test-compare.sh --library A --library B [--library C] [options]"
            echo
            echo "  --pattern P   restart | reopen | openclose (default restart)"
            echo "  --cycles N    cycles per run (default 300)"
            echo "  --runs M      runs per library per round (default 2)"
            echo "  --rounds R    how many times to alternate (default 2)"
            echo
            echo "Include a build known to be broken. If it does not fail here, the test"
            echo "cannot detect the fault and a clean result from the others means nothing."
            exit 0
            ;;
        *) echo "Unknown option: $1" >&2; exit 2 ;;
    esac
done

if [[ ${#LIBRARIES[@]} -lt 2 ]]; then
    echo "Give at least two --library paths; comparing one build against nothing" >&2
    echo "is what this script exists to prevent." >&2
    exit 2
fi

hw_build

hw_heading "Device health before"
hw_require_healthy "before"

declare -a RESULTS=()

for ((round = 1; round <= ROUNDS; round++)); do
    hw_heading "Round $round of $ROUNDS"

    for library in "${LIBRARIES[@]}"; do
        name="$(basename "$library")"
        export RTLSDR_LIBRARY_PATH="$(cd "$(dirname "$library")" && pwd)/$name"

        echo
        echo "  -- $name"

        summary="$(hw_tool HwStress --pattern "$PATTERN" --cycles "$CYCLES" --runs "$RUNS" \
            2>&1 | tail -1)" || true

        echo "  $summary"
        RESULTS+=("round $round  $name  ${summary#*=> }")

        hw_health "health" 2 || true
    done
done

hw_heading "Summary"
printf '  %s\n' "${RESULTS[@]}"

echo
echo "  A build that never fails here has not been shown to be correct; it has been shown"
echo "  not to fail under this pattern, on this hardware, in this many runs."
