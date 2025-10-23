#!/bin/bash

# RTL-SDR Manager Build Script
# This script builds the project, creates NuGet packages, and compiles binaries
#
# Copyright (c) 2018-2025 Nandor Toth
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
clear

# Configuration
PROJECT_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ARTIFACTS_DIR="$PROJECT_ROOT/artifacts"
PACKAGES_DIR="$ARTIFACTS_DIR/packages"
BINARIES_DIR="$ARTIFACTS_DIR/binaries"
CONFIGURATION="Release"

# Arrays to track created files
declare -a CREATED_FILES=()

echo "================================================"
echo "RTL-SDR Manager Build Script"
echo "================================================"
echo ""

# Clean artifacts directory
echo "Cleaning artifacts directory..."
if [ -d "$ARTIFACTS_DIR" ]; then
    rm -rf "$ARTIFACTS_DIR"
fi
mkdir -p "$PACKAGES_DIR"
mkdir -p "$BINARIES_DIR"
echo "✓ Directory is clean now"
echo ""

# Step 1: Restore dependencies
echo "Restoring NuGet packages..."
dotnet restore "$PROJECT_ROOT/RtlSdrManager.sln" > /dev/null 2>&1
echo "✓ Dependencies restored"
echo ""

# Step 2: Build the solution
echo "Building solution in $CONFIGURATION mode..."
dotnet build "$PROJECT_ROOT/RtlSdrManager.sln" \
    --configuration "$CONFIGURATION" \
    --no-restore \
    > /dev/null 2>&1
echo "✓ Solution built successfully"
echo ""

# Step 3: Create NuGet package for RtlSdrManager library
echo "Creating NuGet package..."
dotnet pack "$PROJECT_ROOT/src/RtlSdrManager/RtlSdrManager.csproj" \
    --configuration "$CONFIGURATION" \
    --no-build \
    --output "$PACKAGES_DIR" \
    > /dev/null 2>&1

# Find and record created NuGet packages
while IFS= read -r -d '' file; do
    CREATED_FILES+=("$file")
done < <(find "$PACKAGES_DIR" -type f \( -name "*.nupkg" -o -name "*.snupkg" \) -print0)

echo "✓ NuGet package created"
echo ""

# Step 4: Publish binaries for RtlSdrManager library
echo "Publishing RtlSdrManager library binaries..."
LIBRARY_OUTPUT_DIR="$BINARIES_DIR/RtlSdrManager"
dotnet publish "$PROJECT_ROOT/src/RtlSdrManager/RtlSdrManager.csproj" \
    --configuration "$CONFIGURATION" \
    --output "$LIBRARY_OUTPUT_DIR" \
    --no-build \
    > /dev/null 2>&1

# Record library binaries
while IFS= read -r -d '' file; do
    CREATED_FILES+=("$file")
done < <(find "$LIBRARY_OUTPUT_DIR" -type f -print0)

echo "✓ Library binaries published"
echo ""

# Step 5: Publish binaries for Samples application
echo "Publishing Samples application binaries..."
SAMPLES_OUTPUT_DIR="$BINARIES_DIR/Samples"
dotnet publish "$PROJECT_ROOT/samples/RtlSdrManager.Samples/RtlSdrManager.Samples.csproj" \
    --configuration "$CONFIGURATION" \
    --output "$SAMPLES_OUTPUT_DIR" \
    --no-build \
    > /dev/null 2>&1

# Record samples binaries
while IFS= read -r -d '' file; do
    CREATED_FILES+=("$file")
done < <(find "$SAMPLES_OUTPUT_DIR" -type f -print0)

echo "✓ Samples binaries published"
echo ""

# Step 6: Summary
echo "================================================"
echo "BUILD SUMMARY"
echo "================================================"
echo ""

echo "Build completed successfully!"
echo "Total files created: ${#CREATED_FILES[@]}"
echo "All artifacts are in: $ARTIFACTS_DIR"
echo ""

# NuGet packages section
echo "NuGet Packages:"
find "$PACKAGES_DIR" -type f | sort | while read -r file; do
    relative_path="${file#$ARTIFACTS_DIR/}"
    filesize=$(ls -lh "$file" | awk '{print $5}')
    echo "  - $relative_path ($filesize)"
done

# Library binaries section
echo ""
echo "Library Binaries:"
find "$LIBRARY_OUTPUT_DIR" -type f | sort | while read -r file; do
    relative_path="${file#$ARTIFACTS_DIR/}"
    filesize=$(ls -lh "$file" | awk '{print $5}')
    echo "  - $relative_path ($filesize)"
done

# Samples binaries section
echo ""
echo "Samples Application:"
find "$SAMPLES_OUTPUT_DIR" -type f | sort | while read -r file; do
    relative_path="${file#$ARTIFACTS_DIR/}"
    filesize=$(ls -lh "$file" | awk '{print $5}')
    echo "  - $relative_path ($filesize)"
done
