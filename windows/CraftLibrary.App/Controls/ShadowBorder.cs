using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace CraftLibrary.App.Controls;

/// <summary>
/// A Border with a soft drop shadow drawn behind it.
/// WPF's own DropShadowEffect turns everything inside it into a picture first, which makes text blurry when the
/// app's text size is Small or Large (the whole window is scaled then). This draws the shadow as a few faint
/// rounded layers instead, so the text inside stays sharp at every size.
/// </summary>
public sealed class ShadowBorder : Border
{
    public static readonly DependencyProperty ShadowColorProperty = DependencyProperty.Register(nameof(ShadowColor), typeof(Color), typeof(ShadowBorder),
        new FrameworkPropertyMetadata(Color.FromRgb(0x0F, 0x17, 0x2A), FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>How far the shadow spreads (similar to a blur radius).</summary>
    public static readonly DependencyProperty ShadowSizeProperty = DependencyProperty.Register(nameof(ShadowSize), typeof(double), typeof(ShadowBorder),
        new FrameworkPropertyMetadata(18.0, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>How far the shadow drops below the box.</summary>
    public static readonly DependencyProperty ShadowDropProperty = DependencyProperty.Register(nameof(ShadowDrop), typeof(double), typeof(ShadowBorder),
        new FrameworkPropertyMetadata(8.0, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Overall strength, 0 to 1.</summary>
    public static readonly DependencyProperty ShadowOpacityProperty = DependencyProperty.Register(nameof(ShadowOpacity), typeof(double), typeof(ShadowBorder),
        new FrameworkPropertyMetadata(0.22, FrameworkPropertyMetadataOptions.AffectsRender));

    public Color ShadowColor { get => (Color)GetValue(ShadowColorProperty); set => SetValue(ShadowColorProperty, value); }
    public double ShadowSize { get => (double)GetValue(ShadowSizeProperty); set => SetValue(ShadowSizeProperty, value); }
    public double ShadowDrop { get => (double)GetValue(ShadowDropProperty); set => SetValue(ShadowDropProperty, value); }
    public double ShadowOpacity { get => (double)GetValue(ShadowOpacityProperty); set => SetValue(ShadowOpacityProperty, value); }

    private const int Layers = 10;

    protected override void OnRender(DrawingContext dc)
    {
        var w = ActualWidth;
        var h = ActualHeight;
        if (w > 0 && h > 0 && ShadowOpacity > 0)
        {
            var radius = CornerRadius.TopLeft;
            // Each layer is a little bigger and fainter than the one before; together they fade out softly.
            for (var i = Layers; i >= 1; i--)
            {
                var t = i / (double)Layers;                 // 1 = outermost
                var grow = ShadowSize * t;
                var alpha = ShadowOpacity * (1 - t) * (1 - t) * 0.55;
                if (alpha <= 0.002) continue;
                var c = Color.FromArgb((byte)Math.Round(alpha * 255), ShadowColor.R, ShadowColor.G, ShadowColor.B);
                var b = new SolidColorBrush(c);
                b.Freeze();
                var rect = new Rect(-grow, -grow + ShadowDrop, w + 2 * grow, h + 2 * grow);
                dc.DrawRoundedRectangle(b, null, rect, radius + grow, radius + grow);
            }
        }
        base.OnRender(dc);
    }
}
