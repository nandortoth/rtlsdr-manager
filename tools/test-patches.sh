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
# Check that every patch in patches/ still applies and still compiles.
#
# A vendored patch rots silently: upstream moves, or the branch it came from is rebased, and
# nothing says so until someone needs a patched build and finds it does not apply. This turns
# that into a failure at a time of our choosing.
#
# Each patch names its target in a leading comment, which git apply ignores:
#   # ref:  v2.0.3
#   # repo: https://github.com/steve-m/librtlsdr.git
#
# Needs network, and no hardware.

set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PATCH_DIR="$ROOT/patches"

if [[ "${1:-}" == "--help" ]]; then
    echo "test-patches.sh - check every patch in patches/ still applies and compiles."
    echo
    echo "Each patch names its target ref and repo in a leading '# ref:' / '# repo:' comment."
    echo "Exits nonzero if any patch fails. Needs network; needs no hardware."
    exit 0
fi

# One scratch directory for the whole run, removed on exit: the probe libraries are
# throwaway, and build-native.sh cleans up its clone but knows nothing about our output.
WORKDIR="$(mktemp -d)"
trap 'rm -rf "$WORKDIR"' EXIT

shopt -s nullglob
patches=("$PATCH_DIR"/*.diff)

if [[ ${#patches[@]} -eq 0 ]]; then
    echo "No patches in $PATCH_DIR; nothing to check."
    exit 0
fi

failed=0

for patch in "${patches[@]}"; do
    name="$(basename "$patch")"

    # Read only the header: sed quits at the first diff line, so a patch that happens to
    # modify a file containing "# ref:" cannot be misread as declaring one.
    # Defaults match tools/build-native.sh, so a patch without a header still checks.
    ref="$(sed -n '/^diff /q; s/^# *ref: *//p' "$patch" | head -1)"
    repo="$(sed -n '/^diff /q; s/^# *repo: *//p' "$patch" | head -1)"
    ref="${ref:-v2.0.3}"

    echo "=== $name (ref $ref) ==="

    args=(--ref "$ref" --patch "$patch" --output "$WORKDIR/$name.dylib")
    [[ -n "$repo" ]] && args+=(--repo "$repo")

    if "$ROOT/tools/build-native.sh" "${args[@]}" > "$WORKDIR/$name.log" 2>&1; then
        echo "  PASS  applies to $ref and compiles"
    else
        echo "  FAIL  see below"
        sed 's/^/        /' "$WORKDIR/$name.log" | tail -15
        failed=1
    fi
done

echo
if [[ $failed -eq 0 ]]; then
    echo "  PASS  every patch still applies"
else
    echo "  FAIL  at least one patch no longer applies; regenerate it from the branch it came from"
fi

exit $failed
