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
# Cycle a device hard, bracketed by health checks.
#
# The verification harness performs few cycles, so a fault that fires with low probability
# passes it comfortably. Showing that a crash is present, or gone, needs enough cycles to
# make such a fault near-certain, which is what this runs.

set -euo pipefail
source "$(dirname "${BASH_SOURCE[0]}")/hw-common.sh"

LIBRARY=""
PATTERN="reopen"
CYCLES=100
RUNS=5

while [[ $# -gt 0 ]]; do
    case "$1" in
        --library) LIBRARY="${2:-}"; shift 2 ;;
        --pattern) PATTERN="${2:-}"; shift 2 ;;
        --cycles)  CYCLES="${2:-}"; shift 2 ;;
        --runs)    RUNS="${2:-}"; shift 2 ;;
        --help)
            echo "test-stress.sh [--library PATH] [--pattern P] [--cycles N] [--runs M]"
            echo
            echo "Runs HwStress between two health checks. Patterns: restart, reopen,"
            echo "openclose. Exits nonzero if any run crashed or stalled."
            echo
            echo "A clean result only means something if the same command against a build"
            echo "known to be broken does crash; otherwise the test cannot detect the fault."
            exit 0
            ;;
        *) echo "Unknown option: $1" >&2; exit 2 ;;
    esac
done

hw_build
[[ -n "$LIBRARY" ]] && hw_use_library "$LIBRARY"

hw_heading "Device health before"
hw_require_healthy "before"

hw_heading "Cycling: $PATTERN, $CYCLES cycles x $RUNS runs"
STATUS=0
hw_tool HwStress --pattern "$PATTERN" --cycles "$CYCLES" --runs "$RUNS" || STATUS=$?

hw_heading "Device health after"
# Worth seeing next to the result: this pattern is also what drives a dongle into the
# state where it opens but will not stream, and that changes what a later run means.
hw_health "after" || true

hw_heading "Result"

if [[ $STATUS -eq 0 ]]; then
    echo "  PASS  every run completed"
else
    echo "  FAIL  at least one run crashed or stalled"
fi

exit $STATUS
