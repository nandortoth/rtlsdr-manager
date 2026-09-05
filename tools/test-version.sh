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
# Check that the version agrees everywhere it is written down.
#
# A bump touches eight places across four files. Listing them, as the version-bump notes do,
# is not the same as checking them, and a half-finished bump otherwise surfaces on NuGet.
#
# The library csproj is the source of truth. The CHANGELOG date is deliberately not required:
# it is set when tagging, which is after this runs, so demanding it would fail every
# legitimate pre-release check. The date is reported, not enforced.
#
# Needs no hardware, no network, and no build.

set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
LIB_CSPROJ="$ROOT/src/RtlSdrManager/RtlSdrManager.csproj"
SAMPLES_CSPROJ="$ROOT/samples/RtlSdrManager.Samples/RtlSdrManager.Samples.csproj"

if [[ "${1:-}" == "--help" ]]; then
    echo "test-version.sh - check the version agrees everywhere it is written."
    echo
    echo "Reads <Version> from the library csproj and verifies the other seven places match."
    echo "Exits nonzero on any mismatch. Needs no hardware, network or build."
    exit 0
fi

xml_value() { sed -n "s/.*<$2>\(.*\)<\/$2>.*/\1/p" "$1" | head -1; }

VERSION="$(xml_value "$LIB_CSPROJ" Version)"

if [[ ! "$VERSION" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]]; then
    echo "  FAIL  could not read a version like 1.2.3 from $LIB_CSPROJ (got '$VERSION')"
    exit 1
fi

FOUR="$VERSION.0"
echo "Version under test: $VERSION  (four-part: $FOUR)"
echo

failed=0

check() {
    local what="$1" expected="$2" actual="$3"

    if [[ "$expected" == "$actual" ]]; then
        printf '  PASS  %s\n' "$what"
    else
        printf '  FAIL  %s\n        expected %s, found %s\n' "$what" "$expected" "${actual:-nothing}"
        failed=1
    fi
}

check_contains() {
    local what="$1" file="$2" pattern="$3"

    if grep -qE "$pattern" "$file"; then
        printf '  PASS  %s\n' "$what"
    else
        printf '  FAIL  %s\n        no line matching /%s/ in %s\n' \
               "$what" "$pattern" "${file#"$ROOT/"}"
        failed=1
    fi
}

check "library FileVersion"          "$FOUR"    "$(xml_value "$LIB_CSPROJ" FileVersion)"
check "library AssemblyVersion"      "$FOUR"    "$(xml_value "$LIB_CSPROJ" AssemblyVersion)"
check "samples Version"              "$VERSION" "$(xml_value "$SAMPLES_CSPROJ" Version)"
check "samples FileVersion"          "$FOUR"    "$(xml_value "$SAMPLES_CSPROJ" FileVersion)"
check "samples AssemblyVersion"      "$FOUR"    "$(xml_value "$SAMPLES_CSPROJ" AssemblyVersion)"
check "samples ProductVersion"       "$FOUR"    "$(xml_value "$SAMPLES_CSPROJ" ProductVersion)"

check_contains "release notes name this version" "$LIB_CSPROJ" "v${VERSION//./\\.}"
check_contains "README install example"          "$ROOT/README.md" \
    "PackageReference Include=\"RtlSdrManager\" Version=\"${VERSION//./\\.}\""
check_contains "CHANGELOG has a section"         "$ROOT/CHANGELOG.md" \
    "^## \[${VERSION//./\\.}\]"
check_contains "CHANGELOG summary table row"     "$ROOT/CHANGELOG.md" \
    "^\| \*\*${VERSION//./\\.}\*\* \|"
check_contains "CHANGELOG release-tag link"      "$ROOT/CHANGELOG.md" \
    "^\[${VERSION//./\\.}\]: .*tag/v${VERSION//./\\.}$"

echo
if grep -qE "^## \[${VERSION//./\\.}\] - UNRELEASED" "$ROOT/CHANGELOG.md"; then
    echo "  NOTE  the CHANGELOG section is still UNRELEASED; set its date and the summary"
    echo "        table row when tagging. Not a failure: the date is set at tag time."
fi

if [[ $failed -eq 0 ]]; then
    echo "  PASS  the version agrees everywhere"
else
    echo "  FAIL  the version does not agree everywhere; see above"
fi

exit $failed
