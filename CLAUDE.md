# Craft Library — notes for Claude Code

A Windows desktop app that helps the user's mum organise thousands of craft files:
patterned papers (JPG/PNG/PSD), SVG/DXF/EPS/Silhouette cut files, Word documents that hold a paper
image, PDFs, zip bundles and fonts. It's a file manager first, with sorting tools added on.

Current version: **0.3.0**, a C# (WPF, .NET 10) rebuild of the 0.2.3 Electron app, for speed on low-end PCs.
The Electron version is in `legacy-electron/` for reference only. Do not change it.

## Who it's for: these rules matter
- Mum has ADHD, is very particular, and wants full control and simple visuals.
- **No suggestions**, e.g. never suggest file names. **Nothing automatic**: she decides every move or rename.
- Lots of customisation (Settings), but defaults stay calm and simple. Comfort toggles are off by default.
- Buttons are always the "stitched" style. No textures on buttons. The folder panel has no background pattern.
- Plain, friendly wording in the interface. No jargon. Error messages say what happened in plain words.
- It must be **snappy on low-end machines**: never block the screen's thread with file work.

## Safety principles (keep these; most are tested in CraftLibrary.Tests)
- Only touch files inside folders she has chosen (`SafeFiles.AllowedRoots`). Refuse anything else.
- Refuse Windows/Program Files/AppData, a whole system drive, or the whole user folder (`FolderRules`).
- Never overwrite: add " (2)" etc. (`SafeFiles.UniquePath`). Names are checked (`SafeFiles.CheckName`).
- Moving across drives: copy, check the size matches, then delete the original.
- Remove = real Windows Recycle Bin (`SHFileOperation` with `FOF_ALLOWUNDO` and `FOF_WANTNUKEWARNING`).
  Portable/network drives instead move the file to `<drive>\Removed files (Craft Library)`.
- Put back reads the Recycle Bin's own `$I`/`$R` files (`RecycleBinFiles`) and never overwrites.
- Undo works on the real files. Capitals-only renames go through a temporary name.
- Portable drives aren't watched (so Windows can still safely remove them). The app rescans when its window is activated.

## How it's built (`windows/`)
- `CraftLibrary.Core` (net10.0, no Windows-only code, fully testable on Linux): `SafeFiles`, `FolderRules`,
  `Scanner` (fast listing via `FileSystemEnumerable`), `Duplicates` (same size → quick hash of start+end →
  full SHA-1), `Remover` + `IPlatform`, `RecycleBinFiles`, `AppData`/`Settings` (JSON in
  `%LOCALAPPDATA%\Craft Library\data.json`, saved via temp file + `.bak`), `Palette` (all theme colours;
  `PaletteRules.g.cs` was generated from the original CSS), `FileKinds`.
- `CraftLibrary.App` (net10.0-windows10.0.19041, WPF, CommunityToolkit.Mvvm, SharpVectors for SVG):
  - `Services/`: `WindowsPlatform` (drive types, Recycle Bin, default program names via `AssocQueryString`,
    Open with via `SHOpenWithDialog`, File Explorer), `ThumbnailService`, `FontService`, `FolderWatcher`.
  - `ThumbnailService`: Windows shell thumbnail (`IShellItemImageFactory`, same cache as Explorer) first, then
    fallbacks: docx/zip biggest picture, PSD embedded JPEG, PDF via `Windows.Data.Pdf`, SVG via SharpVectors,
    plain image decode. Newest request first, 3 STA worker threads, disk cache in `thumbs\` (bump
    `CacheVersion` when changing how thumbnails are made), small in-memory cache. An all-empty shell bitmap
    counts as "no thumbnail".
  - `ViewModels/MainViewModel*.cs` (one class in three files: startup/scanning, what's on screen, actions),
    `SettingsViewModel`, `FileItemVm` (thumbnail is requested only when a tile's binding reads `Thumb`), `Rows.cs`.
  - `Views/`: Sidebar, Files (a virtualised ListBox of rows: heading, rows of tiles, list rows), Preview,
    Duplicates, Bin, Settings, Dialogs. `MainWindow.xaml` holds the banner, layout, toast and pop-out preview.
  - `Themes/`: `ThemeService` turns settings into `B.*` brushes, `F.*` fonts and sizes (use `DynamicResource`);
    `Styles.xaml` (stitched buttons `Btn.*`, fields, checks, scroll bars, drop-downs); `Icons.xaml` (line icons
    from the original design, 24×24, drawn by `Controls/Icon`).
  - `Fonts/`: Figtree, Atkinson Hyperlegible, Lexend, OpenDyslexic (TTF, OFL).
  - `App.xaml.cs`: single instance, unexpected errors go to `problems.log` and the app keeps going;
    `CRAFT_TRACE=1` writes binding problems to `bindings.log` (for testing).
- `CraftLibrary.Tests`: xUnit, runs on Linux (`dotnet test windows/CraftLibrary.Tests`).
- `installer/CraftLibrary.nsi`: per-user install to `%LOCALAPPDATA%\Programs\CraftLibrary`.

## Commands
- Tests: `dotnet test windows/CraftLibrary.Tests`
- Build installer: `windows/build.sh` (Linux: needs `dotnet-sdk-10.0` and `nsis` from apt) or `windows\build.cmd` (Windows).
- Version: only in `<Version>` in `CraftLibrary.App.csproj` (the build scripts read it from there).
- In a Claude Code cloud session the .NET download site is blocked; install the SDK with `apt-get install dotnet-sdk-10.0`.

## Trying it without Windows (Wine)
`apt install wine64 wine32:i386 xvfb xdotool imagemagick zip`, then:
`windows/tools/make-test-library.sh <prefix>` builds a test library, and
`windows/tools/run-in-wine.sh --build 55 shot1 "mousemove 182 223" "click 1"` runs the app and screenshots it
(Wine takes ~45 s to start). Wine limitations, all fine on real Windows: shell thumbnails are empty, Put back
can't find Wine's Recycle Bin, pop-ups show black corners, text spacing looks a little uneven.

## Not yet confirmed on real Windows
Everything: 0.3.0 has only been run under Wine. Specifically: shell thumbnails (incl. PSD/EPS via PowerToys),
Recycle Bin + Put back, font install/uninstall, default program names, Open with, PDF thumbnails,
portable-drive "Removed files" folder, speed on a low-end PC.

## Differences from 0.2.3
- Dropped: letter spacing (WPF can't space letters); leftover Sort mode, button-style and material code that had no screen.
- Fixed: capitals-only renames; Sort mode never moved real files (it had no screen anyway).
- New: keyboard (arrows, Enter, F2, Delete, Ctrl+Z, Ctrl+F, Esc); bigger preview picture; problem log.
- Settings start fresh (the Electron version kept them in its own browser storage).

## Next step
Mum is trying the app and will have a list of changes. Work through her feedback, keeping the rules above.
