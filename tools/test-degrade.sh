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
# Drive a dongle into its degraded state and watch whether the health probe notices.
#
# This validates HwHealth against the condition it exists for. A dongle that has been cycled
# a few hundred times still enumerates, still opens, and still reads once, while no longer
# sustaining a stream. A check that opens a device and reads once calls that healthy, which
# is how a degraded dongle gets mistaken for a software fault.
#
# It deliberately leaves the hardware degraded. Replug when it finishes.

set -euo pipefail
source "$(dirname "${BASH_SOURCE[0]}")/hw-common.sh"

LIBRARY=""
BATCH=100
MAX_CYCLES=1000

while [[ $# -gt 0 ]]; do
    case "$1" in
        --library)    LIBRARY="${2:-}"; shift 2 ;;
        --batch)      BATCH="${2:-}"; shift 2 ;;
        --max-cycles) MAX_CYCLES="${2:-}"; shift 2 ;;
        --help)
            echo "test-degrade.sh [--library PATH] [--batch N] [--max-cycles N]"
            echo
            echo "Cycles the reopen pattern in batches, probing health after each, and stops"
            echo "when the probe reports UNHEALTHY. Use a patched library: the point is to"
            echo "degrade the device, not to crash the process."
            echo
            echo "Leaves the dongle degraded. Replug afterwards."
            exit 0
            ;;
        *) echo "Unknown option: $1" >&2; exit 2 ;;
    esac
done

hw_build
[[ -n "$LIBRARY" ]] && hw_use_library "$LIBRARY"

hw_heading "Baseline"
hw_require_healthy "cycles=0"

TOTAL=0
FLIPPED=0

while [[ $TOTAL -lt $MAX_CYCLES ]]; do
    hw_heading "Cycling $BATCH more (total will be $((TOTAL + BATCH)))"

    # A crash here is not the point and would end the batch early, so the outcome is
    # reported but not treated as fatal.
    hw_tool HwStress --pattern reopen --cycles "$BATCH" --runs 1 2>&1 | tail -1 || true

    TOTAL=$((TOTAL + BATCH))

    if hw_health "cycles=$TOTAL"; then
        continue
    fi

    FLIPPED=1
    break
done

hw_heading "Result"

if [[ $FLIPPED -eq 1 ]]; then
    echo "  PASS  the probe reported UNHEALTHY after $TOTAL cycles"
    echo "        HwHealth detects the degraded state, which is what it exists for."
else
    echo "  INCONCLUSIVE  the probe stayed HEALTHY through $TOTAL cycles"
    echo "        Either this hardware does not degrade this way, or the throughput"
    echo "        threshold sits below the degraded rate. Raise --max-cycles, or check"
    echo "        what the probe reported against a device known to be degraded."
fi

echo
echo "  The dongle is now in whatever state this left it. Replug before measuring anything."

exit 0
