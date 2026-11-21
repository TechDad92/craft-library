namespace CraftLibrary.Core;

/// <summary>
/// Every change to a file goes through here, and every request is checked so the app only ever
/// touches folders the person has chosen. Moving or renaming never overwrites anything.
/// </summary>
public sealed class SafeFiles
{
    public const string RemovedFolderName = "Removed files (Craft Library)";

    private readonly object _lock = new();
    private string[] _roots = [];
    private readonly string[] _extraAllowed;

    public SafeFiles(params string[] extraAllowed) => _extraAllowed = extraAllowed;

    /// <summary>The folders she has chosen: her locations plus the "To sort" folder.</summary>
    public IReadOnlyList<string> AllowedRoots { get { lock (_lock) return _roots; } }

    public void SetRoots(IEnumerable<string> roots)
    {
        var list = roots.Where(r => !string.IsNullOrWhiteSpace(r)).Select(PathRules.Norm).Distinct(PathRules.Comparer).ToArray();
        lock (_lock) _roots = list;
    }

    public bool IsAllowed(string? p)
    {
        if (string.IsNullOrWhiteSpace(p)) return false;
        string full;
        try { full = Path.GetFullPath(p); } catch { return false; }
        var roots = AllowedRoots;
        if (roots.Any(r => PathRules.IsInside(full, r))) return true;
        if (_extraAllowed.Any(r => PathRules.IsInside(full, r))) return true;
        // The "Removed files" folder at the top of a drive that holds one of the chosen folders.
        return roots.Any(r => PathRules.IsInside(full, RemovedFolderFor(r)));
    }

    public void AssertAllowed(string? p)
    {
        if (!IsAllowed(p)) throw new FriendlyException("That file is outside the folders you chose, so the app left it alone.");
    }

    public static string RemovedFolderFor(string anyPathOnDrive) =>
        Path.Combine(Path.GetPathRoot(Path.GetFullPath(anyPathOnDrive)) ?? "", RemovedFolderName);

    // ---------- names ----------

    private static readonly char[] BadChars = ['\\', '/', ':', '*', '?', '"', '<', '>', '|'];
    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    /// <summary>The same name rules Windows uses, checked before any move or rename.</summary>
    public static string CheckName(string? name)
    {
        var n = (name ?? "").Trim();
        if (n.Length == 0) throw new FriendlyException("Please type a name.");
        if (n.IndexOfAny(BadChars) >= 0 || n.Any(char.IsControl)) throw new FriendlyException("Names can’t contain any of these: \\ / : * ? \" < > |");
        if (n.EndsWith('.') || n.EndsWith(' ')) throw new FriendlyException("Names can’t end with a full stop or a space.");
        if (ReservedNames.Contains(n)) throw new FriendlyException("Windows keeps the name “" + n + "” for itself. Please choose a different name.");
        if (n.Length > 200) throw new FriendlyException("That name is too long. Please make it shorter.");
        return n;
    }

    /// <summary>"Pink Paper.png", then "Pink Paper (2).png", "Pink Paper (3).png" and so on.</summary>
    public static string UniquePath(string dir, string baseName, string ext)
    {
        var candidate = Path.Combine(dir, PathRules.WithExt(baseName, ext));
        var n = 2;
        while (File.Exists(candidate) || Directory.Exists(candidate))
        {
            candidate = Path.Combine(dir, PathRules.WithExt($"{baseName} ({n})", ext));
            n++;
        }
        return candidate;
    }

    // ---------- moving and renaming ----------

    /// <summary>
    /// Moves a file into <paramref name="destDir"/>, optionally under a new name, and returns where it ended up.
    /// Never overwrites. Across drives it copies, checks the copy is the same size, and only then removes the original.
    /// </summary>
    public string Move(string src, string destDir, string? wantedBase = null)
    {
        AssertAllowed(src);
        AssertAllowed(destDir);
        if (wantedBase != null) wantedBase = CheckName(wantedBase);
        var (srcBase, ext) = PathRules.SplitName(src);
        if (PathRules.Same(Path.GetDirectoryName(Path.GetFullPath(src))!, destDir) && (wantedBase == null || wantedBase == srcBase)) return src;
        if (!File.Exists(src)) throw new FriendlyException("That file isn’t there any more. It may have been moved, or the drive unplugged.");
        if (!Directory.Exists(destDir)) throw new FriendlyException("That folder isn’t there any more. It may have been moved, or the drive unplugged.");
        return MoveUnchecked(src, destDir, wantedBase ?? srcBase, ext);
    }

    /// <summary>Move with the checks already done (also used for the "Removed files" folder).</summary>
    public static string MoveUnchecked(string src, string destDir, string baseName, string ext)
    {
        for (var attempt = 0; ; attempt++)
        {
            var dest = UniquePath(destDir, baseName, ext);
            // Belt and braces: the file must land directly in the folder asked for, never anywhere else.
            if (!PathRules.Same(Path.GetDirectoryName(dest)!, destDir)) throw new FriendlyException("That name isn’t allowed, so the file was not moved.");
            try
            {
                if (SameDrive(src, dest)) File.Move(src, dest, overwrite: false);
                else CopyCheckThenDelete(src, dest);
                return dest;
            }
            catch (IOException) when (attempt < 3 && File.Exists(dest) && File.Exists(src))
            {
                // Another file took that name in the meantime: try the next free name.
            }
        }
    }

    private static bool SameDrive(string a, string b) =>
        string.Equals(Path.GetPathRoot(Path.GetFullPath(a)), Path.GetPathRoot(Path.GetFullPath(b)), StringComparison.OrdinalIgnoreCase);

    private static void CopyCheckThenDelete(string src, string dest)
    {
        File.Copy(src, dest, overwrite: false);
        var a = new FileInfo(src);
        var b = new FileInfo(dest);
        if (a.Length != b.Length)
        {
            try { File.Delete(dest); } catch { /* leave it; the original is untouched */ }
            throw new FriendlyException("The copy did not match the original, so the file was not moved.");
        }
        try { File.SetCreationTimeUtc(dest, a.CreationTimeUtc); File.SetLastWriteTimeUtc(dest, a.LastWriteTimeUtc); } catch { }
        File.Delete(src);
    }

    /// <summary>
    /// Copies a file into <paramref name="destDir"/> and returns the copy's path. Never overwrites: a taken name gets
    /// " (2)". The copy is checked to be the same size as the original, and removed again if it isn't.
    /// </summary>
    public string Copy(string src, string destDir)
    {
        AssertAllowed(src);
        AssertAllowed(destDir);
        if (!File.Exists(src)) throw new FriendlyException("That file isn’t there any more. It may have been moved, or the drive unplugged.");
        if (!Directory.Exists(destDir)) throw new FriendlyException("That folder isn’t there any more. It may have been moved, or the drive unplugged.");
        var (b, ext) = PathRules.SplitName(src);
        for (var attempt = 0; ; attempt++)
        {
            var dest = UniquePath(destDir, b, ext);
            if (!PathRules.Same(Path.GetDirectoryName(dest)!, destDir)) throw new FriendlyException("That name isn’t allowed, so the file was not copied.");
            try
            {
                File.Copy(src, dest, overwrite: false);
            }
            catch (IOException) when (attempt < 3 && File.Exists(dest))
            {
                continue; // another file took that name in the meantime: try the next free name
            }
            if (new FileInfo(src).Length != new FileInfo(dest).Length)
            {
                try { File.Delete(dest); } catch { }
                throw new FriendlyException("The copy did not match the original, so it was removed again.");
            }
            return dest;
        }
    }

    /// <summary>Renames a file in its own folder, keeping its extension. Returns the new full path.</summary>
    public string Rename(string src, string newBase)
    {
        AssertAllowed(src);
        var name = CheckName(newBase);
        if (!File.Exists(src)) throw new FriendlyException("That file isn’t there any more. It may have been moved, or the drive unplugged.");
        var (_, ext) = PathRules.SplitName(src);
        var dir = Path.GetDirectoryName(Path.GetFullPath(src))!;
        var dest = Path.Combine(dir, PathRules.WithExt(name, ext));
        if (string.Equals(PathRules.Norm(dest), PathRules.Norm(src), StringComparison.Ordinal)) return src;
        if (PathRules.Same(dest, src))
        {
            // Only the capital letters changed. Windows counts these as the same name,
            // so the file goes through a temporary name to make the change stick.
            var tmp = UniquePath(dir, name + " (renaming)", ext);
            File.Move(src, tmp);
            try { File.Move(tmp, dest); }
            catch { try { File.Move(tmp, src); } catch { } throw; }
            return dest;
        }
        if (File.Exists(dest) || Directory.Exists(dest)) throw new FriendlyException("There’s already a file called “" + Path.GetFileName(dest) + "” in this folder.");
        File.Move(src, dest, overwrite: false);
        return dest;
    }
}
