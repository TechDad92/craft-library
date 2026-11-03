using System.Globalization;

namespace CraftLibrary.Core;

/// <summary>
/// Works out every colour the screens use from the chosen theme, accent, strength, tint and contrast,
/// using the same rules as the original design: a rule applies when all its names are chosen;
/// a rule naming more things wins, and among equals the later rule wins.
/// </summary>
public static partial class Palette
{
    public sealed record Accent(string Id, string Label, string Bold, string Muted, string Pastel);
    public sealed record Tint(string Id, string Label, string Light, string Dark);

    public static readonly Accent[] Accents =
    [
        new("teal", "Teal", "#0E6B64", "#4F7A75", "#9ED6CF"), new("forest", "Forest green", "#2F6B2F", "#5A7556", "#A8D5B0"),
        new("olive", "Olive", "#5B6B1F", "#6B7044", "#CFD9A0"), new("blue", "Blue", "#1D4ED8", "#51698F", "#A9C6EE"),
        new("navy", "Navy", "#1E3A8A", "#45546E", "#A6B4D8"), new("violet", "Violet", "#6D28D9", "#6E5F93", "#C4B3EE"),
        new("plum", "Plum", "#6B3FA0", "#745A80", "#CDB4DB"), new("magenta", "Magenta", "#A21CAF", "#8A5A86", "#E8B4E2"),
        new("rose", "Rose", "#B4235A", "#9A5C6E", "#F2B5C4"), new("burgundy", "Burgundy", "#8C1D3A", "#7D4A56", "#D9A7B3"),
        new("terracotta", "Terracotta", "#B4471F", "#9B5E48", "#F0BCA4"), new("ochre", "Ochre", "#8A5A00", "#8A6F3E", "#EED39A"),
        new("slate", "Slate grey", "#475569", "#5E6773", "#BFC8D3"),
    ];

    public static readonly Tint[] Tints =
    [
        new("neutral", "Soft grey", "#F2F4F7", "#14181E"), new("cream", "Cream", "#F5EFE3", "#1A1712"),
        new("sky", "Sky", "#EAF1F8", "#111821"), new("blush", "Blush", "#F8ECEF", "#1C1417"),
        new("sage", "Sage", "#EBF2E8", "#131A14"), new("lilac", "Lilac", "#F0ECF7", "#17141F"),
    ];

    public static readonly (string Id, string Label)[] Textures =
        [("solid", "Plain"), ("dots", "Polka dots"), ("mat", "Cutting mat"), ("stripes", "Pinstripe"), ("wash", "Soft wash")];

    /// <summary>Folder colour labels: ten colours in three strengths.</summary>
    public static readonly string[] LabelNames = ["Red", "Orange", "Yellow", "Green", "Teal", "Blue", "Purple", "Pink", "Brown", "Grey"];
    public static readonly Dictionary<string, string[]> LabelColours = new()
    {
        ["pastel"] = ["#F2A9A0", "#F6C79B", "#F3DE8A", "#A8D5B0", "#9ED6CF", "#A9C6EE", "#C4B3EE", "#F2B5D4", "#D9C3AE", "#C6CDD6"],
        ["muted"] = ["#B5706A", "#C29A6E", "#BFA86A", "#7E9B7F", "#6F9A99", "#7D93B4", "#958AAE", "#B48A9F", "#9C8B7B", "#8A929C"],
        ["bold"] = ["#C0392B", "#D9822B", "#C9A227", "#3E8E5E", "#2A8C8C", "#3B6FB6", "#7D5BA6", "#C65A8E", "#8A6A4F", "#5E6773"],
    };

    public static string LabelName(string hex)
    {
        if (string.IsNullOrEmpty(hex)) return "No colour";
        foreach (var (tab, list) in LabelColours)
            for (var i = 0; i < list.Length; i++)
                if (string.Equals(list[i], hex, StringComparison.OrdinalIgnoreCase)) return LabelNames[i] + " (" + tab + ")";
        return "Custom colour " + hex.ToUpperInvariant();
    }

    public static string SwatchFor(Accent a, string strength) => strength switch { "muted" => a.Muted, "pastel" => a.Pastel, _ => a.Bold };

    public static IReadOnlyList<string> ClassesFor(Settings s)
    {
        var c = new List<string> { "t-" + s.Theme, "a-" + s.Accent, "bg-" + s.Tint };
        if (s.Strength == "muted") c.Add("mu");
        else if (s.Strength == "pastel") c.Add("pa");
        if (s.HighContrast) c.Add("hc");
        return c;
    }

    /// <summary>Every colour name (without the leading "--") and its value, for these settings.</summary>
    public static Dictionary<string, string> Resolve(Settings s)
    {
        var have = new HashSet<string>(ClassesFor(s));
        var winners = new Dictionary<string, (int Spec, int Order, string Value)>();
        for (var i = 0; i < Rules.Length; i++)
        {
            var (classes, vars) = Rules[i];
            if (!classes.All(have.Contains)) continue;
            foreach (var (name, value) in vars)
                if (!winners.TryGetValue(name, out var w) || classes.Length > w.Spec || (classes.Length == w.Spec && i >= w.Order))
                    winners[name] = (classes.Length, i, value);
        }
        var raw = winners.ToDictionary(k => k.Key[2..], v => v.Value.Value);
        // Follow references such as var(--accent).
        var outp = new Dictionary<string, string>();
        foreach (var k in raw.Keys)
        {
            var v = raw[k];
            for (var hops = 0; hops < 5 && v.StartsWith("var(--"); hops++)
                v = raw.TryGetValue(v[6..^1], out var next) ? next : "#FF00FF";
            outp[k] = v;
        }
        return outp;
    }

    /// <summary>Reads "#RRGGBB" or "rgba(r,g,b,a)" into (alpha, red, green, blue).</summary>
    public static (byte A, byte R, byte G, byte B) Parse(string css)
    {
        css = css.Trim();
        if (css.StartsWith('#') && css.Length == 7)
            return (255, Convert.ToByte(css[1..3], 16), Convert.ToByte(css[3..5], 16), Convert.ToByte(css[5..7], 16));
        if (css.StartsWith("rgba(") || css.StartsWith("rgb("))
        {
            var parts = css[(css.IndexOf('(') + 1)..^1].Split(',').Select(p => p.Trim()).ToArray();
            var a = parts.Length > 3 ? double.Parse(parts[3], CultureInfo.InvariantCulture) : 1;
            return ((byte)Math.Round(a * 255), byte.Parse(parts[0]), byte.Parse(parts[1]), byte.Parse(parts[2]));
        }
        throw new FormatException("Not a colour: " + css);
    }

    /// <summary>A darker shade (60%), used for the outline of a coloured folder icon.</summary>
    public static string Darker(string hex)
    {
        var (_, r, g, b) = Parse(hex);
        return $"#{(int)Math.Round(r * 0.6):X2}{(int)Math.Round(g * 0.6):X2}{(int)Math.Round(b * 0.6):X2}";
    }
}
