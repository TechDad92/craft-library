using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Principal;
using System.Text;
using CraftLibrary.App.Native;
using CraftLibrary.Core;

namespace CraftLibrary.App.Services;

/// <summary>The real Windows pieces: drive types, the Recycle Bin, default programs, Open with, File Explorer.</summary>
public sealed class WindowsPlatform : IPlatform
{
    private readonly ConcurrentDictionary<string, DriveKind> _driveKinds = new(StringComparer.OrdinalIgnoreCase);

    public DriveKind DriveKindOf(string path)
    {
        var root = Path.GetPathRoot(Path.GetFullPath(path)) ?? "";
        if (root.StartsWith(@"\\", StringComparison.Ordinal)) return DriveKind.Network;
        return _driveKinds.GetOrAdd(root, r =>
        {
            try
            {
                return new DriveInfo(r).DriveType switch
                {
                    DriveType.Fixed => DriveKind.Fixed,
                    DriveType.Removable => DriveKind.Removable,
                    DriveType.Network => DriveKind.Network,
                    _ => DriveKind.Other,
                };
            }
            catch { return DriveKind.Fixed; }
        });
    }

    /// <summary>Forget what kind each drive is (drive letters can be reused after unplugging).</summary>
    public void ForgetDrives() => _driveKinds.Clear();

    public void SendToRecycleBin(string path)
    {
        var op = new NativeMethods.SHFILEOPSTRUCT
        {
            wFunc = NativeMethods.FO_DELETE,
            pFrom = path + "\0\0",
            // ALLOWUNDO = to the Recycle Bin. WANTNUKEWARNING = if Windows can't recycle it (too big for the bin),
            // Windows asks first instead of deleting it for good.
            fFlags = NativeMethods.FOF_ALLOWUNDO | NativeMethods.FOF_NOCONFIRMATION | NativeMethods.FOF_SILENT |
                     NativeMethods.FOF_NOERRORUI | NativeMethods.FOF_WANTNUKEWARNING,
        };
        var rc = NativeMethods.SHFileOperation(ref op);
        if (op.fAnyOperationsAborted) throw new FriendlyException("The file was not removed.");
        if (rc != 0 || File.Exists(path))
            throw new FriendlyException("Windows couldn’t move that file to the Recycle Bin. It may be open in another program.");
    }

    public string? RestoreFromRecycleBin(string originalPath, DateTime removedAfterUtc)
    {
        var root = Path.GetPathRoot(originalPath);
        var sid = WindowsIdentity.GetCurrent().User?.Value;
        if (string.IsNullOrEmpty(root) || sid == null) return null;
        var bin = Path.Combine(root, "$Recycle.Bin", sid);
        return RecycleBinFiles.Restore(bin, originalPath, removedAfterUtc);
    }

    // ---------- programs ----------

    private readonly ConcurrentDictionary<string, string> _appNames = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The friendly name of the program Windows opens this kind of file with, or "" when none is set.</summary>
    public string DefaultProgramName(string ext) => _appNames.GetOrAdd(ext, e =>
    {
        try
        {
            uint len = 0;
            NativeMethods.AssocQueryString(NativeMethods.ASSOCF_NONE, NativeMethods.ASSOCSTR_FRIENDLYAPPNAME, "." + e, null, null, ref len);
            if (len == 0) return "";
            var sb = new StringBuilder((int)len);
            return NativeMethods.AssocQueryString(NativeMethods.ASSOCF_NONE, NativeMethods.ASSOCSTR_FRIENDLYAPPNAME, "." + e, null, sb, ref len) == 0 ? sb.ToString() : "";
        }
        catch { return ""; }
    });

    public static void Open(string path, string? exe)
    {
        if (!string.IsNullOrEmpty(exe))
        {
            if (!File.Exists(exe)) throw new FriendlyException("That program could not be found. Choose it again in Settings.");
            Process.Start(new ProcessStartInfo(exe) { ArgumentList = { path }, UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(path) })?.Dispose();
            return;
        }
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true })?.Dispose(); }
        catch (System.ComponentModel.Win32Exception) { throw new FriendlyException("Windows has no program set for ." + PathRules.SplitName(path).Ext + " files. Use “Open with…” to choose one."); }
    }

    public static void OpenWithDialog(IntPtr owner, string path)
    {
        var info = new NativeMethods.OPENASINFO { pcszFile = path, oaifInFlags = NativeMethods.OAIF_EXEC | NativeMethods.OAIF_ALLOW_REGISTRATION };
        NativeMethods.SHOpenWithDialog(owner, ref info);
    }

    public static void ShowInExplorer(string path) =>
        Process.Start(new ProcessStartInfo("explorer.exe", "/select,\"" + path + "\"") { UseShellExecute = false })?.Dispose();

    public static void OpenRecycleBin() =>
        Process.Start(new ProcessStartInfo("explorer.exe", "shell:RecycleBinFolder") { UseShellExecute = false })?.Dispose();

    /// <summary>File Explorer's sort order: "Card 2" comes before "Card 10", and capitals are ignored.</summary>
    public static int CompareNames(string a, string b) => NativeMethods.StrCmpLogicalW(a, b);
}
