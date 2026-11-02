# Craft Library — notes for Claude Code

A Windows desktop app (Electron) that helps the user's mum organise thousands of craft files:
patterned papers (JPG/PNG/PSD), SVG/DXF/EPS/Silhouette cut files, Word documents that hold a paper
image, PDFs, zip bundles and fonts. It's a file manager first, with sorting tools added on.
Current version: 0.2.3 (installed on mum's PC for her to try out).

## Who it's for: these rules matter
- The main user (the developer's mum) is very particular, and wants full control, a calm screen and simple visuals.
- **No suggestions**, e.g. never suggest file names. **Nothing automatic**: she decides every move or rename.
- Lots of customisation (Settings), but defaults stay calm and simple. Comfort toggles are off by default.
- Buttons are always the "stitched" style. No textures on buttons. The folder panel has no background pattern.
- Plain, friendly wording in the interface. No jargon.

## Safety principles (keep these)
- Only touch files inside folders she has chosen (`allowedRoots` in main.js). Refuse anything else.
- Refuse Windows/Program Files/AppData, a whole system drive, or the whole user folder.
- Never overwrite: add " (2)" etc.
- Moving across drives: copy, check the size matches, then delete the original.
- Remove = real Windows Recycle Bin. Portable/network drives instead move the file to
  `<drive>\Removed files (Craft Library)`.
- Undo works on the real files.
- Portable drives aren't watched (so Windows can still safely remove them). The app rescans when its window is focused.

## How it's built
- `main.js`: all file and Windows work, through IPC handlers (scan, move, rename, remove, restore,
  thumbnails, duplicates via quick hash then full SHA-1, fonts, open/open-with, default app names). Uses PowerShell
  (`-EncodedCommand`, data passed in the env var `CRAFT_DATA`) for Windows-only bits. Thumbnails: Windows shell
  thumbnail → docx/zip embedded image (JSZip) → PSD embedded JPEG; cached to `userData/thumbs`.
  Custom protocol `craftfile://f/<encoded path>` serves allowed files to the window.
- `preload.js`: exposes `window.craft` (the fixed list of allowed requests).
- `app/Main.dc.html` and `app/Thumb.dc.html`: the screens, written in the "design canvas" format
  (`{{ holes }}`, `<sc-if>`, `<sc-for>`, `<dc-import>`, `class Component extends DCLogic` with `renderVals()`).
  **Dual mode:** if `window.craft` exists it uses real files (`this.real`); otherwise sample data, so the same
  file still works as a clickable mockup on claude.ai. Keep both modes working.
- `app/dcrt.js`: a small runtime that renders those .dc.html files with React 18 (template caching,
  props-equality skip for imported components).
- `tools/prepare.js`: copies React, pdf.js and fonts into `app/`, and bundles the screens into `app/screens.js`.
  Runs automatically before start/build.
- State is saved in localStorage key `craft-library-data` (settings, locations, notes, colours, Sort later,
  removed-file list, custom programs).

## Commands (on Windows)
- `npm install`
- `npm start`: run the app without installing
- `npm run dist`: build the installer into `dist/` (electron-builder NSIS works natively on Windows)
- When bumping the version, change it in `package.json`. (`build/installer.nsi` and `tools/set-exe-icon.mjs`
  were only for building on Linux without wine, and carry their own copy of the version.)

## Performance features already in place
- Paging: 120 files are drawn at a time, with more loaded on scroll (`pageSize`, `showCount`).
- Separate scrolling panes in the desktop app (folders / files / preview), unless the preview is at the bottom.
- Rescans that find nothing changed are skipped. Temporary download files are ignored by the watcher.

## Not yet confirmed on real Windows
Put back from the Recycle Bin (PowerShell `undelete`), font install/uninstall, default program names, the
"Open with" dialog, the portable-drive "Removed files" folder, and shell thumbnails for PSD/EPS/Silhouette (via PowerToys).

## Next step
Mum is trying the app and will have a list of changes. Work through her feedback, keeping the rules above.
