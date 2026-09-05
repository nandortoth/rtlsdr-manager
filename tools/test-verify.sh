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
# Run the verification harness, bracketed by health checks.
#
# This is the release gate. The brackets are not ceremony: the harness cannot tell a broken
# library from a degraded dongle, so a green run on unverified hardware means nothing, and a
# red one may be blaming the wrong thing.

set -euo pipefail
source "$(dirname "${BASH_SOURCE[0]}")/hw-common.sh"

LIBRARY=""
EXTRA=()

while [[ $# -gt 0 ]]; do
    case "$1" in
        --library) LIBRARY="${2:-}"; shift 2 ;;
        --help)
            echo "test-verify.sh [--library PATH] [extra HwVerify arguments]"
            echo
            echo "Runs HwVerify between two health checks. Exits nonzero if either the"
            echo "harness fails or the device was not delivering."
            exit 0
            ;;
        *) EXTRA+=("$1"); shift ;;
    esac
done

hw_build
[[ -n "$LIBRARY" ]] && hw_use_library "$LIBRARY"

hw_heading "Device health before"
hw_require_healthy "before"

hw_heading "Verification harness"
STATUS=0
hw_tool HwVerify "${EXTRA[@]+"${EXTRA[@]}"}" || STATUS=$?

hw_heading "Device health after"
# Reported rather than enforced: if the harness degraded the device, that is worth seeing
# next to the result, and it explains a failure that would otherwise look like a regression.
hw_health "after" || true

hw_heading "Result"

if [[ $STATUS -eq 0 ]]; then
    echo "  PASS  the harness completed with no failures"
else
    echo "  FAIL  the harness exited $STATUS"
fi

exit $STATUS
