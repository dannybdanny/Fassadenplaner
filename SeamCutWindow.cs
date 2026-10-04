using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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
    private readonly Canvas _viewport = new();
    private readonly Canvas _drawingCanvas = new();
    private readonly ComboBox _unitCombo = new();

    private TextBlock? _coilValueText;
    private TextBlock? _visibleValueText;

    private readonly ScaleTransform _zoomTransform = new(1, 1);
    private readonly TranslateTransform _panTransform = new();
    private readonly TransformGroup _drawingTransform = new();

    private bool _isPanning;
    private Point _lastPanPoint;
    private double _zoom = 1.0;

    private bool UseCentimeters => _unitCombo.SelectedIndex <= 0;

    private static readonly CultureInfo GermanCulture =
        CultureInfo.GetCultureInfo("de-DE");

    public SeamCutWindow(SeamCutData data)
    {
        _data = data;

        Title = $"Zuschnitt · Schar {data.PanNumber}";
        Width = 640;
        Height = 720;
        MinWidth = 540;
        MinHeight = 560;
        MaxHeight = Math.Max(520, SystemParameters.WorkArea.Height - 40);
        MaxWidth = Math.Max(660, SystemParameters.WorkArea.Width - 40);
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        WindowState = WindowState.Normal;
        ResizeMode = ResizeMode.CanResize;
        Background = new SolidColorBrush(Color.FromRgb(238, 242, 245));

        _drawingTransform.Children.Add(_zoomTransform);
        _drawingTransform.Children.Add(_panTransform);
        _drawingCanvas.RenderTransform = _drawingTransform;

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var header = BuildHeader();
        Grid.SetRow(header, 0);
        root.Children.Add(header);

        var viewerBorder = new Border
        {
            Margin = new Thickness(14, 0, 14, 14),
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(215, 222, 229)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(10),
            ClipToBounds = true
        };

        _viewport.Background = Brushes.White;
        _viewport.ClipToBounds = true;
        _viewport.Children.Add(_drawingCanvas);

        _viewport.SizeChanged += (_, _) => DrawCut();
        _viewport.PreviewMouseWheel += Viewport_PreviewMouseWheel;
        _viewport.PreviewMouseRightButtonDown += Viewport_PreviewMouseRightButtonDown;
        _viewport.PreviewMouseRightButtonUp += Viewport_PreviewMouseRightButtonUp;
        _viewport.PreviewMouseMove += Viewport_PreviewMouseMove;

        viewerBorder.Child = _viewport;

        Grid.SetRow(viewerBorder, 1);
        root.Children.Add(viewerBorder);

        Content = root;

        Loaded += (_, _) =>
        {
            UpdateHeaderValues();
            ResetView();
            DrawCut();
        };
    }

    private UIElement BuildHeader()
    {
        var grid = new Grid
        {
            Margin = new Thickness(14, 10, 14, 10)
        };

        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var left = new StackPanel();

        left.Children.Add(new TextBlock
        {
            Text = $"{_data.SurfaceName} · Schar {_data.PanNumber}",
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(29, 43, 54))
        });

        left.Children.Add(new TextBlock
        {
            Text = _data.IsStartPan
                ? "Startschar · Unterfalz auf beiden Seiten"
                : $"{_data.StartFoldName} Startseite · {_data.EndFoldName} Endseite",
            Margin = new Thickness(0, 2, 0, 0),
            FontSize = 10,
            Foreground = new SolidColorBrush(Color.FromRgb(112, 128, 139))
        });

        left.Children.Add(new TextBlock
        {
            Text = "Mausrad: Zoom · rechte Maustaste: verschieben",
            Margin = new Thickness(0, 2, 0, 0),
            FontSize = 9.5,
            Foreground = new SolidColorBrush(Color.FromRgb(139, 150, 158))
        });

        grid.Children.Add(left);

        var right = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center
        };

        _coilValueText = new TextBlock();
        _visibleValueText = new TextBlock();

        right.Children.Add(BuildInfoChip("Coil", _coilValueText));
        right.Children.Add(BuildInfoChip("sichtbar", _visibleValueText));

        _unitCombo.Width = 72;
        _unitCombo.Height = 30;
        _unitCombo.Margin = new Thickness(8, 0, 0, 0);
        _unitCombo.Items.Add("cm");
        _unitCombo.Items.Add("mm");
        _unitCombo.SelectedIndex = 0;
        _unitCombo.SelectionChanged += (_, _) =>
        {
            UpdateHeaderValues();
            DrawCut();
        };
        right.Children.Add(_unitCombo);

        var resetButton = new Button
        {
            Content = "Ansicht",
            Height = 30,
            Margin = new Thickness(8, 0, 0, 0),
            Padding = new Thickness(9, 0, 9, 0),
            Background = new SolidColorBrush(Color.FromRgb(239, 243, 246)),
            Foreground = new SolidColorBrush(Color.FromRgb(52, 67, 77)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(211, 219, 226)),
            BorderThickness = new Thickness(1)
        };
        resetButton.Click += (_, _) => ResetView();
        right.Children.Add(resetButton);

        var closeButton = new Button
        {
            Content = "Schließen",
            Height = 30,
            Margin = new Thickness(8, 0, 0, 0),
            Padding = new Thickness(10, 0, 10, 0),
            Background = new SolidColorBrush(Color.FromRgb(36, 55, 70)),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0)
        };
        closeButton.Click += (_, _) => Close();
        right.Children.Add(closeButton);

        Grid.SetColumn(right, 1);
        grid.Children.Add(right);

        return grid;
    }

    private static Border BuildInfoChip(string caption, TextBlock valueText)
    {
        var stack = new StackPanel();

        stack.Children.Add(new TextBlock
        {
            Text = caption,
            FontSize = 8.5,
            Foreground = new SolidColorBrush(Color.FromRgb(127, 141, 151))
        });

        valueText.FontSize = 11.5;
        valueText.FontWeight = FontWeights.SemiBold;
        valueText.Foreground = new SolidColorBrush(Color.FromRgb(49, 86, 107));
        stack.Children.Add(valueText);

        return new Border
        {
            Margin = new Thickness(7, 0, 0, 0),
            Padding = new Thickness(8, 5, 8, 5),
            Background = new SolidColorBrush(Color.FromRgb(247, 249, 251)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(218, 225, 231)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Child = stack
        };
    }

    private void UpdateHeaderValues()
    {
        if (_coilValueText is not null)
            _coilValueText.Text = FormatLength(_data.RawWidthMm);

        if (_visibleValueText is not null)
            _visibleValueText.Text = FormatLength(_data.VisibleWidthMm);
    }

    private string FormatLength(double valueMm)
    {
        return UseCentimeters
            ? $"{(valueMm / 10.0).ToString("0.#", GermanCulture)} cm"
            : $"{valueMm.ToString("0.#", GermanCulture)} mm";
    }

    private void ResetView()
    {
        _zoom = 1.0;
        _zoomTransform.ScaleX = 1.0;
        _zoomTransform.ScaleY = 1.0;
        _panTransform.X = 0;
        _panTransform.Y = 0;
    }

    private void Viewport_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        var mouse = e.GetPosition(_viewport);
        var oldZoom = _zoom;
        var factor = e.Delta > 0 ? 1.12 : 0.89;

        _zoom = Math.Clamp(_zoom * factor, 0.55, 4.0);
        if (Math.Abs(_zoom - oldZoom) < 0.0001)
            return;

        var worldX = (mouse.X - _panTransform.X) / oldZoom;
        var worldY = (mouse.Y - _panTransform.Y) / oldZoom;

        _zoomTransform.ScaleX = _zoom;
        _zoomTransform.ScaleY = _zoom;

        _panTransform.X = mouse.X - worldX * _zoom;
        _panTransform.Y = mouse.Y - worldY * _zoom;

        e.Handled = true;
    }

    private void Viewport_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        _isPanning = true;
        _lastPanPoint = e.GetPosition(_viewport);
        _viewport.CaptureMouse();
        Mouse.OverrideCursor = Cursors.SizeAll;
        e.Handled = true;
    }

    private void Viewport_PreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        _isPanning = false;
        _viewport.ReleaseMouseCapture();
        Mouse.OverrideCursor = null;
        e.Handled = true;
    }

    private void Viewport_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isPanning || e.RightButton != MouseButtonState.Pressed)
            return;

        var current = e.GetPosition(_viewport);
        var delta = current - _lastPanPoint;

        _panTransform.X += delta.X;
        _panTransform.Y += delta.Y;

        _lastPanPoint = current;
        e.Handled = true;
    }

    private void DrawCut()
    {
        _drawingCanvas.Children.Clear();

        var width = _viewport.ActualWidth;
        var height = _viewport.ActualHeight;

        if (width < 100 || height < 100)
            return;

        _drawingCanvas.Width = width;
        _drawingCanvas.Height = height;

        const double marginLeft = 86;
        const double marginRight = 86;
        const double marginTop = 150;
        const double marginBottom = 72;

        var availableWidth = Math.Max(100, width - marginLeft - marginRight);
        var availableHeight = Math.Max(100, height - marginTop - marginBottom);

        var rawWidth = Math.Max(_data.RawWidthMm, 1);
        var maxHeight = Math.Max(_data.StartHeightMm, _data.EndHeightMm);

        var scale = Math.Min(
            availableWidth / rawWidth,
            availableHeight /
            Math.Max(maxHeight + Math.Abs(_data.BottomDeltaMm), 1));

        var x0 = marginLeft;
        var x1 = x0 + rawWidth * scale;

        var bottomBase = marginTop + availableHeight;
        var leftBottom = bottomBase;
        var rightBottom = bottomBase - _data.BottomDeltaMm * scale;

        var leftTop = leftBottom - _data.StartHeightMm * scale;
        var rightTop = rightBottom - _data.EndHeightMm * scale;

        var pBottomLeft = new Point(x0, leftBottom);
        var pBottomRight = new Point(x1, rightBottom);
        var pTopRight = new Point(x1, rightTop);
        var pTopLeft = new Point(x0, leftTop);

        var startFoldX = x0 + _data.StartFoldMm * scale;
        var endFoldX = x1 - _data.EndFoldMm * scale;

        // Falzzonen zuerst, damit ihre Bedeutung optisch klar ist.
        DrawFoldBand(
            new Point(x0, leftBottom),
            new Point(x0, leftTop),
            new Point(startFoldX, leftTop),
            new Point(startFoldX, leftBottom));

        DrawFoldBand(
            new Point(endFoldX, rightBottom),
            new Point(endFoldX, rightTop),
            new Point(x1, rightTop),
            new Point(x1, rightBottom));

        var polygon = new Polygon
        {
            Points = new PointCollection
            {
                pBottomLeft,
                pBottomRight,
                pTopRight,
                pTopLeft
            },
            Fill = new SolidColorBrush(Color.FromArgb(195, 231, 236, 240)),
            Stroke = new SolidColorBrush(Color.FromRgb(45, 61, 72)),
            StrokeThickness = 1.6
        };

        _drawingCanvas.Children.Add(polygon);

        DrawFoldLine(startFoldX, leftBottom, leftTop);
        DrawFoldLine(endFoldX, rightBottom, rightTop);

        DrawAllowanceLine(
            pTopLeft,
            pTopRight,
            new Point((x0 + x1) / 2, (leftBottom + rightBottom) / 2),
            _data.TopAllowanceMm * scale);

        DrawAllowanceLine(
            pBottomLeft,
            pBottomRight,
            new Point((x0 + x1) / 2, (leftTop + rightTop) / 2),
            _data.BottomAllowanceMm * scale);

        // Kompakte Profilansicht über der Schar.
        var profileY =
            Math.Max(38, Math.Min(leftTop, rightTop) - 88);

        DrawProfileView(
            x0,
            startFoldX,
            endFoldX,
            x1,
            profileY);

        // Maßkette direkt über der Oberkante:
        // nur Falz | Deckbreite | Falz, ohne ausgeschriebene Bezeichnungen.
        var topDimensionY =
            Math.Max(profileY + 34, Math.Min(leftTop, rightTop) - 24);

        DrawDimensionSegment(
            x0,
            startFoldX,
            topDimensionY,
            FormatLength(_data.StartFoldMm));

        DrawDimensionSegment(
            startFoldX,
            endFoldX,
            topDimensionY,
            FormatLength(_data.VisibleWidthMm));

        DrawDimensionSegment(
            endFoldX,
            x1,
            topDimensionY,
            FormatLength(_data.EndFoldMm));

        DrawHorizontalDimension(
            x0,
            x1,
            Math.Min(height - 28, Math.Max(leftBottom, rightBottom) + 40),
            FormatLength(_data.RawWidthMm));

        DrawVerticalDimension(
            x0 - 50,
            leftBottom,
            leftTop,
            FormatLength(_data.StartHeightMm));

        DrawVerticalDimension(
            x1 + 50,
            rightBottom,
            rightTop,
            FormatLength(_data.EndHeightMm));

        AddLegend(
            $"oben +{FormatLength(_data.TopAllowanceMm)}",
            (x0 + x1) / 2,
            Math.Max(profileY - 26, 4),
            HorizontalAlignment.Center);

        AddLegend(
            $"unten +{FormatLength(_data.BottomAllowanceMm)}",
            (x0 + x1) / 2,
            Math.Min(height - 50, Math.Max(leftBottom, rightBottom) + 10),
            HorizontalAlignment.Center);
    }

    private void DrawProfileView(
        double x0,
        double startFoldX,
        double endFoldX,
        double x1,
        double y)
    {
        var stroke = new SolidColorBrush(Color.FromRgb(45, 61, 72));
        var accent = new SolidColorBrush(Color.FromRgb(72, 119, 145));

        // Deckfläche des Profils.
        _drawingCanvas.Children.Add(new Line
        {
            X1 = startFoldX,
            X2 = endFoldX,
            Y1 = y,
            Y2 = y,
            Stroke = stroke,
            StrokeThickness = 1.8
        });

        DrawProfileFold(
            x0,
            startFoldX,
            y,
            _data.StartFoldName,
            isLeft: true,
            stroke,
            accent);

        DrawProfileFold(
            endFoldX,
            x1,
            y,
            _data.EndFoldName,
            isLeft: false,
            stroke,
            accent);

        var caption = new TextBlock
        {
            Text = "Profilansicht",
            FontSize = 9,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(124, 137, 147))
        };

        caption.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(caption, (x0 + x1) / 2 - caption.DesiredSize.Width / 2);
        Canvas.SetTop(caption, y - 28);
        _drawingCanvas.Children.Add(caption);
    }

    private void DrawProfileFold(
        double outerX,
        double innerX,
        double y,
        string foldName,
        bool isLeft,
        Brush stroke,
        Brush accent)
    {
        var direction = isLeft ? 1.0 : -1.0;
        var outer = isLeft ? outerX : outerX;
        var inner = isLeft ? innerX : innerX;

        // Die Profilansicht ist bewusst schematisch:
        // Unterfalz = einfache Aufkantung mit kurzer Rückkantung,
        // Oberfalz = höhere Aufkantung mit kleiner Überdeckung.
        var isUpper = foldName.Contains(
            "Ober",
            StringComparison.OrdinalIgnoreCase);

        var rise = isUpper ? 18.0 : 13.0;
        var returnLength = isUpper ? 12.0 : 8.0;

        var baseX = inner;

        _drawingCanvas.Children.Add(new Line
        {
            X1 = baseX,
            X2 = baseX,
            Y1 = y,
            Y2 = y - rise,
            Stroke = accent,
            StrokeThickness = 1.8
        });

        _drawingCanvas.Children.Add(new Line
        {
            X1 = baseX,
            X2 = baseX - direction * returnLength,
            Y1 = y - rise,
            Y2 = y - rise,
            Stroke = accent,
            StrokeThickness = 1.8
        });

        if (isUpper)
        {
            _drawingCanvas.Children.Add(new Line
            {
                X1 = baseX - direction * returnLength,
                X2 = baseX - direction * returnLength,
                Y1 = y - rise,
                Y2 = y - rise + 5,
                Stroke = accent,
                StrokeThickness = 1.8
            });
        }

        // kurze Basislinie bis zur Rohkante, damit Coilbreite und Falzzone erkennbar bleiben.
        _drawingCanvas.Children.Add(new Line
        {
            X1 = outer,
            X2 = baseX,
            Y1 = y,
            Y2 = y,
            Stroke = stroke,
            StrokeThickness = 1.2
        });
    }

    private void DrawFoldBand(Point p1, Point p2, Point p3, Point p4)
    {
        _drawingCanvas.Children.Add(new Polygon
        {
            Points = new PointCollection { p1, p2, p3, p4 },
            Fill = new SolidColorBrush(Color.FromArgb(80, 113, 164, 191)),
            StrokeThickness = 0
        });
    }

    private void DrawFoldLine(double x, double bottom, double top)
    {
        _drawingCanvas.Children.Add(new Line
        {
            X1 = x,
            X2 = x,
            Y1 = bottom,
            Y2 = top,
            Stroke = new SolidColorBrush(Color.FromRgb(72, 119, 145)),
            StrokeThickness = 1.15,
            StrokeDashArray = new DoubleCollection { 4, 3 }
        });
    }

    private void DrawAllowanceLine(
        Point a,
        Point b,
        Point interiorPoint,
        double distance)
    {
        if (distance <= 0.01)
            return;

        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        var length = Math.Sqrt(dx * dx + dy * dy);

        if (length < 0.001)
            return;

        var nx = -dy / length;
        var ny = dx / length;

        var mid = new Point(
            (a.X + b.X) / 2,
            (a.Y + b.Y) / 2);

        var toInterior = new Vector(
            interiorPoint.X - mid.X,
            interiorPoint.Y - mid.Y);

        if (nx * toInterior.X + ny * toInterior.Y < 0)
        {
            nx = -nx;
            ny = -ny;
        }

        var oa = new Point(
            a.X + nx * distance,
            a.Y + ny * distance);

        var ob = new Point(
            b.X + nx * distance,
            b.Y + ny * distance);

        _drawingCanvas.Children.Add(new Line
        {
            X1 = oa.X,
            X2 = ob.X,
            Y1 = oa.Y,
            Y2 = ob.Y,
            Stroke = new SolidColorBrush(Color.FromRgb(72, 119, 145)),
            StrokeThickness = 1.15,
            StrokeDashArray = new DoubleCollection { 5, 3 }
        });
    }

    private void DrawDimensionSegment(
        double x0,
        double x1,
        double y,
        string text)
    {
        var stroke = new SolidColorBrush(Color.FromRgb(92, 105, 115));

        _drawingCanvas.Children.Add(new Line
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

        var label = CreateLabel(text);
        label.Child = new TextBlock
        {
            Text = text,
            FontSize = 9.6,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(55, 69, 79)),
            TextAlignment = TextAlignment.Center
        };

        PlaceLabel(label, (x0 + x1) / 2, y - 13);
    }

    private void DrawHorizontalDimension(
        double x0,
        double x1,
        double y,
        string text)
    {
        var stroke = new SolidColorBrush(Color.FromRgb(92, 105, 115));

        var label = CreateLabel(text);
        label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

        var center = (x0 + x1) / 2;
        var gap = label.DesiredSize.Width / 2 + 7;

        _drawingCanvas.Children.Add(new Line
        {
            X1 = x0,
            X2 = Math.Max(x0, center - gap),
            Y1 = y,
            Y2 = y,
            Stroke = stroke,
            StrokeThickness = 1
        });

        _drawingCanvas.Children.Add(new Line
        {
            X1 = Math.Min(x1, center + gap),
            X2 = x1,
            Y1 = y,
            Y2 = y,
            Stroke = stroke,
            StrokeThickness = 1
        });

        DrawTick(x0, y, true, stroke);
        DrawTick(x1, y, true, stroke);

        PlaceLabel(label, center, y);
    }

    private void DrawVerticalDimension(
        double x,
        double y0,
        double y1,
        string text)
    {
        var stroke = new SolidColorBrush(Color.FromRgb(92, 105, 115));

        var label = CreateLabel(text);
        label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

        var top = Math.Min(y0, y1);
        var bottom = Math.Max(y0, y1);
        var center = (top + bottom) / 2;
        var gap = label.DesiredSize.Height / 2 + 7;

        _drawingCanvas.Children.Add(new Line
        {
            X1 = x,
            X2 = x,
            Y1 = top,
            Y2 = Math.Max(top, center - gap),
            Stroke = stroke,
            StrokeThickness = 1
        });

        _drawingCanvas.Children.Add(new Line
        {
            X1 = x,
            X2 = x,
            Y1 = Math.Min(bottom, center + gap),
            Y2 = bottom,
            Stroke = stroke,
            StrokeThickness = 1
        });

        DrawTick(x, y0, false, stroke);
        DrawTick(x, y1, false, stroke);

        PlaceLabel(label, x, center);
    }

    private void DrawTick(
        double x,
        double y,
        bool vertical,
        Brush stroke)
    {
        _drawingCanvas.Children.Add(new Line
        {
            X1 = vertical ? x : x - 5,
            X2 = vertical ? x : x + 5,
            Y1 = vertical ? y - 5 : y,
            Y2 = vertical ? y + 5 : y,
            Stroke = stroke,
            StrokeThickness = 1
        });
    }

    private Border CreateLabel(string text)
    {
        return new Border
        {
            Background = Brushes.White,
            Padding = new Thickness(4, 2, 4, 2),
            Child = new TextBlock
            {
                Text = text,
                FontSize = 10.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(55, 69, 79))
            }
        };
    }

    private void PlaceLabel(
        Border label,
        double x,
        double y)
    {
        label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

        Canvas.SetLeft(
            label,
            x - label.DesiredSize.Width / 2);

        Canvas.SetTop(
            label,
            y - label.DesiredSize.Height / 2);

        _drawingCanvas.Children.Add(label);
    }

    private void AddLegend(
        string text,
        double x,
        double y,
        HorizontalAlignment alignment)
    {
        var label = CreateLabel(text);
        label.Background =
            new SolidColorBrush(Color.FromArgb(245, 255, 255, 255));

        label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

        var left = alignment switch
        {
            HorizontalAlignment.Left => x,
            HorizontalAlignment.Right => x - label.DesiredSize.Width,
            _ => x - label.DesiredSize.Width / 2
        };

        Canvas.SetLeft(label, Math.Max(0, left));
        Canvas.SetTop(label, Math.Max(0, y));
        _drawingCanvas.Children.Add(label);
    }
}
