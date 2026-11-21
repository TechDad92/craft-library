using System.Globalization;
using System.Text.RegularExpressions;

namespace CraftLibrary.Core;

public enum FileKind { Paper, Doc, Pdf, Vector, Zip, Font, Unknown }

public static partial class FileKinds
{
    [GeneratedRegex("^(jpe?g|png|gif|bmp|webp|tiff?|psd|heic)$")] private static partial Regex PaperRx();
    [GeneratedRegex("^(docx?|odt|rtf|pages)$")] private static partial Regex DocRx();
    [GeneratedRegex(@"^(svg|eps|ai|dxf|studio3?|cut|fcm|scut\d*|plt|cdr)$")] private static partial Regex VectorRx();
    [GeneratedRegex("^(zip|rar|7z)$")] private static partial Regex ZipRx();
    [GeneratedRegex("^(ttf|otf|woff2?)$")] private static partial Regex FontRx();

    public static FileKind KindFor(string ext)
    {
        var e = (ext ?? "").ToLowerInvariant();
        if (PaperRx().IsMatch(e)) return FileKind.Paper;
        if (DocRx().IsMatch(e)) return FileKind.Doc;
        if (e == "pdf") return FileKind.Pdf;
        if (VectorRx().IsMatch(e)) return FileKind.Vector;
        if (ZipRx().IsMatch(e)) return FileKind.Zip;
        if (FontRx().IsMatch(e)) return FileKind.Font;
        return FileKind.Unknown;
    }

    private static readonly Dictionary<string, string> Types = new(StringComparer.OrdinalIgnoreCase)
    {
        ["jpg"] = "JPEG image", ["jpeg"] = "JPEG image", ["png"] = "PNG image", ["gif"] = "GIF image", ["webp"] = "WebP image",
        ["bmp"] = "Bitmap image", ["tif"] = "TIFF image", ["tiff"] = "TIFF image", ["heic"] = "HEIC photo", ["psd"] = "Photoshop file",
        ["svg"] = "SVG cut file", ["eps"] = "EPS vector file", ["ai"] = "Illustrator file", ["dxf"] = "DXF cut file",
        ["studio3"] = "Silhouette Studio file", ["studio"] = "Silhouette Studio file", ["docx"] = "Word document",
        ["doc"] = "Word document (older)", ["pdf"] = "PDF document", ["zip"] = "Zip bundle", ["rar"] = "RAR bundle",
        ["7z"] = "7-Zip bundle", ["ttf"] = "Font (TrueType)", ["otf"] = "Font (OpenType)", ["cut"] = "CUT file", ["fcm"] = "ScanNCut file"
    };

    public static string TypeLabel(string ext) =>
        Types.TryGetValue(ext ?? "", out var t) ? t : ((string.IsNullOrEmpty(ext) ? "NO" : ext.ToUpperInvariant()) + " file");

    /// <summary>Same wording as before: 2.4 MB, 96 KB, 812 bytes.</summary>
    public static string HumanSize(long b)
    {
        var c = CultureInfo.InvariantCulture;
        if (b >= 1_000_000_000) return (b / 1e9).ToString("0.0", c) + " GB";
        if (b >= 1_000_000) return (b / 1e6).ToString("0.0", c) + " MB";
        if (b >= 1_000) return Math.Round(b / 1e3).ToString("0", c) + " KB";
        return b + " bytes";
    }

    public static string ShortDate(DateTime utc) => utc.ToLocalTime().ToString("d MMM yyyy", CultureInfo.GetCultureInfo("en-GB"));

    /// <summary>
    /// The kinds of file a crafter works with: pictures and papers, PDFs, cut files, Word print sheets, zip bundles
    /// and fonts. The setup guide and the top of Settings › "Open files with" list only these; every other kind of file
    /// is under "Change how other kinds of files open".
    /// </summary>
    public static readonly IReadOnlySet<string> CraftExts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "jpg", "jpeg", "png", "gif", "bmp", "webp", "tif", "tiff", "psd",
        "pdf",
        "svg", "eps", "ai", "dxf", "studio3", "studio", "cut", "fcm",
        "docx", "doc",
        "zip",
        "ttf", "otf",
    };

    public static bool IsCraft(string ext) => CraftExts.Contains(ext ?? "");

    /// <summary>Craft file types always listed under "Open files with", even before any are found.</summary>
    public static readonly string[] CommonExts = ["jpg", "png", "pdf", "docx", "svg", "psd", "eps", "dxf", "studio3", "zip", "ttf", "otf"];
}
