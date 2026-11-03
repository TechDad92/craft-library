namespace CraftLibrary.Tests;

/// <summary>A throwaway folder for one test, deleted afterwards.</summary>
public sealed class TestFolder : IDisposable
{
    public string Root { get; } = Directory.CreateTempSubdirectory("craft-test-").FullName;

    public string Dir(params string[] parts)
    {
        var p = Path.Combine([Root, .. parts]);
        Directory.CreateDirectory(p);
        return p;
    }

    public string File(string relative, string content = "x")
    {
        var p = Path.Combine(Root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        System.IO.File.WriteAllText(p, content);
        return p;
    }

    public string[] Names(params string[] parts) =>
        Directory.GetFiles(Path.Combine([Root, .. parts])).Select(Path.GetFileName).Order(StringComparer.Ordinal).ToArray()!;

    public void Dispose() { try { Directory.Delete(Root, true); } catch { } }
}

/// <summary>Stand-in for Windows: a pretend Recycle Bin and a list of which folders count as portable drives.</summary>
public sealed class FakePlatform(string binFolder) : CraftLibrary.Core.IPlatform
{
    public HashSet<string> PortableRoots { get; } = [];

    public CraftLibrary.Core.DriveKind DriveKindOf(string path) =>
        PortableRoots.Any(r => path.StartsWith(r, StringComparison.Ordinal)) ? CraftLibrary.Core.DriveKind.Removable : CraftLibrary.Core.DriveKind.Fixed;

    public void SendToRecycleBin(string path)
    {
        Directory.CreateDirectory(binFolder);
        var id = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        var ext = Path.GetExtension(path);
        System.IO.File.WriteAllBytes(Path.Combine(binFolder, "$I" + id + ext),
            CraftLibrary.Core.RecycleBinFiles.Build(path, new FileInfo(path).Length, DateTime.UtcNow));
        System.IO.File.Move(path, Path.Combine(binFolder, "$R" + id + ext));
    }

    public string? RestoreFromRecycleBin(string originalPath, DateTime removedAfterUtc) =>
        CraftLibrary.Core.RecycleBinFiles.Restore(binFolder, originalPath, removedAfterUtc);
}
