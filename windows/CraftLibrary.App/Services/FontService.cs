using System.Windows.Media;
using CraftLibrary.App.Native;
using CraftLibrary.Core;
using Microsoft.Win32;

namespace CraftLibrary.App.Services;

/// <summary>Font files: showing them in their own style, and installing them for her Windows account (no administrator needed).</summary>
public static class FontService
{
    private const string RegKey = @"Software\Microsoft\Windows NT\CurrentVersion\Fonts";
    private static readonly Dictionary<string, FontFamily?> Families = new(StringComparer.OrdinalIgnoreCase);

    private static string UserFontsDir =>
        System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "Windows", "Fonts");

    private static string WindowsFontsDir => Environment.GetFolderPath(Environment.SpecialFolder.Fonts);

    /// <summary>The font inside a font file, so its preview can be written in it.</summary>
    public static FontFamily? FamilyFromFile(string path)
    {
        lock (Families)
        {
            if (Families.TryGetValue(path, out var f)) return f;
            try { f = System.Windows.Media.Fonts.GetFontFamilies(new Uri(path)).FirstOrDefault(); }
            catch { f = null; }
            Families[path] = f;
            return f;
        }
    }

    /// <summary>Names of every font installed on this computer, A to Z.</summary>
    public static List<string> InstalledFamilies()
    {
        var names = new SortedSet<string>(StringComparer.CurrentCultureIgnoreCase);
        foreach (var f in System.Windows.Media.Fonts.SystemFontFamilies)
        {
            var n = f.FamilyNames.TryGetValue(System.Windows.Markup.XmlLanguage.GetLanguage("en-us"), out var en) ? en : f.Source;
            if (!string.IsNullOrWhiteSpace(n)) names.Add(n);
        }
        // Fonts installed just for her account live in their own folder.
        try
        {
            if (Directory.Exists(UserFontsDir))
                foreach (var file in Directory.EnumerateFiles(UserFontsDir))
                    if (FamilyFromFile(file) is { } fam && fam.FamilyNames.Values.FirstOrDefault() is { } n) names.Add(n);
        }
        catch (IOException) { }
        return names.ToList();
    }

    public static bool IsInstalled(string path)
    {
        var name = System.IO.Path.GetFileName(path);
        return File.Exists(System.IO.Path.Combine(UserFontsDir, name)) || File.Exists(System.IO.Path.Combine(WindowsFontsDir, name));
    }

    private static string Label(string path)
    {
        var (b, ext) = PathRules.SplitName(path);
        return b + (ext.Equals("otf", StringComparison.OrdinalIgnoreCase) ? " (OpenType)" : " (TrueType)");
    }

    public static void Install(string path)
    {
        Directory.CreateDirectory(UserFontsDir);
        var target = System.IO.Path.Combine(UserFontsDir, System.IO.Path.GetFileName(path));
        if (!File.Exists(target)) File.Copy(path, target);
        using (var key = Registry.CurrentUser.CreateSubKey(RegKey)) key.SetValue(Label(path), target, RegistryValueKind.String);
        NativeMethods.AddFontResourceW(target);
        Announce();
    }

    public static void Uninstall(string path)
    {
        var target = System.IO.Path.Combine(UserFontsDir, System.IO.Path.GetFileName(path));
        NativeMethods.RemoveFontResourceW(target);
        using (var key = Registry.CurrentUser.OpenSubKey(RegKey, writable: true)) key?.DeleteValue(Label(path), throwOnMissingValue: false);
        try { File.Delete(target); } catch (IOException) { /* still in use by a program: Windows removes it after a restart */ }
        Announce();
    }

    /// <summary>Tells open programs the list of fonts has changed.</summary>
    private static void Announce() =>
        NativeMethods.SendMessageTimeout(NativeMethods.HWND_BROADCAST, NativeMethods.WM_FONTCHANGE, IntPtr.Zero, IntPtr.Zero, 2, 1000, out _);
}
