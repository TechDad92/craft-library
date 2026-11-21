# Craft Library

A calm, friendly file manager for craft files: patterned papers, cut files (SVG, DXF, Silhouette),
Word documents that hold a paper image, PDFs, zip bundles and fonts. It's a file manager first,
with sorting tools added on.

It was made for my mum, who has thousands of craft files and wanted something simple, quiet and fully
under her control: nothing happens to a file unless she chooses it, and everything can be undone.

## Features

- Big, clear pictures of every file, including the paper inside Word documents, zip bundles, PDFs,
  Photoshop files and SVG cut files
- A "To sort" folder for new downloads, and a "Sort later" pile for anything that can wait
- Move, copy, rename and remove files safely, with Undo and Redo
- Finds exact duplicate files, even when their names are different
- Notes and colour labels on folders, notes on files
- Install font files with one click
- Lots of ways to make it comfortable: light or dark, colours, easy-reading fonts, text size, Focus mode,
  hiding kinds of files you'd rather not see
- A short setup guide the first time it opens

## Installing it

Download the newest `Craft-Library-Setup-<version>.exe` from the
[Releases page](https://github.com/TechDad92/craft-library/releases) and run it. It installs just for the
current Windows account (no administrator password), and puts **Craft Library** on the desktop and in the
Start menu. Needs Windows 10 (version 1809 or later) or Windows 11, 64-bit.

To update, run the newer installer: it installs over the top and keeps all settings.

Windows may show "Windows protected your PC" because the installer isn't signed: click **More info**,
then **Run anyway**.

## Support this project

Craft Library is free. If it's useful to you and you'd like to help it keep improving, you can support
its development through [GitHub Sponsors](https://github.com/sponsors/TechDad92). Thank you!

## What's in this folder

- `windows/` – the app (C# and WPF, .NET 10).
  - `CraftLibrary.Core/` – the file-handling rules: which folders are allowed, moving and renaming
    without ever overwriting, scanning folders, finding duplicates, saved settings, colours.
  - `CraftLibrary.App/` – the screens and the Windows parts (Recycle Bin, thumbnails, fonts, programs).
  - `CraftLibrary.Tests/` – automatic checks for the file-handling rules.
  - `installer/` – the installer script (NSIS).
  - `tools/` – scripts for trying the app under Wine on Linux.
- `docs/releases/` – the notes for each version; `CHANGELOG.md` – a short list of all versions.
- `legacy-electron/` – the first version (0.2.3, Electron), kept for reference.

## Safety rules built into the app

- It only reads or changes files inside the folders chosen in the app. Anything else is refused.
- It refuses Windows' own folders, Program Files, AppData, a whole C: drive or the whole user folder.
- Moving, copying or renaming never overwrites: if the name is taken, " (2)" is added.
- Moving to another drive copies first, checks the copy is the same size, and only then removes the original.
- "Send to Recycle Bin" uses the real Windows Recycle Bin. Portable and network drives have no
  Recycle Bin, so files from them go to a "Removed files (Craft Library)" folder at the top of that drive.
- Putting a file back never overwrites either: if a file with that name has appeared since, " (2)" is added.
- Portable drives are not watched for changes (so Windows can still safely remove them);
  the app checks them again whenever its window comes back to the front.

## Building it yourself

**On Windows:** install the [.NET 10 SDK](https://dotnet.microsoft.com/download) and
[NSIS](https://nsis.sourceforge.io), then double-click `windows\build.cmd`.
The installer appears in `windows\dist\`.

**On Linux:** `sudo apt install dotnet-sdk-10.0 nsis`, then run `windows/build.sh`.

Both scripts run the automatic checks first and stop if any fail.
To change the version number, edit `<Version>` in `windows/CraftLibrary.App/CraftLibrary.App.csproj`.

## Where the app keeps things

`%LOCALAPPDATA%\Craft Library\` holds the settings (`data.json`, with a backup copy), saved
thumbnails (`thumbs\`), and `problems.log` if anything unexpected ever goes wrong.

## Licence

Craft Library is free software: you can redistribute it and/or modify it under the terms of the
**GNU General Public License, version 3** (see [`LICENSE`](LICENSE)). You're welcome to fork it and make
your own version; if you share a changed version, share its code under the same licence.

It includes:
- the fonts Figtree, Atkinson Hyperlegible and Lexend, under the SIL Open Font License, and an older
  OpenDyslexic, under Creative Commons Attribution 3.0 and the Bitstream Vera font licence
  (licence files in `windows/CraftLibrary.App/Fonts/`);
- [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet) (MIT licence) and
  [SharpVectors](https://github.com/ElinamLLC/SharpVectors) (BSD 3-Clause licence).
