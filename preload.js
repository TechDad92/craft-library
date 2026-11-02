// The only doorway between the screens and the computer. Each request is checked in main.js.
const { contextBridge, ipcRenderer } = require('electron');

contextBridge.exposeInMainWorld('craft', {
  info: () => ipcRenderer.invoke('app-info'),
  setRoots: (roots) => ipcRenderer.invoke('set-roots', roots),
  scan: (req) => ipcRenderer.invoke('scan', req),
  findDuplicates: (files) => ipcRenderer.invoke('find-duplicates', files),
  thumb: (p, kind) => ipcRenderer.invoke('thumb', p, kind),
  thumbSaved: (p, url) => ipcRenderer.invoke('thumb-saved', p, url),
  move: (src, destDir, base) => ipcRenderer.invoke('move', src, destDir, base),
  rename: (src, newBase) => ipcRenderer.invoke('rename', src, newBase),
  remove: (p) => ipcRenderer.invoke('remove', p),
  restore: (entry) => ipcRenderer.invoke('restore', entry),
  open: (p, exe) => ipcRenderer.invoke('open', p, exe),
  openWithDialog: (p) => ipcRenderer.invoke('open-with-dialog', p),
  showInFolder: (p) => ipcRenderer.invoke('show-in-folder', p),
  openRecycleBin: () => ipcRenderer.invoke('open-recycle-bin'),
  defaultApps: (exts) => ipcRenderer.invoke('default-apps', exts),
  pickFolder: (title, startIn) => ipcRenderer.invoke('pick-folder', title, startIn),
  pickProgram: () => ipcRenderer.invoke('pick-program'),
  systemFonts: () => ipcRenderer.invoke('system-fonts'),
  fontsInstalled: (files) => ipcRenderer.invoke('fonts-installed', files),
  installFont: (p) => ipcRenderer.invoke('install-font', p),
  uninstallFont: (p) => ipcRenderer.invoke('uninstall-font', p),
  fileUrl: (p) => 'craftfile://f/' + encodeURIComponent(p),
  onChange: (fn) => { ipcRenderer.on('fs-changed', () => fn()); }
});
