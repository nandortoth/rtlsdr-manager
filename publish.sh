#!/bin/bash

# RTL-SDR Manager Publish Script
# This script publishes the NuGet packages (.nupkg and .snupkg) to NuGet.org
#
# Copyright (c) 2018-2026 Nandor Toth
#
# This program is free software: you can redistribute it and/or modify
# it under the terms of the GNU General Public License as published by
# the Free Software Foundation, either version 3 of the License, or
# (at your option) any later version.
#
# This program is distributed in the hope that it will be useful,
# but WITHOUT ANY WARRANTY; without even the implied warranty of
# MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
# GNU General Public License for more details.
#
# You should have received a copy of the GNU General Public License
# along with this program. If not, see <https://www.gnu.org/licenses/>.

set -e  # Exit on error

# Configuration
PROJECT_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ARTIFACTS_DIR="$PROJECT_ROOT/artifacts"
PACKAGES_DIR="$ARTIFACTS_DIR/packages"
NUGET_SOURCE="https://api.nuget.org/v3/index.json"
KEYCHAIN_SERVICE="nuget.org"
KEYCHAIN_ACCOUNT="rtlsdr-manager-publish"
OS="$(uname)"

# ── Functions ────────────────────────────────────────────────────────────────

usage() {
    echo "Usage: ./publish.sh [OPTIONS]"
    echo ""
    echo "Publishes the NuGet packages from artifacts/packages/ to NuGet.org."
    echo "Run ./build.sh first to create the packages."
    echo ""
    echo "Options:"
    echo "  --api-key <key>  Use an explicit NuGet API key for this run only"
    echo "  --help, -h       Show this help and exit"
    echo ""
    echo "API key resolution (in order):"
    echo "  1. --api-key <key>   explicit, this run only"
    echo "  2. macOS Keychain    (service: $KEYCHAIN_SERVICE, account: $KEYCHAIN_ACCOUNT)"
    echo "  3. NUGET_API_KEY     environment variable"
    echo "  If none is found, you are prompted and can save it to the Keychain."
    echo ""
    echo "Examples:"
    echo "  ./publish.sh                  # Publish using the stored/Keychain key"
    echo "  ./publish.sh --api-key <key>  # Publish with an explicit key"
}

# List the packages in PACKAGES_DIR as "  - relative/path (size)"
list_packages() {
    find "$PACKAGES_DIR" -type f \( -name "*.nupkg" -o -name "*.snupkg" \) | sort | while read -r file; do
        relative_path="${file#"$ARTIFACTS_DIR"/}"
        filesize=$(ls -lh "$file" | awk '{print $5}')
        echo "  - $relative_path ($filesize)"
    done
}

# ── Argument parsing ─────────────────────────────────────────────────────────

# Parse command-line arguments
#   --api-key <key>   explicit key (this run only)
API_KEY=""
KEY_SOURCE=""

while [ $# -gt 0 ]; do
    case "$1" in
        --api-key)
            if [[ -z "${2:-}" || "${2:-}" == -* ]]; then
                echo "ERROR: --api-key requires a value" >&2
                exit 1
            fi
            API_KEY="$2"
            KEY_SOURCE="--api-key argument"
            shift 2
            ;;
        --help|-h)
            usage
            exit 0
            ;;
        *)
            echo "ERROR: Unknown argument: $1" >&2
            echo "" >&2
            usage >&2
            exit 1
            ;;
    esac
done

# ── Main ─────────────────────────────────────────────────────────────────────

clear

echo "================================================"
echo "RTL-SDR Manager Publish Script"
echo "================================================"
echo ""

# Check prerequisites
echo "Checking prerequisites..."
if ! command -v dotnet > /dev/null 2>&1; then
    echo "ERROR: dotnet not found." >&2
    echo "Install the .NET SDK: https://dotnet.microsoft.com/download" >&2
    exit 1
fi
echo "✓ dotnet found"
echo ""

# Resolve the API key in order of precedence:
#   1. --api-key <key>   (explicit, this run only)
#   2. macOS Keychain    (only on macOS)
#   3. NUGET_API_KEY     (environment variable)

# 2. macOS Keychain (only on macOS)
if [ -z "$API_KEY" ] && [ "$OS" = "Darwin" ]; then
    if API_KEY="$(security find-generic-password -s "$KEYCHAIN_SERVICE" -a "$KEYCHAIN_ACCOUNT" -w 2>/dev/null)"; then
        KEY_SOURCE="macOS Keychain"
    else
        API_KEY=""
    fi
fi

# 3. NUGET_API_KEY environment variable
if [ -z "$API_KEY" ] && [ -n "${NUGET_API_KEY:-}" ]; then
    API_KEY="$NUGET_API_KEY"
    KEY_SOURCE="NUGET_API_KEY environment variable"
fi

# First-run setup: nothing found, so prompt for the key and offer to save it
if [ -z "$API_KEY" ]; then
    echo "No NuGet API key found."
    echo "Create one at: https://www.nuget.org/account/apikeys"
    echo ""
    read -r -s -p "Enter your NuGet API key: " API_KEY
    echo ""
    if [ -z "$API_KEY" ]; then
        echo "ERROR: No API key entered." >&2
        exit 1
    fi
    KEY_SOURCE="interactive prompt"
    echo ""

    if [ "$OS" = "Darwin" ]; then
        echo "Where should the key be saved?"
        echo "  1) macOS Keychain (secure, persistent)"
        echo "  2) Temporary (this run only)"
        read -r -p "Choose [1/2] (default: 2): " save_choice
        echo ""
        case "$save_choice" in
            1)
                if security add-generic-password \
                        -s "$KEYCHAIN_SERVICE" \
                        -a "$KEYCHAIN_ACCOUNT" \
                        -w "$API_KEY" \
                        -U 2>/dev/null; then
                    echo "✓ Saved to macOS Keychain (service: $KEYCHAIN_SERVICE, account: $KEYCHAIN_ACCOUNT)"
                else
                    echo "✗ Could not save to Keychain; using the key for this run only" >&2
                fi
                ;;
            *)
                echo "✓ Using the key for this run only"
                ;;
        esac
    else
        # Non-macOS: no Keychain. A child process cannot set the parent
        # shell's environment, so we can only use the key for this run and
        # offer a paste-ready export line for reuse in the current session.
        echo "✓ Using the key for this run only"
        echo "  To reuse it in this shell session, run:"
        echo "    export NUGET_API_KEY='$API_KEY'"
    fi
    echo ""
fi

echo "Using API key from: $KEY_SOURCE"
echo ""

# Validate that packages exist (run ./build.sh first if not)
if [ ! -d "$PACKAGES_DIR" ] || ! ls "$PACKAGES_DIR"/*.nupkg > /dev/null 2>&1; then
    echo "ERROR: No .nupkg files found in $PACKAGES_DIR" >&2
    echo "Run ./build.sh first to create the packages." >&2
    exit 1
fi

# Derive the package version from the .nupkg filename
# (e.g. RtlSdrManager.0.7.1.nupkg -> 0.7.1)
nupkgs=("$PACKAGES_DIR"/*.nupkg)
VERSION=$(basename "${nupkgs[0]}" .nupkg | sed -E 's/^.*\.([0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z.-]+)?)$/\1/')

# Show what will be published (the matching .snupkg is pushed automatically
# by NuGet.org alongside its .nupkg — no need to push it separately)
echo "Packages to publish:"
list_packages
echo ""

# Confirm before publishing (publishing to NuGet.org is irreversible)
read -r -p "Publish these packages to NuGet.org? [y/N] " confirm
if [[ ! "$confirm" =~ ^[Yy]$ ]]; then
    echo "Aborted."
    exit 0
fi
echo ""

# Push each package. Pushing the .nupkg automatically pushes the paired
# .snupkg symbol package. --skip-duplicate makes re-runs safe.
echo "Publishing to NuGet.org..."
echo ""
for package in "$PACKAGES_DIR"/*.nupkg; do
    echo "Pushing $(basename "$package")..."
    dotnet nuget push "$package" \
        --api-key "$API_KEY" \
        --source "$NUGET_SOURCE" \
        --skip-duplicate
    echo ""
done

# ── Summary ──────────────────────────────────────────────────────────────────

echo "================================================"
echo "PUBLISH SUMMARY"
echo "================================================"
echo ""
echo "Packages published successfully!"
echo "Version:      $VERSION"
echo "Source:       $NUGET_SOURCE"
echo ""
echo "Published:"
list_packages
echo ""
