namespace CraftLibrary.Core;

public enum DriveKind { Fixed, Removable, Network, Other }

/// <summary>The Windows-only pieces the removing logic needs. The app supplies the real ones; tests supply stand-ins.</summary>
public interface IPlatform
{
    DriveKind DriveKindOf(string path);
    /// <summary>Sends a file to the real Windows Recycle Bin.</summary>
    void SendToRecycleBin(string path);
    /// <summary>
    /// Puts a file back from the Recycle Bin into its original folder, never overwriting (a taken name gets " (2)").
    /// Returns where it came back to, or null when it can't be found there.
    /// </summary>
    string? RestoreFromRecycleBin(string originalPath, DateTime removedAfterUtc);
}

/// <summary>What the app remembers about a removed file, so it can be put back.</summary>
public sealed class RemovedEntry
{
    public string OrigPath { get; set; } = "";
    /// <summary>"recycle" = Windows Recycle Bin; "folder" = the "Removed files" folder on a portable drive.</summary>
    public string Method { get; set; } = "recycle";
    public string? RemovedPath { get; set; }
    public string Name { get; set; } = "";
    public string Ext { get; set; } = "";
    public string Size { get; set; } = "";
    public string Added { get; set; } = "";
    public DateTime RemovedAtUtc { get; set; }
    public string Id => OrigPath + "|" + RemovedAtUtc.Ticks;
}

public sealed class Remover(SafeFiles safe, IPlatform platform)
{
    /// <summary>Portable and network drives have no Recycle Bin.</summary>
    public bool NeedsRemovedFolder(string path) => platform.DriveKindOf(path) is DriveKind.Removable or DriveKind.Network;

    public RemovedEntry Remove(string path)
    {
        safe.AssertAllowed(path);
        if (!File.Exists(path)) throw new FriendlyException("That file isn’t there any more. It may have been moved, or the drive unplugged.");
        var (b, ext) = PathRules.SplitName(path);
        var info = new FileInfo(path);
        var entry = new RemovedEntry
        {
            OrigPath = path, Name = b, Ext = ext.ToLowerInvariant(), Size = FileKinds.HumanSize(info.Length),
            Added = FileKinds.ShortDate(info.CreationTimeUtc), RemovedAtUtc = DateTime.UtcNow
        };
        if (NeedsRemovedFolder(path))
        {
            var holder = SafeFiles.RemovedFolderFor(path);
            Directory.CreateDirectory(holder);
            entry.Method = "folder";
            entry.RemovedPath = SafeFiles.MoveUnchecked(path, holder, b, ext);
        }
        else
        {
            platform.SendToRecycleBin(path);
            entry.Method = "recycle";
        }
        return entry;
    }

    /// <summary>Puts a removed file back where it came from (never overwriting). Returns the path it came back to.</summary>
    public string Restore(RemovedEntry e)
    {
        var dir = Path.GetDirectoryName(e.OrigPath)!;
        safe.AssertAllowed(dir);
        if (e.Method == "folder")
        {
            if (e.RemovedPath == null || !File.Exists(e.RemovedPath)) throw new FriendlyException("Couldn’t find it in the “" + SafeFiles.RemovedFolderName + "” folder any more.");
            safe.AssertAllowed(e.RemovedPath);
            Directory.CreateDirectory(dir);
            var (b, ext) = PathRules.SplitName(e.OrigPath);
            return SafeFiles.MoveUnchecked(e.RemovedPath, dir, b, ext);
        }
        Directory.CreateDirectory(dir);
        return platform.RestoreFromRecycleBin(e.OrigPath, e.RemovedAtUtc.AddMinutes(-2))
            ?? throw new FriendlyException("Couldn’t find it in the Windows Recycle Bin. It may have been emptied, or you can put it back from there yourself.");
    }
}
