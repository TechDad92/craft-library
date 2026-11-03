#!/bin/bash
# Builds Craft Library and its installer (on Linux; needs the .NET 10 SDK and NSIS: apt install dotnet-sdk-10.0 nsis).
# The finished installer appears in windows/dist/.
set -e
cd "$(dirname "$0")"
VERSION=$(grep -oP '(?<=<Version>)[^<]+' CraftLibrary.App/CraftLibrary.App.csproj)
echo "Building Craft Library $VERSION"
dotnet test CraftLibrary.Tests -nologo -v q
rm -rf publish && mkdir -p dist
dotnet publish CraftLibrary.App -nologo -v q -c Release -o publish
makensis -V2 -DVERSION="$VERSION" installer/CraftLibrary.nsi
ls -lh dist/Craft-Library-Setup-$VERSION.exe
