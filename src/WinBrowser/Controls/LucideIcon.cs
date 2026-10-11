using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Path = Microsoft.UI.Xaml.Shapes.Path;

namespace WinBrowser.Controls;

/// <summary>
/// A Lucide icon (from /assets) drawn as a vector stroke. It is coloured by the inherited
/// Foreground, so it follows button hover/pressed/disabled states like a FontIcon.
/// </summary>
public sealed partial class LucideIcon : UserControl
{
    private readonly Path _path = new()
    {
        StrokeStartLineCap = PenLineCap.Round,
        StrokeEndLineCap = PenLineCap.Round,
        StrokeLineJoin = PenLineJoin.Round,
    };

    public LucideIcon()
    {
        IsTabStop = false;
        IsHitTestVisible = false;
        _path.SetBinding(Shape.StrokeProperty, new Binding { Source = this, Path = new PropertyPath(nameof(Foreground)) });
        _path.StrokeThickness = StrokeWidth;

        // Lucide draws on a 24x24 grid; the Viewbox scales it (and the stroke) to Size.
        var canvas = new Canvas { Width = 24, Height = 24 };
        canvas.Children.Add(_path);
        Content = new Viewbox { Child = canvas, Stretch = Stretch.Uniform };
        Width = Height = Size;
    }

    public static readonly DependencyProperty SymbolProperty = DependencyProperty.Register(
        nameof(Symbol), typeof(string), typeof(LucideIcon),
        new PropertyMetadata(null, (d, e) => ((LucideIcon)d).UpdateGeometry()));

    /// <summary>File name of the icon without extension, e.g. "arrow-left".</summary>
    public string? Symbol
    {
        get => (string?)GetValue(SymbolProperty);
        set => SetValue(SymbolProperty, value);
    }

    public static readonly DependencyProperty SizeProperty = DependencyProperty.Register(
        nameof(Size), typeof(double), typeof(LucideIcon),
        new PropertyMetadata(16.0, (d, e) => ((LucideIcon)d).Width = ((LucideIcon)d).Height = (double)e.NewValue));

    public double Size
    {
        get => (double)GetValue(SizeProperty);
        set => SetValue(SizeProperty, value);
    }

    public static readonly DependencyProperty StrokeWidthProperty = DependencyProperty.Register(
        nameof(StrokeWidth), typeof(double), typeof(LucideIcon),
        new PropertyMetadata(1.75, (d, e) => ((LucideIcon)d)._path.StrokeThickness = (double)e.NewValue));

    /// <summary>Stroke width in the 24-unit grid (Lucide's default is 2).</summary>
    public double StrokeWidth
    {
        get => (double)GetValue(StrokeWidthProperty);
        set => SetValue(StrokeWidthProperty, value);
    }

    private void UpdateGeometry()
    {
        _path.Data = Symbol != null && LucideIcons.Paths.TryGetValue(Symbol, out var data)
            ? (Geometry)XamlBindingHelper.ConvertValue(typeof(Geometry), data)
            : null;
    }
}
