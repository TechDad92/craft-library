namespace CraftLibrary.Core;

/// <summary>
/// Folders the app refuses to work in, so it can never be pointed at Windows itself,
/// a program's folder, or a whole system drive.
/// </summary>
public sealed record FolderRules(string Home, string SystemDriveRoot, IReadOnlyList<string> Forbidden)
{
    public static FolderRules ForThisComputer(string appDataDir)
    {
        string? Env(string n) => Environment.GetEnvironmentVariable(n);
        var sysRoot = Path.GetPathRoot(Env("SystemRoot") ?? Env("WINDIR") ?? Environment.GetFolderPath(Environment.SpecialFolder.Windows));
        if (string.IsNullOrEmpty(sysRoot)) sysRoot = OperatingSystem.IsWindows() ? @"C:\" : "/";
        var forbidden = new[]
        {
            Env("SystemRoot"), Env("WINDIR"), Env("ProgramFiles"), Env("ProgramFiles(x86)"), Env("ProgramW6432"), Env("ProgramData"),
            Env("APPDATA"), Env("LOCALAPPDATA"), appDataDir
        }.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s!).ToList();
        return new FolderRules(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), sysRoot, forbidden);
    }

    /// <summary>Returns a friendly reason the folder can't be used, or "" when it is fine.</summary>
    public string Problem(string folder)
    {
        var n = PathRules.Norm(folder);
        var root = Path.GetPathRoot(n) ?? "";
        var isWholeDrive = PathRules.Same(n, root);
        if (isWholeDrive && PathRules.Same(root, SystemDriveRoot))
            return OperatingSystem.IsWindows() || root.Length > 1
                ? "Please choose a folder, not the whole " + root.TrimEnd('\\', '/') + " drive. (Portable drives are fine to choose whole.)"
                : "Please choose a folder, not the whole computer.";
        if (!string.IsNullOrEmpty(Home) && PathRules.Same(n, Home))
            return "That is your whole user folder, which has lots of Windows files in it. Please choose a folder inside it, like Documents or a craft folder.";
        foreach (var bad in Forbidden)
            if (PathRules.IsInside(n, bad))
                return "That folder belongs to Windows or a program, so the app won’t work in it. Please choose a different folder.";
        return "";
    }
}
