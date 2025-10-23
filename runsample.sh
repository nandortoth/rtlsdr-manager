#!/bin/bash

# RTL-SDR Manager Sample Runner Script
# This script builds the project and runs the sample application
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
BUILD_SCRIPT="$PROJECT_ROOT/build.sh"
ARTIFACTS_DIR="$PROJECT_ROOT/artifacts"
SAMPLES_DIR="$ARTIFACTS_DIR/binaries/Samples"
SAMPLE_EXE="$SAMPLES_DIR/RtlSdrManager.Samples"

echo "================================================"
echo "RTL-SDR Manager Sample Runner"
echo "================================================"
echo ""

# Step 1: Check if build script exists
if [ ! -f "$BUILD_SCRIPT" ]; then
    echo "Error: build.sh not found at $BUILD_SCRIPT"
    exit 1
fi

# Step 2: Run the build script
echo "Running build script in the background..."
echo ""
bash "$BUILD_SCRIPT" > /dev/null 2>&1

# Step 3: Verify sample executable exists
echo "================================================"
echo "RUNNING SAMPLE APPLICATION"
echo "================================================"
echo ""

if [ ! -f "$SAMPLE_EXE" ]; then
    echo "Error: Sample executable not found at $SAMPLE_EXE"
    exit 1
fi

echo "Starting sample application from artifacts..."
echo "Executable: $SAMPLE_EXE"
echo ""

# Step 4: Run the sample application
dotnet "$SAMPLE_EXE.dll"

# Capture exit code
SAMPLE_EXIT_CODE=$?

echo ""
echo "================================================"

if [ $SAMPLE_EXIT_CODE -eq 0 ]; then
    echo "✓ Running sample is finished!"
else
    echo "✗ Sample exited with code: $SAMPLE_EXIT_CODE"
fi

echo "================================================"

exit $SAMPLE_EXIT_CODE
