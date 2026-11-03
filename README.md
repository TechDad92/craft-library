# Craft Library

A calm file manager for Mum's craft files: patterned papers, cut files (SVG, DXF, Silhouette),
Word documents that hold a paper image, PDFs, zip bundles and fonts. It's a file manager first,
with sorting tools added on.

## Installing it

Run `Craft-Library-Setup-<version>.exe`. It installs just for the current Windows account
(no administrator password), and puts **Craft Library** on the desktop and in the Start menu.
Windows 10 (version 1809 or later) or Windows 11, 64-bit.

If the old version (0.2.x) is installed, uninstall it first from
**Settings › Apps › Installed apps › Craft Library**. The two versions are installed in different
folders, but they share the same shortcut name. The new version starts fresh: choose the craft folder once
when it first opens.

## What's in this folder

- `windows/` – the app (C# and WPF, .NET 10). This is the current version.
  - `CraftLibrary.Core/` – the file-handling rules: which folders are allowed, moving and renaming
    without ever overwriting, scanning folders, finding duplicates, saved settings, colours.
  - `CraftLibrary.App/` – the screens and the Windows parts (Recycle Bin, thumbnails, fonts, programs).
  - `CraftLibrary.Tests/` – automatic checks for the file-handling rules.
  - `installer/` – the installer script (NSIS).
  - `tools/` – scripts for trying the app under Wine on Linux.
- `legacy-electron/` – the previous version (0.2.3, Electron). Kept for reference until the new
  version is confirmed on Mum's PC; it can then be deleted.

## Safety rules built into the app

- It only reads or changes files inside the folders chosen in the app. Anything else is refused.
- It refuses Windows' own folders, Program Files, AppData, a whole C: drive or the whole user folder.
- Moving or renaming never overwrites: if the name is taken, " (2)" is added.
- Moving to another drive copies first, checks the copy is the same size, and only then removes the original.
- "Send to Recycle Bin" uses the real Windows Recycle Bin. Portable and network drives have no
  Recycle Bin, so files from them go to a "Removed files (Craft Library)" folder at the top of that drive.
- Putting a file back never overwrites either: if a file with that name has appeared since, " (2)" is added.
- Portable drives are not watched for changes (so Windows can still safely remove them);
  the app checks them again whenever its window comes back to the front.

## Building a new installer

**On Windows:** install the [.NET 10 SDK](https://dotnet.microsoft.com/download) and
[NSIS](https://nsis.sourceforge.io), then double-click `windows\build.cmd`.
The installer appears in `windows\dist\`.

**On Linux:** `sudo apt install dotnet-sdk-10.0 nsis`, then run `windows/build.sh`.

Both scripts run the automatic checks first and stop if any fail.
To change the version number, edit `<Version>` in `windows/CraftLibrary.App/CraftLibrary.App.csproj`.

## Where the app keeps things

`%LOCALAPPDATA%\Craft Library\` holds her settings (`data.json`, with a backup copy), saved
thumbnails (`thumbs\`), and `problems.log` if anything unexpected ever goes wrong.
