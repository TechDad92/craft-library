// Craft Library — desktop app.
// This side of the app does all the real work with files and Windows.
// The screens ask for things through a small, fixed set of requests (see preload.js),
// and every request is checked so the app only ever touches folders the person has chosen.

const { app, BrowserWindow, ipcMain, dialog, shell, nativeImage, protocol, net } = require('electron');
const path = require('path');
const fs = require('fs');
const fsp = fs.promises;
const crypto = require('crypto');
const { spawn } = require('child_process');
const { pathToFileURL } = require('url');

const IS_WIN = process.platform === 'win32';
const REMOVED_FOLDER = 'Removed files (Craft Library)';
const SKIP_DIRS = new Set(['$recycle.bin', 'system volume information', 'node_modules', '.git', REMOVED_FOLDER.toLowerCase()]);
const INBOX_SKIP_EXT = new Set(['exe', 'msi', 'dll', 'lnk', 'ini', 'tmp', 'crdownload', 'partial', 'part', 'download', 'sys', 'bat', 'cmd']);
const MAX_FILES = 60000;

let win = null;
let allowedRoots = [];      // folders the person has chosen (locations + the sorting folder)
let watchers = [];

protocol.registerSchemesAsPrivileged([
  { scheme: 'craftfile', privileges: { standard: true, secure: true, supportFetchAPI: true, stream: true, corsEnabled: true } }
]);

// ---------- safety helpers ----------
function norm(p) { return path.resolve(p); }
function same(a, b) { return IS_WIN ? a.toLowerCase() === b.toLowerCase() : a === b; }
function isInside(child, parent) {
  const c = norm(child), p = norm(parent);
  if (same(c, p)) return true;
  const rel = path.relative(p, c);
  return !!rel && !rel.startsWith('..') && !path.isAbsolute(rel);
}
function allowed(p) {
  if (!p) return false;
  const extra = [path.join(app.getPath('userData'), 'thumbs')];
  return allowedRoots.concat(extra).some(r => isInside(p, r)) || isRemovedFolderPath(p);
}
function isRemovedFolderPath(p) {
  // The "Removed files" folder at the top of a drive that holds one of the chosen folders.
  return allowedRoots.some(r => {
    const drive = path.parse(norm(r)).root;
    return isInside(p, path.join(drive, REMOVED_FOLDER));
  });
}
function assertAllowed(p) { if (!allowed(p)) throw new Error('That file is outside the folders you chose, so the app left it alone.'); }

async function exists(p) { try { await fsp.access(p); return true; } catch (e) { return false; } }
async function uniquePath(dir, base, ext) {
  let candidate = path.join(dir, base + (ext ? '.' + ext : ''));
  let n = 2;
  while (await exists(candidate)) { candidate = path.join(dir, `${base} (${n})` + (ext ? '.' + ext : '')); n++; }
  return candidate;
}
function splitName(file) {
  const ext = path.extname(file).slice(1);
  return { base: ext ? path.basename(file, '.' + ext) : path.basename(file), ext };
}

// Move a file without ever overwriting. Across drives: copy, check, then delete the original.
async function safeMove(src, destDir, wantedBase) {
  const { base, ext } = splitName(src);
  const dest = await uniquePath(destDir, wantedBase || base, ext);
  // Belt and braces: the file must land directly in the folder asked for, never anywhere else.
  if (!same(path.dirname(norm(dest)), norm(destDir))) throw new Error('That name isn’t allowed, so the file was not moved.');
  try {
    await fsp.rename(src, dest);
  } catch (e) {
    if (e.code !== 'EXDEV') throw e;
    await fsp.copyFile(src, dest, fs.constants.COPYFILE_EXCL);
    const [a, b] = await Promise.all([fsp.stat(src), fsp.stat(dest)]);
    if (a.size !== b.size) { await fsp.unlink(dest).catch(() => {}); throw new Error('The copy did not match the original, so the file was not moved.'); }
    const t = a.mtime; await fsp.utimes(dest, a.atime, t).catch(() => {});
    await fsp.unlink(src);
  }
  return dest;
}

// ---------- PowerShell helper (Windows only) ----------
function powershell(script, data) {
  return new Promise((resolve, reject) => {
    if (!IS_WIN) return reject(new Error('Only available on Windows'));
    const full = '[Console]::OutputEncoding = [System.Text.Encoding]::UTF8\n$ProgressPreference = "SilentlyContinue"\n' + script;
    const encoded = Buffer.from(full, 'utf16le').toString('base64');
    const ps = spawn('powershell.exe', ['-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass', '-EncodedCommand', encoded], {
      windowsHide: true, env: Object.assign({}, process.env, { CRAFT_DATA: JSON.stringify(data || {}) })
    });
    let out = '', err = '';
    ps.stdout.on('data', d => out += d.toString('utf8'));
    ps.stderr.on('data', d => err += d.toString('utf8'));
    ps.on('error', reject);
    ps.on('close', code => {
      const txt = out.trim();
      if (code !== 0 && !txt) return reject(new Error(err.trim() || 'PowerShell failed'));
      try { resolve(txt ? JSON.parse(txt) : null); } catch (e) { resolve(txt); }
    });
  });
}

// ---------- scanning ----------
async function walk(root, opts) {
  const folders = [];
  const files = [];
  let count = 0;
  async function visit(dir, depth, parent) {
    let entries;
    try { entries = await fsp.readdir(dir, { withFileTypes: true }); } catch (e) { return; }
    const subdirs = [], wanted = [];
    for (const ent of entries) {
      const full = path.join(dir, ent.name);
      if (ent.isDirectory()) {
        if (!opts.recursive) continue;
        if (ent.name.startsWith('.') || ent.name.startsWith('$') || SKIP_DIRS.has(ent.name.toLowerCase())) continue;
        folders.push({ path: full, name: ent.name, depth, parent });
        subdirs.push(full);
      } else if (ent.isFile()) {
        if (ent.name.startsWith('~$') || ent.name.toLowerCase() === 'desktop.ini' || ent.name.toLowerCase() === 'thumbs.db') continue;
        const { base, ext } = splitName(ent.name);
        if (opts.filterInbox && INBOX_SKIP_EXT.has(ext.toLowerCase())) continue;
        if (++count > MAX_FILES) break;
        wanted.push({ full, base, ext: ext.toLowerCase() });
      }
    }
    // Look up file sizes and dates many at a time (much quicker on slow disks than one by one).
    for (let i = 0; i < wanted.length; i += 48) {
      const chunk = wanted.slice(i, i + 48);
      const stats = await Promise.all(chunk.map(w => fsp.stat(w.full).catch(() => null)));
      chunk.forEach((w, j) => {
        const st = stats[j];
        if (st) files.push({ path: w.full, dir, name: w.base, ext: w.ext, size: st.size, born: (st.birthtimeMs || st.mtimeMs), mtime: st.mtimeMs });
      });
    }
    if (count > MAX_FILES) return;
    if (depth < 14) for (const sd of subdirs) await visit(sd, depth + 1, sd);
  }
  await visit(root, 0, root);
  return { folders, files, capped: count > MAX_FILES };
}

ipcMain.handle('scan', async (e, req) => {
  const result = { locations: [], inbox: null };
  for (const root of req.locations || []) {
    if (!(await exists(root))) { result.locations.push({ root, ok: false }); continue; }
    const w = await walk(root, { recursive: true });
    result.locations.push({ root, ok: true, folders: w.folders, files: w.files, capped: w.capped });
  }
  if (req.inbox) {
    if (await exists(req.inbox)) {
      const w = await walk(req.inbox, { recursive: false, filterInbox: true });
      result.inbox = { root: req.inbox, ok: true, files: w.files };
    } else result.inbox = { root: req.inbox, ok: false, files: [] };
  }
  return result;
});

ipcMain.handle('set-roots', (e, roots) => {
  allowedRoots = (roots || []).filter(Boolean).map(norm);
  startWatching();
  return true;
});

async function startWatching() {
  watchers.forEach(w => { try { w.close(); } catch (e) {} });
  watchers = [];
  const gen = ++watchGen;
  let timer = null;
  const ping = (ev, name) => {
    // Browsers write half-finished downloads as temporary files; those changes are ignored.
    if (name && /\.(crdownload|part|partial|tmp|download)$|^~\$|desktop\.ini$|thumbs\.db$/i.test(String(name))) return;
    clearTimeout(timer);
    timer = setTimeout(() => { if (win && !win.isDestroyed()) win.webContents.send('fs-changed'); }, 2500);
  };
  for (const r of allowedRoots) {
    // Portable drives are not watched, so Windows can still "safely remove" them.
    // Changes there are picked up when the app window is brought back to the front.
    const type = await driveType(r).catch(() => 'Fixed');
    if (gen !== watchGen) return;
    if (type === 'Removable' || type === 'Network') continue;
    try { const w = fs.watch(r, { recursive: IS_WIN || process.platform === 'darwin' }, ping); w.on('error', () => {}); watchers.push(w); } catch (e) { /* drive unplugged */ }
  }
}
let watchGen = 0;

// ---------- duplicates: only files of the same size are compared, by their contents ----------
const hashCache = new Map();
function hashFile(p, size, mtime) {
  const key = p + '|' + size + '|' + mtime;
  if (hashCache.has(key)) return Promise.resolve(hashCache.get(key));
  return hashFileRaw(p).then(h => { if (h) hashCache.set(key, h); return h; });
}
const quickCache = new Map();
async function quickHash(p, size, mtime) {
  const key = p + '|' + size + '|' + mtime;
  if (quickCache.has(key)) return quickCache.get(key);
  let fh;
  try {
    fh = await fsp.open(p, 'r');
    const n = Math.min(size, 64 * 1024);
    const a = Buffer.alloc(n); await fh.read(a, 0, n, 0);
    const b = Buffer.alloc(n); await fh.read(b, 0, n, Math.max(0, size - n));
    const h = crypto.createHash('sha1').update(a).update(b).digest('hex');
    quickCache.set(key, h);
    return h;
  } catch (e) { return null; } finally { if (fh) await fh.close().catch(() => {}); }
}
function hashFileRaw(p) {
  return new Promise((resolve) => {
    const h = crypto.createHash('sha1');
    const s = fs.createReadStream(p);
    s.on('data', d => h.update(d));
    s.on('error', () => resolve(null));
    s.on('end', () => resolve(h.digest('hex')));
  });
}
ipcMain.handle('find-duplicates', async (e, files) => {
  const bySize = new Map();
  for (const f of files) { if (!f.size) continue; const k = f.size; if (!bySize.has(k)) bySize.set(k, []); bySize.get(k).push(f); }
  const out = {};
  for (const [size, group0] of bySize) {
    if (group0.length < 2) continue;
    // First compare just the start and end of each file; only files that still match are read in full.
    const byQuick = new Map();
    for (const f of group0) {
      if (!allowed(f.path)) continue;
      const q = await quickHash(f.path, f.size, f.mtime);
      if (!q) continue;
      if (!byQuick.has(q)) byQuick.set(q, []);
      byQuick.get(q).push(f);
    }
    const group = [].concat(...[...byQuick.values()].filter(g => g.length > 1));
    const byHash = new Map();
    for (const f of group) {
      const p = f.path;
      const hsh = f.size <= 128 * 1024 ? await quickHash(p, f.size, f.mtime) : await hashFile(p, f.size, f.mtime);
      if (!hsh) continue;
      if (!byHash.has(hsh)) byHash.set(hsh, []);
      byHash.get(hsh).push(p);
    }
    for (const [hsh, group] of byHash) if (group.length > 1) group.forEach(p => { out[p] = hsh.slice(0, 12); });
  }
  return out;
});

// ---------- thumbnails ----------
const thumbCache = new Map();
const IMAGE_EXT = new Set(['jpg', 'jpeg', 'png', 'gif', 'bmp', 'webp', 'tif', 'tiff', 'ico']);

async function shellThumb(p) {
  if (!IS_WIN && process.platform !== 'darwin') return null;
  try {
    const img = await nativeImage.createThumbnailFromPath(p, { width: 360, height: 360 });
    if (img && !img.isEmpty()) return img.toDataURL();
  } catch (e) { /* no thumbnail handler for this type */ }
  return null;
}
async function resizedImage(p) {
  try {
    const img = nativeImage.createFromPath(p);
    if (img.isEmpty()) return null;
    const sz = img.getSize();
    const r = img.resize(sz.width > sz.height ? { width: 420 } : { height: 420 });
    return r.toDataURL();
  } catch (e) { return null; }
}
function bufferToDataUrl(buf, ext) {
  const mime = { png: 'image/png', jpg: 'image/jpeg', jpeg: 'image/jpeg', gif: 'image/gif', bmp: 'image/bmp', webp: 'image/webp' }[ext];
  if (!mime) return null;
  try {
    const img = nativeImage.createFromBuffer(buf);
    if (!img.isEmpty()) {
      const sz = img.getSize();
      if (sz.width > 420 || sz.height > 420) return img.resize(sz.width > sz.height ? { width: 420 } : { height: 420 }).toDataURL();
    }
  } catch (e) {}
  return 'data:' + mime + ';base64,' + buf.toString('base64');
}
async function zipThumb(p, kind) {
  const JSZip = require('jszip');
  const zip = await JSZip.loadAsync(await fsp.readFile(p));
  const names = Object.keys(zip.files).filter(n => !zip.files[n].dir);
  const pics = names.filter(n => /\.(png|jpe?g|gif|bmp|webp)$/i.test(n) && (kind !== 'doc' || /^word\/media\//i.test(n)));
  let url = null;
  if (pics.length) {
    // Prefer the biggest picture (usually the paper itself, not a small logo).
    let best = pics[0], bestSize = 0;
    for (const n of pics) { const s = zip.files[n]._data && zip.files[n]._data.uncompressedSize || 0; if (s > bestSize) { best = n; bestSize = s; } }
    const buf = await zip.files[best].async('nodebuffer');
    url = bufferToDataUrl(buf, path.extname(best).slice(1).toLowerCase());
  }
  return { url, inner: kind === 'zip' ? names.length : 0 };
}
async function psdThumb(p) {
  // Photoshop files usually carry a small JPEG preview (image resource 1036).
  const fh = await fsp.open(p, 'r');
  try {
    const head = Buffer.alloc(30);
    await fh.read(head, 0, 30, 0);
    if (head.toString('ascii', 0, 4) !== '8BPS') return null;
    const cmLen = head.readUInt32BE(26);
    let pos = 30 + cmLen;
    const lenBuf = Buffer.alloc(4); await fh.read(lenBuf, 0, 4, pos);
    const resLen = lenBuf.readUInt32BE(0); pos += 4;
    const res = Buffer.alloc(Math.min(resLen, 8 * 1024 * 1024)); await fh.read(res, 0, res.length, pos);
    let i = 0;
    while (i + 12 < res.length) {
      if (res.toString('ascii', i, i + 4) !== '8BIM') break;
      const id = res.readUInt16BE(i + 4);
      let nameLen = res.readUInt8(i + 6); let off = i + 7 + nameLen; if ((nameLen + 1) % 2) off++;
      const size = res.readUInt32BE(off); const dataStart = off + 4;
      if (id === 1036 || id === 1033) {
        const jpg = res.subarray(dataStart + 28, dataStart + size);
        return 'data:image/jpeg;base64,' + jpg.toString('base64');
      }
      i = dataStart + size + (size % 2);
    }
  } finally { await fh.close(); }
  return null;
}

// Finished thumbnails are saved as small picture files, so the next time the app opens they appear straight away.
function thumbDir() { return path.join(app.getPath('userData'), 'thumbs'); }
function fileUrl(p) { return 'craftfile://f/' + encodeURIComponent(p); }
async function saveThumb(key, out) {
  if (!out.url || !out.url.startsWith('data:')) return out;
  const m = /^data:image\/(png|jpeg|gif|bmp|webp);base64,(.*)$/.exec(out.url);
  if (!m) return out;
  const file = path.join(thumbDir(), crypto.createHash('sha1').update(key).digest('hex') + '.' + (m[1] === 'jpeg' ? 'jpg' : m[1]));
  try { await fsp.mkdir(thumbDir(), { recursive: true }); await fsp.writeFile(file, Buffer.from(m[2], 'base64')); return { url: fileUrl(file), inner: out.inner }; } catch (e) { return out; }
}
async function loadThumbIndex() {
  try { return JSON.parse(await fsp.readFile(path.join(thumbDir(), 'index.json'), 'utf8')); } catch (e) { return {}; }
}
let thumbIndex = null, thumbIndexTimer = null;
function saveThumbIndexSoon() {
  clearTimeout(thumbIndexTimer);
  thumbIndexTimer = setTimeout(() => { fsp.mkdir(thumbDir(), { recursive: true }).then(() => fsp.writeFile(path.join(thumbDir(), 'index.json'), JSON.stringify(thumbIndex))).catch(() => {}); }, 2000);
}
ipcMain.handle('thumb-saved', async (e, p, url) => {
  // A preview made in the window (PDF pages) is saved the same way.
  if (!allowed(p) || typeof url !== 'string') return null;
  const st = await fsp.stat(p).catch(() => null);
  if (!st) return null;
  const key = p + '|' + st.size + '|' + st.mtimeMs;
  const out = await saveThumb(key, { url, inner: 0 });
  if (!thumbIndex) thumbIndex = await loadThumbIndex();
  thumbIndex[key] = out; thumbCache.set(key, out); saveThumbIndexSoon();
  return out;
});

ipcMain.handle('thumb', async (e, p, kind) => {
  if (!allowed(p)) return null;
  const st = await fsp.stat(p).catch(() => null);
  if (!st) return null;
  const key = p + '|' + st.size + '|' + st.mtimeMs;
  if (thumbCache.has(key)) return thumbCache.get(key);
  if (!thumbIndex) thumbIndex = await loadThumbIndex();
  const known = thumbIndex[key];
  if (known && (!known.url || await exists(decodeURIComponent(known.url.replace('craftfile://f/', ''))))) { thumbCache.set(key, known); return known; }
  const ext = path.extname(p).slice(1).toLowerCase();
  let out = { url: null, inner: 0 };
  try {
    if (kind === 'doc' && ext === 'docx') { out = await zipThumb(p, 'doc'); if (!out.url) out.url = await shellThumb(p); }
    else if (kind === 'zip') { out = await zipThumb(p, 'zip'); if (!out.url) out.url = await shellThumb(p); }
    else if (ext === 'psd') { out.url = (await shellThumb(p)) || (await psdThumb(p)); }
    else if (IMAGE_EXT.has(ext)) { out.url = (await shellThumb(p)) || (await resizedImage(p)); }
    else { out.url = await shellThumb(p); }
  } catch (err) { out = { url: null, inner: 0 }; }
  out = await saveThumb(key, out);
  thumbCache.set(key, out);
  // Files with no preview are remembered too, except PDFs, which the window may still draw.
  if (out.url || (kind !== 'pdf' && ext !== 'ai')) { thumbIndex[key] = out; saveThumbIndexSoon(); }
  return out;
});

// ---------- file actions ----------
function ok(value) { return { ok: true, value }; }
function fail(err) { return { ok: false, error: (err && err.message) || String(err) }; }

// The same name rules Windows uses, checked before any move or rename.
function checkName(name) {
  if (!name) throw new Error('Please type a name.');
  if (/[\\/:*?"<>|]/.test(name)) throw new Error('Names can’t contain any of these: \\ / : * ? " < > |');
  if (/[. ]$/.test(name)) throw new Error('Names can’t end with a full stop or a space.');
}

ipcMain.handle('move', async (e, src, destDir, wantedBase) => {
  try {
    assertAllowed(src); assertAllowed(destDir);
    if (wantedBase != null) { wantedBase = String(wantedBase).trim(); checkName(wantedBase); }
    if (same(path.dirname(norm(src)), norm(destDir)) && (!wantedBase || wantedBase === splitName(src).base)) return ok(src);
    if (!(await exists(destDir))) throw new Error('That folder isn’t there any more. It may have been moved, or the drive unplugged.');
    return ok(await safeMove(src, destDir, wantedBase));
  } catch (err) { return fail(err); }
});

ipcMain.handle('rename', async (e, src, newBase) => {
  try {
    assertAllowed(src);
    const name = String(newBase || '').trim();
    checkName(name);
    const { ext } = splitName(src);
    const dest = path.join(path.dirname(src), name + (ext ? '.' + ext : ''));
    if (norm(dest) === norm(src)) return ok(src);
    if (same(norm(dest), norm(src))) {
      // Only the capital letters changed. Windows counts these as the same name,
      // so the file goes through a temporary name to make the change stick.
      const tmp = await uniquePath(path.dirname(src), name + ' (renaming)', ext);
      await fsp.rename(src, tmp);
      try { await fsp.rename(tmp, dest); } catch (err) { await fsp.rename(tmp, src).catch(() => {}); throw err; }
      return ok(dest);
    }
    if (await exists(dest) && !same(dest, src)) throw new Error('There’s already a file called “' + path.basename(dest) + '” in this folder.');
    await fsp.rename(src, dest);
    return ok(dest);
  } catch (err) { return fail(err); }
});

const driveTypeCache = new Map();
async function driveType(p) {
  if (!IS_WIN) return 'Fixed';
  const root = path.parse(norm(p)).root;
  if (root.startsWith('\\\\')) return 'Network';
  if (driveTypeCache.has(root)) return driveTypeCache.get(root);
  let t = 'Fixed';
  try { t = await powershell("$d = $env:CRAFT_DATA | ConvertFrom-Json; ([System.IO.DriveInfo]::new($d.root)).DriveType.ToString() | ConvertTo-Json", { root }); } catch (e) {}
  driveTypeCache.set(root, t);
  return t;
}

ipcMain.handle('remove', async (e, p) => {
  try {
    assertAllowed(p);
    const type = await driveType(p);
    if (type === 'Removable' || type === 'Network') {
      const holder = path.join(path.parse(norm(p)).root, REMOVED_FOLDER);
      await fsp.mkdir(holder, { recursive: true });
      const dest = await safeMove(p, holder);
      return ok({ method: 'folder', removedPath: dest });
    }
    await shell.trashItem(p);
    return ok({ method: 'recycle', removedPath: null });
  } catch (err) { return fail(err); }
});

const RESTORE_PS = `
$d = $env:CRAFT_DATA | ConvertFrom-Json
$shell = New-Object -ComObject Shell.Application
$bin = $shell.Namespace(10)
$best = $null; $bestDate = [datetime]::MinValue
foreach ($item in $bin.Items()) {
  $from = $item.ExtendedProperty('System.Recycle.DeletedFrom')
  if ($from -eq $null) { continue }
  if ($from.TrimEnd('\\') -ne $d.dir.TrimEnd('\\')) { continue }
  $nm = $item.Name
  $fileName = [System.IO.Path]::GetFileName($item.Path)
  $orig = $item.ExtendedProperty('System.ParsingName')
  if (($nm -ne $d.name) -and ($nm -ne $d.base) -and ($orig -ne $d.name)) { continue }
  $when = $item.ExtendedProperty('System.Recycle.DateDeleted')
  if ($when -ne $null -and $when -gt $bestDate) { $best = $item; $bestDate = $when } elseif ($best -eq $null) { $best = $item }
}
if ($best -eq $null) { 'notfound' | ConvertTo-Json; exit }
$best.InvokeVerb('undelete')
Start-Sleep -Milliseconds 400
'done' | ConvertTo-Json
`;

ipcMain.handle('restore', async (e, entry) => {
  try {
    assertAllowed(path.dirname(entry.origPath));
    if (entry.method === 'folder') {
      assertAllowed(entry.removedPath);
      const { base } = splitName(entry.origPath);
      return ok(await safeMove(entry.removedPath, path.dirname(entry.origPath), base));
    }
    const dir = path.dirname(entry.origPath), name = path.basename(entry.origPath);
    const res = await powershell(RESTORE_PS, { dir, name, base: splitName(name).base });
    if (res !== 'done') throw new Error('Couldn’t find it in the Windows Recycle Bin. It may have been emptied, or you can put it back from there yourself.');
    return ok(entry.origPath);
  } catch (err) { return fail(err); }
});

ipcMain.handle('open', async (e, p, exe) => {
  try {
    assertAllowed(p);
    if (exe) {
      if (!(await exists(exe))) throw new Error('That program could not be found. Choose it again in Settings.');
      spawn(exe, [p], { detached: true, stdio: 'ignore' }).unref();
      return ok(true);
    }
    const msg = await shell.openPath(p);
    if (msg) throw new Error(msg);
    return ok(true);
  } catch (err) { return fail(err); }
});

ipcMain.handle('open-with-dialog', async (e, p) => {
  try {
    assertAllowed(p);
    if (!IS_WIN) throw new Error('Only available on Windows');
    spawn('rundll32.exe', ['shell32.dll,OpenAs_RunDLL', p], { detached: true, stdio: 'ignore' }).unref();
    return ok(true);
  } catch (err) { return fail(err); }
});

ipcMain.handle('show-in-folder', async (e, p) => { try { assertAllowed(p); shell.showItemInFolder(p); return ok(true); } catch (err) { return fail(err); } });

ipcMain.handle('open-recycle-bin', async () => {
  try { if (IS_WIN) spawn('explorer.exe', ['shell:RecycleBinFolder'], { detached: true, stdio: 'ignore' }).unref(); return ok(true); } catch (err) { return fail(err); }
});

// Friendly names of the programs Windows uses for each file type.
const ASSOC_PS = `
$d = $env:CRAFT_DATA | ConvertFrom-Json
Add-Type -Namespace Craft -Name Assoc -MemberDefinition @'
[DllImport("Shlwapi.dll", CharSet = CharSet.Unicode)]
public static extern uint AssocQueryString(uint flags, uint str, string assoc, string extra, [Out] System.Text.StringBuilder outStr, ref uint outLen);
'@
$out = @{}
foreach ($ext in $d.exts) {
  $len = [uint32]512; $sb = New-Object System.Text.StringBuilder 512
  $r = [Craft.Assoc]::AssocQueryString(0, 4, ('.' + $ext), $null, $sb, [ref]$len)
  if ($r -eq 0) { $out[$ext] = $sb.ToString() } else { $out[$ext] = '' }
}
$out | ConvertTo-Json -Compress
`;
ipcMain.handle('default-apps', async (e, exts) => {
  try { return (await powershell(ASSOC_PS, { exts })) || {}; } catch (err) { return {}; }
});

// Folders the app refuses to work in, so it can never be pointed at Windows itself or a whole system drive.
function folderProblem(p) {
  const n = norm(p);
  const home = app.getPath('home');
  const sysRoot = path.parse(process.env.SystemRoot || process.env.WINDIR || 'C:\\Windows').root || (IS_WIN ? 'C:\\' : '/');
  const bad = [process.env.SystemRoot, process.env.WINDIR, process.env.ProgramFiles, process.env['ProgramFiles(x86)'], process.env.ProgramData,
    process.env.APPDATA, process.env.LOCALAPPDATA, app.getPath('userData')].filter(Boolean);
  if (same(n, path.parse(n).root) && same(path.parse(n).root, sysRoot)) return 'Please choose a folder, not the whole ' + n + ' drive. (Portable drives are fine to choose whole.)';
  if (same(n, norm(home))) return 'That is your whole user folder, which has lots of Windows files in it. Please choose a folder inside it, like Documents or a craft folder.';
  if (!IS_WIN && same(n, '/')) return 'Please choose a folder, not the whole computer.';
  for (const b of bad) if (isInside(n, b)) return 'That folder belongs to Windows or a program, so the app won’t work in it. Please choose a different folder.';
  return '';
}
ipcMain.handle('pick-folder', async (e, title, startIn) => {
  const r = await dialog.showOpenDialog(win, { title: title || 'Choose a folder', properties: ['openDirectory'], defaultPath: startIn || app.getPath('documents') });
  if (r.canceled || !r.filePaths.length) return null;
  const p = r.filePaths[0];
  const problem = folderProblem(p);
  return problem ? { error: problem } : { path: p };
});

ipcMain.handle('pick-program', async () => {
  const r = await dialog.showOpenDialog(win, {
    title: 'Choose a program', properties: ['openFile'],
    defaultPath: IS_WIN ? 'C:\\Program Files' : app.getPath('home'),
    filters: IS_WIN ? [{ name: 'Programs', extensions: ['exe'] }] : []
  });
  if (r.canceled || !r.filePaths.length) return null;
  const exe = r.filePaths[0];
  return { exe, name: path.basename(exe).replace(/\.exe$/i, '') };
});

// ---------- fonts ----------
const FONTS_PS = `
Add-Type -AssemblyName System.Drawing
$names = (New-Object System.Drawing.Text.InstalledFontCollection).Families | ForEach-Object { $_.Name }
$names | ConvertTo-Json -Compress
`;
ipcMain.handle('system-fonts', async () => {
  try { const r = await powershell(FONTS_PS); return Array.isArray(r) ? r : (r ? [r] : []); } catch (err) { return []; }
});
function userFontsDir() { return path.join(process.env.LOCALAPPDATA || app.getPath('appData'), 'Microsoft', 'Windows', 'Fonts'); }
ipcMain.handle('fonts-installed', async (e, files) => {
  const out = {};
  for (const f of files) {
    const n = path.basename(f);
    out[f] = (await exists(path.join(userFontsDir(), n))) || (IS_WIN && await exists(path.join(process.env.WINDIR || 'C:\\Windows', 'Fonts', n)));
  }
  return out;
});
const FONT_ADD_PS = `
$d = $env:CRAFT_DATA | ConvertFrom-Json
Add-Type -Namespace Craft -Name Fonts -MemberDefinition @'
[DllImport("gdi32.dll", CharSet = CharSet.Unicode)] public static extern int AddFontResource(string file);
[DllImport("gdi32.dll", CharSet = CharSet.Unicode)] public static extern int RemoveFontResource(string file);
[DllImport("user32.dll")] public static extern int SendMessageTimeout(IntPtr h, uint m, IntPtr w, IntPtr l, uint f, uint t, out IntPtr r);
'@
$key = 'HKCU:\\Software\\Microsoft\\Windows NT\\CurrentVersion\\Fonts'
if (-not (Test-Path $key)) { New-Item -Path $key -Force | Out-Null }
if ($d.remove) {
  [Craft.Fonts]::RemoveFontResource($d.target) | Out-Null
  Remove-ItemProperty -Path $key -Name $d.label -ErrorAction SilentlyContinue
  Remove-Item -LiteralPath $d.target -ErrorAction SilentlyContinue
} else {
  Copy-Item -LiteralPath $d.source -Destination $d.target -Force
  New-ItemProperty -Path $key -Name $d.label -Value $d.target -PropertyType String -Force | Out-Null
  [Craft.Fonts]::AddFontResource($d.target) | Out-Null
}
$r = [IntPtr]::Zero
[Craft.Fonts]::SendMessageTimeout([IntPtr]0xffff, 0x001D, [IntPtr]::Zero, [IntPtr]::Zero, 2, 1000, [ref]$r) | Out-Null
'done' | ConvertTo-Json
`;
function fontLabel(file) {
  const { base, ext } = splitName(file);
  return base + (ext.toLowerCase() === 'otf' ? ' (OpenType)' : ' (TrueType)');
}
ipcMain.handle('install-font', async (e, p) => {
  try {
    assertAllowed(p);
    if (!IS_WIN) throw new Error('Installing fonts only works on Windows.');
    await fsp.mkdir(userFontsDir(), { recursive: true });
    const target = path.join(userFontsDir(), path.basename(p));
    await powershell(FONT_ADD_PS, { source: p, target, label: fontLabel(p), remove: false });
    return ok(true);
  } catch (err) { return fail(err); }
});
ipcMain.handle('uninstall-font', async (e, p) => {
  try {
    if (!IS_WIN) throw new Error('Only available on Windows');
    const target = path.join(userFontsDir(), path.basename(p));
    await powershell(FONT_ADD_PS, { target, label: fontLabel(p), remove: true });
    return ok(true);
  } catch (err) { return fail(err); }
});

ipcMain.handle('app-info', () => ({ platform: process.platform, documents: app.getPath('documents'), downloads: app.getPath('downloads'), version: app.getVersion() }));

// ---------- window ----------
function createWindow() {
  win = new BrowserWindow({
    width: 1400, height: 900, minWidth: 900, minHeight: 600,
    title: 'Craft Library',
    icon: path.join(__dirname, 'app', 'icon.png'),
    autoHideMenuBar: true,
    backgroundColor: '#F2F4F7',
    webPreferences: { contextIsolation: true, nodeIntegration: false, sandbox: true, preload: path.join(__dirname, 'preload.js') }
  });
  win.loadFile(path.join(__dirname, 'app', 'index.html'));
  win.webContents.setWindowOpenHandler(({ url }) => { if (/^https?:/.test(url)) shell.openExternal(url); return { action: 'deny' }; });
  win.webContents.on('will-navigate', (ev) => ev.preventDefault());
}

app.whenReady().then(() => {
  // Lets the screens show pictures and fonts from the chosen folders, and nothing else.
  protocol.handle('craftfile', (req) => {
    try {
      const u = new URL(req.url);
      const p = decodeURIComponent(u.pathname.replace(/^\//, ''));
      if (!allowed(p)) return new Response('Not allowed', { status: 403 });
      return net.fetch(pathToFileURL(p).toString());
    } catch (e) { return new Response('Bad request', { status: 400 }); }
  });
  createWindow();
  app.on('activate', () => { if (BrowserWindow.getAllWindows().length === 0) createWindow(); });
});
app.on('window-all-closed', () => { if (process.platform !== 'darwin') app.quit(); });
