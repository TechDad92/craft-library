// Puts the Craft Library icon and name into the app's .exe (used when building on a non-Windows computer).
import * as ResEdit from 'resedit';
import fs from 'fs';
const exePath = process.argv[2];
const icoPath = process.argv[3];
const exe = ResEdit.NtExecutable.from(fs.readFileSync(exePath), { ignoreCert: true });
const res = ResEdit.NtExecutableResource.from(exe);
const ico = ResEdit.Data.IconFile.from(fs.readFileSync(icoPath));
const groups = ResEdit.Resource.IconGroupEntry.fromEntries(res.entries);
const groupId = groups.length ? groups[0].id : 1;
const lang = groups.length ? groups[0].lang : 1033;
ResEdit.Resource.IconGroupEntry.replaceIconsForResource(res.entries, groupId, lang, ico.icons.map(i => i.data));
const vi = ResEdit.Resource.VersionInfo.fromEntries(res.entries)[0];
if (vi) {
  vi.setStringValues({ lang: 1033, codepage: 1200 }, {
    FileDescription: 'Craft Library', ProductName: 'Craft Library',
    CompanyName: 'Craft Library', OriginalFilename: 'Craft Library.exe', InternalName: 'Craft Library'
  });
  vi.setFileVersion(0, 2, 3, 0, 1033);
  vi.setProductVersion(0, 2, 3, 0, 1033);
  vi.outputToResourceEntries(res.entries);
}
res.outputResource(exe);
fs.writeFileSync(exePath, Buffer.from(exe.generate()));
console.log('Icon set on', exePath);
