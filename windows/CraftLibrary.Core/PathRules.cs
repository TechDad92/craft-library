namespace CraftLibrary.Core;

/// <summary>
/// How paths are compared. Windows ignores capital letters in names, so "Flower.png" and
/// "flower.png" are the same file there. Tests can switch this to see Windows behaviour on Linux.
/// </summary>
public static class PathRules
{
    public static bool IgnoreCase { get; set; } = OperatingSystem.IsWindows();

    public static StringComparison Comparison => IgnoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    public static StringComparer Comparer => IgnoreCase ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    public static string Norm(string p) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(p)) is var n && n.Length > 0 ? n : Path.GetFullPath(p);

    public static bool Same(string a, string b) => string.Equals(Norm(a), Norm(b), Comparison);

    /// <summary>True when <paramref name="child"/> is the folder itself or anything inside it.</summary>
    public static bool IsInside(string child, string parent)
    {
        var c = Norm(child);
        var p = Norm(parent);
        if (string.Equals(c, p, Comparison)) return true;
        var withSep = p.EndsWith(Path.DirectorySeparatorChar) ? p : p + Path.DirectorySeparatorChar;
        return c.StartsWith(withSep, Comparison);
    }

    /// <summary>Splits "Pink Paper.png" into ("Pink Paper", "png"). The extension is kept as typed.</summary>
    public static (string Base, string Ext) SplitName(string fileNameOrPath)
    {
        var name = Path.GetFileName(fileNameOrPath);
        var dot = name.LastIndexOf('.');
        if (dot <= 0) return (name, "");
        return (name[..dot], name[(dot + 1)..]);
    }

    public static string WithExt(string baseName, string ext) => ext.Length > 0 ? baseName + "." + ext : baseName;
}

/// <summary>A problem worth telling the person about, in plain words.</summary>
public sealed class FriendlyException(string message) : Exception(message);
