using System.IO.Enumeration;

namespace CraftLibrary.Core;

public sealed record FileEntry(string Path, string Dir, string Name, string Ext, long Size, DateTime CreatedUtc, DateTime ModifiedUtc);

public sealed record FolderEntry(string Path, string Name, int Depth, string Parent);

public sealed record ScanResult(string Root, bool Ok, IReadOnlyList<FolderEntry> Folders, IReadOnlyList<FileEntry> Files, bool Capped)
{
    public static ScanResult Missing(string root) => new(root, false, [], [], false);
}

/// <summary>
/// Reads folders quickly. Sizes and dates come straight from the folder listing,
/// so no file is opened or looked up one by one.
/// </summary>
public static class Scanner
{
    public const int MaxFiles = 60000;
    public const int MaxDepth = 14;

    private static readonly HashSet<string> SkipDirs = new(StringComparer.OrdinalIgnoreCase)
    {
        "$recycle.bin", "system volume information", "node_modules", ".git", SafeFiles.RemovedFolderName
    };

    private static readonly HashSet<string> InboxSkipExt = new(StringComparer.OrdinalIgnoreCase)
    {
        "exe", "msi", "dll", "lnk", "url", "ini", "tmp", "crdownload", "partial", "part", "download", "sys", "bat", "cmd"
    };

    /// <summary>Files that are never craft files: Office lock files, Windows and Mac housekeeping files.</summary>
    public static bool IsJunkFile(string name) =>
        name.StartsWith("~$", StringComparison.Ordinal) || name.StartsWith("._", StringComparison.Ordinal) ||
        name.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase) || name.Equals("thumbs.db", StringComparison.OrdinalIgnoreCase) ||
        name.Equals(".DS_Store", StringComparison.OrdinalIgnoreCase);

    /// <summary>Temporary files browsers write while downloading; changes to these are ignored.</summary>
    public static bool IsTemporaryDownload(string name) =>
        name.EndsWith(".crdownload", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".part", StringComparison.OrdinalIgnoreCase) ||
        name.EndsWith(".partial", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase) ||
        name.EndsWith(".download", StringComparison.OrdinalIgnoreCase) || IsJunkFile(name);

    private readonly record struct Item(bool IsDir, string Path, string Name, long Size, DateTime Created, DateTime Modified);

    private static readonly EnumerationOptions Options = new()
    {
        RecurseSubdirectories = false,
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.System,
        ReturnSpecialDirectories = false,
    };

    private static List<Item> List(string dir)
    {
        var e = new FileSystemEnumerable<Item>(dir,
            (ref FileSystemEntry x) => new Item(x.IsDirectory, x.ToFullPath(), x.FileName.ToString(), x.Length,
                x.CreationTimeUtc.UtcDateTime, x.LastWriteTimeUtc.UtcDateTime), Options)
        {
            // Folder shortcuts (junctions and links) are skipped, so the scan can never go round in circles.
            ShouldIncludePredicate = (ref FileSystemEntry x) => !(x.IsDirectory && (x.Attributes & FileAttributes.ReparsePoint) != 0)
        };
        try { return e.ToList(); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return []; }
    }

    /// <param name="recursive">Look inside subfolders too (locations). The "To sort" folder is read one level only.</param>
    /// <param name="filterInbox">Leave out programs, shortcuts and half-finished downloads (for the "To sort" folder).</param>
    public static ScanResult Scan(string root, bool recursive, bool filterInbox = false, CancellationToken ct = default)
    {
        if (!Directory.Exists(root)) return ScanResult.Missing(root);
        var folders = new List<FolderEntry>();
        var files = new List<FileEntry>();
        var capped = false;

        void Visit(string dir, int depth)
        {
            ct.ThrowIfCancellationRequested();
            var subdirs = new List<string>();
            foreach (var it in List(dir))
            {
                if (it.IsDir)
                {
                    if (!recursive) continue;
                    if (it.Name.StartsWith('.') || it.Name.StartsWith('$') || SkipDirs.Contains(it.Name)) continue;
                    folders.Add(new FolderEntry(it.Path, it.Name, depth, dir));
                    subdirs.Add(it.Path);
                    continue;
                }
                if (IsJunkFile(it.Name)) continue;
                var (b, ext) = PathRules.SplitName(it.Name);
                if (filterInbox && InboxSkipExt.Contains(ext)) continue;
                if (files.Count >= MaxFiles) { capped = true; return; }
                var created = it.Created == default || it.Created.Year < 1980 ? it.Modified : it.Created;
                files.Add(new FileEntry(it.Path, dir, b, ext.ToLowerInvariant(), it.Size, created, it.Modified));
            }
            if (depth < MaxDepth)
                foreach (var sd in subdirs.OrderBy(s => s, StringComparer.OrdinalIgnoreCase))
                {
                    if (capped) return;
                    Visit(sd, depth + 1);
                }
        }

        Visit(PathRules.Norm(root), 0);
        return new ScanResult(root, true, folders, files, capped);
    }
}
