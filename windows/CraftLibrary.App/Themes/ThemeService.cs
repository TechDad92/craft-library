using System.Windows;
using System.Windows.Media;
using CraftLibrary.App.Services;
using CraftLibrary.Core;

namespace CraftLibrary.App.Themes;

/// <summary>
/// Turns her settings into the colours, background pattern, fonts and spacing the screens use.
/// Screens refer to these by name (for example {DynamicResource B.accent}), so a change shows straight away.
/// </summary>
public static class ThemeService
{
    private static readonly Uri FontBase = new("pack://application:,,,/");

    public static readonly (string Id, string Label, string Group, string Family)[] BuiltInFonts =
    [
        ("figtree", "Figtree (app default)", "default", "./Fonts/#Figtree"),
        ("atkinson", "Atkinson Hyperlegible", "easy", "./Fonts/#Atkinson Hyperlegible"),
        ("lexend", "Lexend", "easy", "./Fonts/#Lexend"),
        ("opendyslexic", "OpenDyslexic", "easy", "./Fonts/#OpenDyslexic"),
    ];

    public static FontFamily FontFor(string id)
    {
        foreach (var f in BuiltInFonts)
            if (f.Id == id) return new FontFamily(FontBase, f.Family + ", Segoe UI");
        if (id.StartsWith("sys-", StringComparison.Ordinal)) return new FontFamily(id[4..] + ", Segoe UI");
        if (id.StartsWith("lib-", StringComparison.Ordinal) && FontService.FamilyFromFile(id[4..]) is { } lib) return lib;
        return new FontFamily(FontBase, "./Fonts/#Figtree, Segoe UI");
    }

    public static Color ToColor(string css)
    {
        var (a, r, g, b) = Palette.Parse(css);
        return Color.FromArgb(a, r, g, b);
    }

    private static SolidColorBrush Brush(string css)
    {
        var b = new SolidColorBrush(ToColor(css));
        b.Freeze();
        return b;
    }

    public static void Apply(ResourceDictionary res, Settings s)
    {
        var c = Palette.Resolve(s);
        foreach (var (name, value) in c)
        {
            if (name.StartsWith("tex-") || name is "mat" or "mat-sz") continue;
            res["B." + name] = Brush(value);
            res["C." + name] = ToColor(value);
        }
        // Folder icons without a colour label (the original's warm yellow folder).
        res["B.folderFill"] = Brush("#F6D58E");
        res["B.folderLine"] = Brush("#B88A2E");
        res["B.badge"] = Brush("#1C2430");
        res["B.texture"] = Texture(s.Texture, c);

        foreach (var (key, _) in Settings.FontSections) res["F." + key] = FontFor(s.Fonts.GetValueOrDefault(key, "figtree"));

        res["Zoom"] = s.TextSize switch { "s" => 0.9, "l" => 1.15, _ => 1.0 };
        res["LineHeight"] = s.LineSpacing switch { "relaxed" => 22.0, "loose" => 26.0, _ => double.NaN };
        var gap = s.Density switch { "compact" => 8.0, "spacious" => 26.0, _ => 16.0 };
        res["TileGap"] = gap;
        res["TileMargin"] = new Thickness(gap / 2);
        res["RowMargin"] = new Thickness(24 - gap / 2, 0, 24 - gap / 2, 0);
        res["SideRowHeight"] = s.Density switch { "compact" => 36.0, "spacious" => 52.0, _ => 44.0 };
        res["CaptionPad"] = s.Density switch { "compact" => new Thickness(8, 6, 8, 8), "spacious" => new Thickness(14, 14, 14, 16), _ => new Thickness(12, 10, 12, 12) };
        res["V.btnIcon"] = s.BtnLabels == "words" ? Visibility.Collapsed : Visibility.Visible;
        res["V.btnWord"] = s.BtnLabels == "icons" ? Visibility.Collapsed : Visibility.Visible;
        res["TipsOn"] = s.Tips;
    }

    /// <summary>The background patterns from the original design, drawn as repeating tiles.</summary>
    private static Brush Texture(string id, Dictionary<string, string> c)
    {
        var a = ToColor(c.GetValueOrDefault("tex-a", "rgba(28,36,48,0.05)"));
        var b = ToColor(c.GetValueOrDefault("tex-b", "rgba(28,36,48,0.1)"));
        Brush result;
        switch (id)
        {
            case "dots":
            {
                var g = new DrawingGroup();
                g.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(0, 0, 18, 18))));
                g.Children.Add(new GeometryDrawing(new SolidColorBrush(b), null, new EllipseGeometry(new Point(9, 9), 1.75, 1.75)));
                result = Tile(g, 18);
                break;
            }
            case "mat":
            {
                var g = new DrawingGroup();
                g.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(0, 0, 80, 80))));
                var thin = new SolidColorBrush(a);
                for (var i = 0; i < 80; i += 16)
                {
                    g.Children.Add(new GeometryDrawing(thin, null, new RectangleGeometry(new Rect(i, 0, 1, 80))));
                    g.Children.Add(new GeometryDrawing(thin, null, new RectangleGeometry(new Rect(0, i, 80, 1))));
                }
                var thick = new SolidColorBrush(b);
                g.Children.Add(new GeometryDrawing(thick, null, new RectangleGeometry(new Rect(0, 0, 1, 80))));
                g.Children.Add(new GeometryDrawing(thick, null, new RectangleGeometry(new Rect(0, 0, 80, 1))));
                result = Tile(g, 80);
                break;
            }
            case "stripes":
            {
                var lg = new LinearGradientBrush { MappingMode = BrushMappingMode.Absolute, SpreadMethod = GradientSpreadMethod.Repeat, StartPoint = new Point(0, 0), EndPoint = new Point(8.485, 8.485) };
                lg.GradientStops.Add(new GradientStop(a, 0)); lg.GradientStops.Add(new GradientStop(a, 0.1667));
                lg.GradientStops.Add(new GradientStop(Colors.Transparent, 0.1667)); lg.GradientStops.Add(new GradientStop(Colors.Transparent, 1));
                result = lg;
                break;
            }
            case "wash":
            {
                var soft = ToColor(c.GetValueOrDefault("accent-soft", "#E6F4F2"));
                var g = new DrawingGroup();
                g.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(0, 0, 100, 100))));
                void Glow(double x, double y, double r)
                {
                    var rg = new RadialGradientBrush(soft, Color.FromArgb(0, soft.R, soft.G, soft.B))
                    { MappingMode = BrushMappingMode.Absolute, Center = new Point(x, y), GradientOrigin = new Point(x, y), RadiusX = r, RadiusY = r };
                    g.Children.Add(new GeometryDrawing(rg, null, new RectangleGeometry(new Rect(0, 0, 100, 100))));
                }
                Glow(12, 18, 38); Glow(88, 72, 42); Glow(50, 100, 32);
                result = new DrawingBrush(g) { Stretch = Stretch.Fill };
                break;
            }
            default:
                result = Brushes.Transparent;
                break;
        }
        if (result.CanFreeze) result.Freeze();
        return result;
    }

    private static DrawingBrush Tile(Drawing d, double size) => new(d)
    {
        TileMode = TileMode.Tile,
        Viewport = new Rect(0, 0, size, size),
        ViewportUnits = BrushMappingMode.Absolute,
        Viewbox = new Rect(0, 0, size, size),
        ViewboxUnits = BrushMappingMode.Absolute,
        Stretch = Stretch.None,
    };
}
