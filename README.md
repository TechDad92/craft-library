# Craft Library – project files

This folder holds everything needed to rebuild the Craft Library app.
You only need it if the design changes and you want to make a new installer.
To simply install the app, use the Setup .exe instead.

## What's in here

- `app/Main.dc.html` – the main screen (layout and how it behaves). It works two ways:
  inside the desktop app it uses real files; in the design canvas it uses sample files.
- `app/Thumb.dc.html` – the file thumbnails
- `app/dcrt.js` – the small engine that draws those two screens
- `app/index.html` – the page the app window opens
- `main.js` – does the real work with files and Windows (moving, renaming, Recycle Bin, thumbnails, fonts)
- `preload.js` – the short, fixed list of things the screens are allowed to ask `main.js` to do
- `tools/prepare.js` – gathers fonts and libraries into the app folder (runs automatically)
- `build/icon.ico` – the app icon

## Safety rules built into main.js

- The app only reads or changes files inside the folders chosen in the app. Anything else is refused.
- It refuses to work in Windows' own folders, Program Files, AppData, a whole C: drive or the whole user folder.
- Moving or renaming never overwrites: if the name is taken, " (2)" is added.
- Moving to another drive copies first, checks the copy is the same size, and only then removes the original.
- "Send to Recycle Bin" uses the real Windows Recycle Bin. Portable and network drives have no
  Recycle Bin, so files from them go to a "Removed files (Craft Library)" folder at the top of that drive.
- Portable drives are not watched for changes (so Windows can still safely remove them);
  the app checks them again whenever its window comes back to the front.

## Rebuilding the installer on a Windows computer

1. Install **Node.js** (the "LTS" version) from https://nodejs.org. Accept all the defaults.
2. Unzip this folder somewhere easy, like your Desktop.
3. Open the folder in File Explorer, click in the address bar at the top, type `cmd` and press **Enter**.
   A black Command Prompt window opens, already in the right folder.
4. Type `npm install` and press **Enter**. Wait until it finishes (a few minutes the first time).
5. To try the app without installing it, type `npm start` and press **Enter**. Close the window when done.
6. To make a new installer, type `npm run dist` and press **Enter**.
   The new Setup file appears in the `dist` folder.

## Updating the design

If the mockup changes, replace `app/Main.dc.html` (and `app/Thumb.dc.html` if it changed)
with the new versions, then repeat steps 5 and 6.
