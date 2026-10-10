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
#
# Every health check reads through one library chosen for the purpose, never through the
# build just measured. A probe running on a defective build can die on stop and report
# nothing, and a build that cannot stream would make the device look worn out.

set -euo pipefail
source "$(dirname "${BASH_SOURCE[0]}")/hw-common.sh"

PATTERN="restart"
CYCLES=300
RUNS=2
ROUNDS=2
LIBRARIES=()
HEALTH_LIBRARY=""

while [[ $# -gt 0 ]]; do
    case "$1" in
        --library)        LIBRARIES+=("${2:-}"); shift 2 ;;
        --health-library) HEALTH_LIBRARY="${2:-}"; shift 2 ;;
        --pattern)        PATTERN="${2:-}"; shift 2 ;;
        --cycles)         CYCLES="${2:-}"; shift 2 ;;
        --runs)           RUNS="${2:-}"; shift 2 ;;
        --rounds)         ROUNDS="${2:-}"; shift 2 ;;
        --help)
            echo "test-compare.sh --library A --library B [--library C] --health-library H [options]"
            echo
            echo "  --health-library H  library every health check reads through (required);"
            echo "                      use a build known to stream and to stop cleanly"
            echo "  --pattern P         restart | reopen | openclose (default restart)"
            echo "  --cycles N          cycles per run (default 300)"
            echo "  --runs M            runs per library per round (default 2)"
            echo "  --rounds R          how many times to alternate (default 2)"
            echo
            echo "Include a build known to be broken. If it does not fail here, the test"
            echo "cannot detect the fault and a clean result from the others means nothing."
            echo
            echo "Stops at the first unhealthy check, since every run after it would measure"
            echo "a device that is not delivering. Exits 3 when it stopped early, 0 otherwise."
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

if [[ -z "$HEALTH_LIBRARY" ]]; then
    echo "Give --health-library. Without it the probe reads through whatever library is" >&2
    echo "selected, which may be the defective build under test or the system one." >&2
    exit 2
fi

# An unresolvable path falls through to the installed library without a word, so a run
# believed to measure one build would measure another.
for library in "${LIBRARIES[@]}" "$HEALTH_LIBRARY"; do
    if [[ ! -f "$library" ]]; then
        echo "No such library: $library" >&2
        exit 2
    fi
done

absolute() {
    echo "$(cd "$(dirname "$1")" && pwd)/$(basename "$1")"
}

HEALTH_LIBRARY="$(absolute "$HEALTH_LIBRARY")"

# Probe the device through the health library, whatever the measured build is.
health() {
    RTLSDR_LIBRARY_PATH="$HEALTH_LIBRARY" hw_health "$@"
}

hw_build

echo "Health library: $HEALTH_LIBRARY"

hw_heading "Device health before"

if ! health "before"; then
    echo
    echo "The device is not delivering, so nothing measured here would mean anything."
    echo "Replug the dongle and run again."
    exit 1
fi

declare -a RESULTS=()
STOPPED=""

for ((round = 1; round <= ROUNDS; round++)); do
    hw_heading "Round $round of $ROUNDS"

    for library in "${LIBRARIES[@]}"; do
        name="$(basename "$library")"
        export RTLSDR_LIBRARY_PATH="$(absolute "$library")"

        echo
        echo "  -- $name"

        status=0
        output="$(hw_tool HwStress --pattern "$PATTERN" --cycles "$CYCLES" --runs "$RUNS" \
            2>&1)" || status=$?
        summary="$(tail -1 <<< "$output")"

        # Each run's own line says how it ended, including what a managed fault reported. A
        # native crash that leaves no crash report is otherwise only a count in the summary.
        grep -E '^ +run [0-9]+:' <<< "$output" || true
        echo "  $summary"

        healthy=1
        health "health" 2 || healthy=0

        verdict=""

        # A starved run is only a finding once the device's state is known. If the health
        # library still streams, the device was fine and the build stopped delivering.
        if [[ $status -eq 3 ]]; then
            if [[ $healthy -eq 1 ]]; then
                verdict="  FAILED: starved while the device was healthy"
            else
                verdict="  inconclusive: starved, device not delivering"
            fi
        fi

        RESULTS+=("round $round  $name  ${summary#*=> }$verdict")

        if [[ $healthy -eq 0 ]]; then
            STOPPED="after $name in round $round"
            break 2
        fi
    done
done

hw_heading "Summary"
printf '  %s\n' "${RESULTS[@]}"

if [[ -n "$STOPPED" ]]; then
    echo
    echo "  INCOMPLETE  the health check $STOPPED did not report a healthy"
    echo "              device, so the session ended there. The last run's result is in"
    echo "              doubt, and nothing after it was measured. Replug before running again."
    exit 3
fi

echo
echo "  A build that never fails here has not been shown to be correct; it has been shown"
echo "  not to fail under this pattern, on this hardware, in this many runs."
