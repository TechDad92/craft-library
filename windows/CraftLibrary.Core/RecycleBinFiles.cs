using System.Text;

namespace CraftLibrary.Core;

/// <summary>
/// Reads the Windows Recycle Bin's own files. Since Windows Vista each removed file is stored as a pair in
/// <c>X:\$Recycle.Bin\&lt;your account id&gt;\</c>: <c>$R…</c> holds the file itself, and <c>$I…</c> (same ending)
/// records where it came from and when it was removed. Putting a file back means moving <c>$R…</c> to its old
/// folder and deleting <c>$I…</c>, which is what File Explorer does. Doing it here means a name that has been
/// taken in the meantime gets " (2)" instead of Windows asking to replace the file.
/// </summary>
public static class RecycleBinFiles
{
    public sealed record Info(string OriginalPath, long Size, DateTime DeletedUtc);

    /// <summary>Reads a <c>$I…</c> file (version 1 from Vista/7/8, version 2 from Windows 10 and 11).</summary>
    public static Info? Parse(byte[] data)
    {
        if (data.Length < 24) return null;
        var version = BitConverter.ToInt64(data, 0);
        var size = BitConverter.ToInt64(data, 8);
        DateTime deleted;
        try { deleted = DateTime.FromFileTimeUtc(BitConverter.ToInt64(data, 16)); } catch { return null; }
        string path;
        if (version == 2)
        {
            if (data.Length < 28) return null;
            var chars = BitConverter.ToInt32(data, 24);
            if (chars <= 0 || 28 + chars * 2 > data.Length) return null;
            path = Encoding.Unicode.GetString(data, 28, chars * 2);
        }
        else if (version == 1)
        {
            var len = Math.Min(520, data.Length - 24);
            path = Encoding.Unicode.GetString(data, 24, len);
        }
        else return null;
        path = path.TrimEnd('\0');
        var nul = path.IndexOf('\0');
        if (nul >= 0) path = path[..nul];
        return path.Length == 0 ? null : new Info(path, size, deleted);
    }

    /// <summary>Builds a <c>$I…</c> file (version 2). Used by the tests.</summary>
    public static byte[] Build(string originalPath, long size, DateTime deletedUtc)
    {
        var name = Encoding.Unicode.GetBytes(originalPath + "\0");
        var buf = new byte[28 + name.Length];
        BitConverter.GetBytes(2L).CopyTo(buf, 0);
        BitConverter.GetBytes(size).CopyTo(buf, 8);
        BitConverter.GetBytes(deletedUtc.ToFileTimeUtc()).CopyTo(buf, 16);
        BitConverter.GetBytes(name.Length / 2).CopyTo(buf, 24);
        name.CopyTo(buf, 28);
        return buf;
    }

    /// <summary>
    /// Looks in one account's Recycle Bin folder for the most recently removed copy of <paramref name="originalPath"/>
    /// (removed after <paramref name="removedAfterUtc"/>) and moves it back. Returns where it came back to, or null.
    /// </summary>
    public static string? Restore(string binFolder, string originalPath, DateTime removedAfterUtc)
    {
        if (!Directory.Exists(binFolder)) return null;
        (string iFile, string rFile, Info info)? best = null;
        foreach (var iFile in Directory.EnumerateFiles(binFolder, "$I*"))
        {
            Info? info;
            try { info = Parse(File.ReadAllBytes(iFile)); } catch { continue; }
            if (info == null || !PathRules.Same(info.OriginalPath, originalPath) || info.DeletedUtc < removedAfterUtc) continue;
            var rFile = Path.Combine(binFolder, "$R" + Path.GetFileName(iFile)[2..]);
            if (!File.Exists(rFile)) continue;
            if (best == null || info.DeletedUtc > best.Value.info.DeletedUtc) best = (iFile, rFile, info);
        }
        if (best == null) return null;
        var dir = Path.GetDirectoryName(originalPath)!;
        Directory.CreateDirectory(dir);
        var (b, ext) = PathRules.SplitName(originalPath);
        var back = SafeFiles.MoveUnchecked(best.Value.rFile, dir, b, ext);
        try { File.Delete(best.Value.iFile); } catch { /* harmless: Windows tidies up orphans */ }
        return back;
    }
}
