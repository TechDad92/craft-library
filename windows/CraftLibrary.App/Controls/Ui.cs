using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace CraftLibrary.App.Controls;

/// <summary>Small settings that can be put on any button or box from the screen layouts.</summary>
public static class Ui
{
    /// <summary>Shows the dashed "stitched" line inside a button (on by default).</summary>
    public static readonly DependencyProperty StitchedProperty = DependencyProperty.RegisterAttached("Stitched", typeof(bool), typeof(Ui), new FrameworkPropertyMetadata(true));
    public static bool GetStitched(DependencyObject o) => (bool)o.GetValue(StitchedProperty);
    public static void SetStitched(DependencyObject o, bool v) => o.SetValue(StitchedProperty, v);

    /// <summary>Marks a row or option as the current one.</summary>
    public static readonly DependencyProperty ActiveProperty = DependencyProperty.RegisterAttached("Active", typeof(bool), typeof(Ui), new FrameworkPropertyMetadata(false));
    public static bool GetActive(DependencyObject o) => (bool)o.GetValue(ActiveProperty);
    public static void SetActive(DependencyObject o, bool v) => o.SetValue(ActiveProperty, v);

    public static readonly DependencyProperty RadiusProperty = DependencyProperty.RegisterAttached("Radius", typeof(CornerRadius), typeof(Ui), new FrameworkPropertyMetadata(new CornerRadius(10)));
    public static CornerRadius GetRadius(DependencyObject o) => (CornerRadius)o.GetValue(RadiusProperty);
    public static void SetRadius(DependencyObject o, CornerRadius v) => o.SetValue(RadiusProperty, v);

    /// <summary>Grey hint text shown in an empty box.</summary>
    public static readonly DependencyProperty PlaceholderProperty = DependencyProperty.RegisterAttached("Placeholder", typeof(string), typeof(Ui), new FrameworkPropertyMetadata(""));
    public static string GetPlaceholder(DependencyObject o) => (string)o.GetValue(PlaceholderProperty);
    public static void SetPlaceholder(DependencyObject o, string v) => o.SetValue(PlaceholderProperty, v);

    /// <summary>Icon shown before a button's words.</summary>
    public static readonly DependencyProperty GlyphProperty = DependencyProperty.RegisterAttached("Glyph", typeof(Geometry), typeof(Ui), new FrameworkPropertyMetadata(null));
    public static Geometry? GetGlyph(DependencyObject o) => (Geometry?)o.GetValue(GlyphProperty);
    public static void SetGlyph(DependencyObject o, Geometry? v) => o.SetValue(GlyphProperty, v);

    /// <summary>Puts the keyboard in this box as soon as it appears.</summary>
    public static readonly DependencyProperty FocusOnShowProperty = DependencyProperty.RegisterAttached("FocusOnShow", typeof(bool), typeof(Ui),
        new FrameworkPropertyMetadata(false, (o, e) =>
        {
            if (o is FrameworkElement fe && (bool)e.NewValue)
            {
                void Go() => fe.Dispatcher.BeginInvoke(() => { fe.Focus(); Keyboard.Focus(fe); if (fe is TextBox tb) tb.SelectAll(); }, System.Windows.Threading.DispatcherPriority.Input);
                fe.IsVisibleChanged += (_, v) => { if ((bool)v.NewValue) Go(); };
                if (fe.IsVisible) Go();
            }
        }));
    public static bool GetFocusOnShow(DependencyObject o) => (bool)o.GetValue(FocusOnShowProperty);
    public static void SetFocusOnShow(DependencyObject o, bool v) => o.SetValue(FocusOnShowProperty, v);
}

/// <summary>true → shown, false → hidden. Add "not" as the parameter to flip it.</summary>
public sealed class VisibleIf : IValueConverter
{
    public object Convert(object? value, Type t, object? p, CultureInfo c)
    {
        var on = value switch
        {
            bool b => b,
            string s => s.Length > 0,
            int i => i != 0,
            System.Collections.ICollection col => col.Count > 0,
            null => false,
            _ => true,
        };
        if (p as string == "not") on = !on;
        return on ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? v, Type t, object? p, CultureInfo c) => throw new NotSupportedException();
}

/// <summary>Colour text such as "#9ED6CF" → a brush.</summary>
public sealed class HexBrush : IValueConverter
{
    public object? Convert(object? value, Type t, object? p, CultureInfo c)
    {
        if (value is not string s || s.Length == 0) return p is string fallback && fallback.Length > 0 ? Application.Current.TryFindResource(fallback) : null;
        try { var b = new SolidColorBrush(Themes.ThemeService.ToColor(s)); b.Freeze(); return b; } catch { return null; }
    }

    public object ConvertBack(object? v, Type t, object? p, CultureInfo c) => throw new NotSupportedException();
}

/// <summary>Flips true and false.</summary>
public sealed class Not : IValueConverter
{
    public object Convert(object? value, Type t, object? p, CultureInfo c) => value is not true;
    public object ConvertBack(object? value, Type t, object? p, CultureInfo c) => value is not true;
}

/// <summary>true → the side named in the parameter ("Right|Left"); false → the other one.</summary>
public sealed class SideIf : IValueConverter
{
    public object Convert(object? value, Type t, object? p, CultureInfo c)
    {
        var parts = ((p as string) ?? "Right|Left").Split('|');
        return Enum.Parse(t.IsEnum ? t : typeof(Dock), value is true ? parts[0] : parts[1]);
    }

    public object ConvertBack(object? v, Type t, object? p, CultureInfo c) => throw new NotSupportedException();
}

/// <summary>A thumbnail as a fill for a rounded box (the box clips it to its rounded corners).</summary>
public sealed class ThumbFill : IValueConverter
{
    public object? Convert(object? value, Type t, object? p, CultureInfo c)
    {
        if (value is not ImageSource img) return null;
        var b = new ImageBrush(img) { Stretch = p as string == "cover" ? Stretch.UniformToFill : Stretch.Uniform };
        RenderOptions.SetBitmapScalingMode(b, BitmapScalingMode.HighQuality);
        b.Freeze();
        return b;
    }

    public object ConvertBack(object? v, Type t, object? p, CultureInfo c) => throw new NotSupportedException();
}

/// <summary>Picks the tile drawing for a file or a folder in the grid.</summary>
public sealed class TileSelector : DataTemplateSelector
{
    public DataTemplate? FileTemplate { get; set; }
    public DataTemplate? FolderTemplate { get; set; }

    public override DataTemplate? SelectTemplate(object item, DependencyObject container) =>
        item is ViewModels.FolderTileVm ? FolderTemplate : FileTemplate;
}
