#!/usr/bin/env bash

# Configuration
RTLSDR_CURRENTFOLDER=$(pwd)
RTLSDR_TEMPFOLDER=${RTLSDR_CURRENTFOLDER}"/publish"

# Set XTERM workaround for NuGet and Mono
tempterm=$TERM
export TERM=xterm

# Create temporary folder
if [ ! -d publish ]; then
    mkdir publish
fi

# Build the solution
dotnet publish -f netcoreapp2.0 -c Release ../RtlSdrManager/RtlSdrManager.csproj -o ${RTLSDR_TEMPFOLDER}"/netcoreapp2.0"
dotnet publish -f netcoreapp2.1 -c Release ../RtlSdrManager/RtlSdrManager.csproj -o ${RTLSDR_TEMPFOLDER}"/netcoreapp2.1"

# Define the NuGet alias
mono /usr/local/bin/nuget.exe pack RtlSdrManager.nuspec

# Delete the temporary folder
rm -rf publish

# Set back the original terminal
export TERM=${tempterm}
