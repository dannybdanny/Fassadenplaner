using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Fassadenplaner;

public sealed record SeamCutData(
    string SurfaceName,
    int PanNumber,
    bool IsStartPan,
    double RawWidthMm,
    double VisibleWidthMm,
    double StartHeightMm,
    double EndHeightMm,
    double BottomDeltaMm,
    double TopDeltaMm,
    double StartFoldMm,
    double EndFoldMm,
    double TopAllowanceMm,
    double BottomAllowanceMm,
    string StartFoldName,
    string EndFoldName,
    string DeckDirection);

public sealed class SeamCutWindow : Window
{
    private readonly SeamCutData _data;
    private readonly Canvas _canvas = new();

    private static readonly CultureInfo GermanCulture =
        CultureInfo.GetCultureInfo("de-DE");

    public SeamCutWindow(SeamCutData data)
    {
        _data = data;

        Title = $"Zuschnitt · Schar {data.PanNumber}";
        Width = 980;
        Height = 720;
        MinWidth = 760;
        MinHeight = 560;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(238, 242, 245));

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var header = BuildHeader();
        Grid.SetRow(header, 0);
        root.Children.Add(header);

        var viewerBorder = new Border
        {
            Margin = new Thickness(18, 0, 18, 18),
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(215, 222, 229)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(14)
        };

        _canvas.Background = Brushes.Transparent;
        _canvas.SizeChanged += (_, _) => DrawCut();
        viewerBorder.Child = _canvas;

        Grid.SetRow(viewerBorder, 1);
        root.Children.Add(viewerBorder);

        Content = root;
    }

    private UIElement BuildHeader()
    {
        var grid = new Grid
        {
            Margin = new Thickness(18, 16, 18, 12)
        };

        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var left = new StackPanel();

        left.Children.Add(new TextBlock
        {
            Text = $"{_data.SurfaceName} · Schar {_data.PanNumber}",
            FontSize = 22,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(29, 43, 54))
        });

        left.Children.Add(new TextBlock
        {
            Text = _data.IsStartPan
                ? "Startschar · Unterfalz auf beiden Seiten"
                : $"{_data.StartFoldName} an der Startseite · {_data.EndFoldName} an der Endseite",
            Margin = new Thickness(0, 3, 0, 0),
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(112, 128, 139))
        });

        left.Children.Add(new TextBlock
        {
            Text = $"Deckrichtung: {_data.DeckDirection}",
            Margin = new Thickness(0, 2, 0, 0),
            FontSize = 10.5,
            Foreground = new SolidColorBrush(Color.FromRgb(126, 140, 150))
        });

        grid.Children.Add(left);

        var right = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center
        };

        right.Children.Add(BuildInfoChip("Coil", _data.RawWidthMm));
        right.Children.Add(BuildInfoChip("sichtbar", _data.VisibleWidthMm));

        Grid.SetColumn(right, 1);
        grid.Children.Add(right);

        return grid;
    }

    private static Border BuildInfoChip(string caption, double value)
    {
        var stack = new StackPanel();

        stack.Children.Add(new TextBlock
        {
            Text = caption,
            FontSize = 9,
            Foreground = new SolidColorBrush(Color.FromRgb(127, 141, 151))
        });

        stack.Children.Add(new TextBlock
        {
            Text = $"{value:0.#} mm",
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(49, 86, 107))
        });

        return new Border
        {
            Margin = new Thickness(8, 0, 0, 0),
            Padding = new Thickness(10, 6, 10, 6),
            Background = new SolidColorBrush(Color.FromRgb(247, 249, 251)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(218, 225, 231)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(7),
            Child = stack
        };
    }

    private void DrawCut()
    {
        _canvas.Children.Clear();

        var width = _canvas.ActualWidth;
        var height = _canvas.ActualHeight;

        if (width < 100 || height < 100)
            return;

        const double marginLeft = 100;
        const double marginRight = 120;
        const double marginTop = 75;
        const double marginBottom = 90;

        var availableWidth = Math.Max(100, width - marginLeft - marginRight);
        var availableHeight = Math.Max(100, height - marginTop - marginBottom);

        var rawWidth = Math.Max(_data.RawWidthMm, 1);
        var maxHeight = Math.Max(_data.StartHeightMm, _data.EndHeightMm);
        var scale = Math.Min(
            availableWidth / rawWidth,
            availableHeight / Math.Max(maxHeight + Math.Abs(_data.BottomDeltaMm), 1));

        var x0 = marginLeft;
        var x1 = x0 + rawWidth * scale;

        var bottomBase = marginTop + availableHeight;
        var leftBottom = bottomBase;
        var rightBottom = bottomBase - _data.BottomDeltaMm * scale;

        var leftTop = leftBottom - _data.StartHeightMm * scale;
        var rightTop = rightBottom - _data.EndHeightMm * scale;

        var polygon = new Polygon
        {
            Points = new PointCollection
            {
                new Point(x0, leftBottom),
                new Point(x1, rightBottom),
                new Point(x1, rightTop),
                new Point(x0, leftTop)
            },
            Fill = new SolidColorBrush(Color.FromRgb(231, 236, 240)),
            Stroke = new SolidColorBrush(Color.FromRgb(45, 61, 72)),
            StrokeThickness = 1.6
        };

        _canvas.Children.Add(polygon);

        DrawFoldLine(x0 + _data.StartFoldMm * scale, leftBottom, leftTop, "Falz");
        DrawFoldLine(x1 - _data.EndFoldMm * scale, rightBottom, rightTop, "Falz");

        DrawHorizontalDimension(
            x0,
            x1,
            Math.Min(height - 35, Math.Max(leftBottom, rightBottom) + 38),
            $"{_data.RawWidthMm:0.#} mm Rohbreite");

        DrawVerticalDimension(
            x0 - 42,
            leftBottom,
            leftTop,
            $"{_data.StartHeightMm:0.#} mm");

        DrawVerticalDimension(
            x1 + 42,
            rightBottom,
            rightTop,
            $"{_data.EndHeightMm:0.#} mm");

        AddLabel(
            $"{_data.StartFoldName} {_data.StartFoldMm:0.#} mm",
            x0 + _data.StartFoldMm * scale * 0.5,
            Math.Min(leftBottom, leftTop) - 26,
            HorizontalAlignment.Center);

        AddLabel(
            $"{_data.EndFoldName} {_data.EndFoldMm:0.#} mm",
            x1 - _data.EndFoldMm * scale * 0.5,
            Math.Min(rightBottom, rightTop) - 26,
            HorizontalAlignment.Center);

        AddLabel(
            $"oben +{_data.TopAllowanceMm:0.#} mm",
            (x0 + x1) / 2,
            Math.Min(leftTop, rightTop) - 32,
            HorizontalAlignment.Center);

        AddLabel(
            $"unten +{_data.BottomAllowanceMm:0.#} mm",
            (x0 + x1) / 2,
            Math.Max(leftBottom, rightBottom) + 8,
            HorizontalAlignment.Center);

        var note = new TextBlock
        {
            Text = "2D-Zuschnitt aus der geneigten Gaubenwange. Seitenfalze und Umschläge sind geometrisch berücksichtigt.",
            FontSize = 10,
            Foreground = new SolidColorBrush(Color.FromRgb(121, 134, 144)),
            TextWrapping = TextWrapping.Wrap,
            Width = Math.Max(280, width - 80)
        };

        Canvas.SetLeft(note, 40);
        Canvas.SetTop(note, 18);
        _canvas.Children.Add(note);
    }

    private void DrawFoldLine(double x, double bottom, double top, string label)
    {
        _canvas.Children.Add(new Line
        {
            X1 = x,
            X2 = x,
            Y1 = bottom,
            Y2 = top,
            Stroke = new SolidColorBrush(Color.FromRgb(72, 119, 145)),
            StrokeThickness = 1.1,
            StrokeDashArray = new DoubleCollection { 4, 3 }
        });
    }

    private void DrawHorizontalDimension(double x0, double x1, double y, string text)
    {
        var stroke = new SolidColorBrush(Color.FromRgb(92, 105, 115));

        _canvas.Children.Add(new Line
        {
            X1 = x0,
            X2 = x1,
            Y1 = y,
            Y2 = y,
            Stroke = stroke,
            StrokeThickness = 1
        });

        DrawTick(x0, y, true, stroke);
        DrawTick(x1, y, true, stroke);

        AddLabel(text, (x0 + x1) / 2, y - 20, HorizontalAlignment.Center);
    }

    private void DrawVerticalDimension(double x, double y0, double y1, string text)
    {
        var stroke = new SolidColorBrush(Color.FromRgb(92, 105, 115));

        _canvas.Children.Add(new Line
        {
            X1 = x,
            X2 = x,
            Y1 = y0,
            Y2 = y1,
            Stroke = stroke,
            StrokeThickness = 1
        });

        DrawTick(x, y0, false, stroke);
        DrawTick(x, y1, false, stroke);

        AddLabel(text, x, (y0 + y1) / 2, HorizontalAlignment.Center);
    }

    private void DrawTick(double x, double y, bool vertical, Brush stroke)
    {
        _canvas.Children.Add(new Line
        {
            X1 = vertical ? x : x - 5,
            X2 = vertical ? x : x + 5,
            Y1 = vertical ? y - 5 : y,
            Y2 = vertical ? y + 5 : y,
            Stroke = stroke,
            StrokeThickness = 1
        });
    }

    private void AddLabel(string text, double x, double y, HorizontalAlignment alignment)
    {
        var border = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(240, 255, 255, 255)),
            Padding = new Thickness(4, 2, 4, 2),
            Child = new TextBlock
            {
                Text = text,
                FontSize = 10.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(55, 69, 79))
            }
        };

        border.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

        Canvas.SetLeft(border, x - border.DesiredSize.Width / 2);
        Canvas.SetTop(border, y - border.DesiredSize.Height / 2);
        _canvas.Children.Add(border);
    }
}
