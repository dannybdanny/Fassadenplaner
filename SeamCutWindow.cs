using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Fassadenplaner;

public sealed record SeamCutData(
    string SurfaceKey,
    string PanCode,
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
    string DeckDirection,
    double? TopApexOffsetMm = null,
    double? TopApexHeightMm = null,
    bool ShowTopAngle = false);

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

    // In der 2D-Zuschnittansicht wird die linke Gaubenwange gespiegelt:
    // hohe Startseite rechts, Deckrichtung rechts → links.
    // Rechte Wange: hohe Startseite links, Deckrichtung links → rechts.
    private bool IsLeftCheek =>
        _data.SurfaceKey == "left-cheek";

    private bool IsFront =>
        _data.SurfaceKey == "front";

    private string PanCode => _data.PanCode;

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
            Text = $"{_data.SurfaceName} · {PanCode}",
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(29, 43, 54))
        });

        left.Children.Add(new TextBlock
        {
            Text = IsFront
                ? $"{_data.StartFoldName} links · {_data.EndFoldName} rechts"
                : _data.IsStartPan
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

        const double marginLeft = 42;
        const double marginRight = 42;
        const double marginTop = 118;
        const double marginBottom = 34;

        var availableWidth = Math.Max(100, width - marginLeft - marginRight);
        var availableHeight = Math.Max(100, height - marginTop - marginBottom);

        var rawWidth = Math.Max(_data.RawWidthMm, 1);
        var maxHeight = Math.Max(
            Math.Max(_data.StartHeightMm, _data.EndHeightMm),
            _data.TopApexHeightMm ?? 0);

        var contentHeightMm = Math.Max(
            maxHeight + Math.Abs(_data.BottomDeltaMm),
            1);

        var scale = Math.Min(
            availableWidth / rawWidth,
            availableHeight / contentHeightMm);

        var scaledWidth = rawWidth * scale;
        var scaledHeight = contentHeightMm * scale;

        // Standardansicht möglichst groß und mittig ausnutzen.
        var x0 = (width - scaledWidth) / 2.0;
        var x1 = x0 + scaledWidth;

        var bottomBase = marginTop + scaledHeight;

        // "Start" ist immer die hohe Seite der Gaubenwange.
        // Rechts: Start links. Links: Start rechts (gespiegelt).
        var startBottom = bottomBase;
        var endBottom = bottomBase - _data.BottomDeltaMm * scale;

        var startTop = startBottom - _data.StartHeightMm * scale;
        var endTop = endBottom - _data.EndHeightMm * scale;

        var leftBottom = IsLeftCheek ? endBottom : startBottom;
        var rightBottom = IsLeftCheek ? startBottom : endBottom;

        var leftTop = IsLeftCheek ? endTop : startTop;
        var rightTop = IsLeftCheek ? startTop : endTop;

        var pBottomLeft = new Point(x0, leftBottom);
        var pBottomRight = new Point(x1, rightBottom);
        var pTopRight = new Point(x1, rightTop);
        var pTopLeft = new Point(x0, leftTop);

        Point? pTopApex = null;

        if (_data.TopApexOffsetMm is double apexOffset &&
            _data.TopApexHeightMm is double apexHeight)
        {
            pTopApex = new Point(
                x0 + apexOffset * scale,
                bottomBase - apexHeight * scale);
        }

        var startFoldX = IsLeftCheek
            ? x1 - _data.StartFoldMm * scale
            : x0 + _data.StartFoldMm * scale;

        var endFoldX = IsLeftCheek
            ? x0 + _data.EndFoldMm * scale
            : x1 - _data.EndFoldMm * scale;

        var leftFoldX = IsLeftCheek ? endFoldX : startFoldX;
        var rightFoldX = IsLeftCheek ? startFoldX : endFoldX;

        var leftFoldName = IsLeftCheek
            ? _data.EndFoldName
            : _data.StartFoldName;

        var rightFoldName = IsLeftCheek
            ? _data.StartFoldName
            : _data.EndFoldName;

        var leftFoldMm = IsLeftCheek
            ? _data.EndFoldMm
            : _data.StartFoldMm;

        var rightFoldMm = IsLeftCheek
            ? _data.StartFoldMm
            : _data.EndFoldMm;

        var leftHeightMm = IsLeftCheek
            ? _data.EndHeightMm
            : _data.StartHeightMm;

        var rightHeightMm = IsLeftCheek
            ? _data.StartHeightMm
            : _data.EndHeightMm;

        // Falzzonen zuerst, damit ihre Bedeutung optisch klar ist.
        DrawFoldBand(
            new Point(x0, leftBottom),
            new Point(x0, leftTop),
            new Point(leftFoldX, leftTop),
            new Point(leftFoldX, leftBottom));

        DrawFoldBand(
            new Point(rightFoldX, rightBottom),
            new Point(rightFoldX, rightTop),
            new Point(x1, rightTop),
            new Point(x1, rightBottom));

        var polygonPoints = new PointCollection
        {
            pBottomLeft,
            pBottomRight,
            pTopRight
        };

        if (pTopApex is Point apexPoint)
            polygonPoints.Add(apexPoint);

        polygonPoints.Add(pTopLeft);

        var polygon = new Polygon
        {
            Points = polygonPoints,
            Fill = new SolidColorBrush(Color.FromArgb(195, 231, 236, 240)),
            Stroke = new SolidColorBrush(Color.FromRgb(45, 61, 72)),
            StrokeThickness = 1.6
        };

        _drawingCanvas.Children.Add(polygon);

        DrawPanCodeLabel(polygonPoints);

        DrawFoldLine(leftFoldX, leftBottom, leftTop);
        DrawFoldLine(rightFoldX, rightBottom, rightTop);

        var interiorPoint =
            new Point(
                (x0 + x1) / 2,
                (leftBottom + rightBottom) / 2);

        if (pTopApex is Point topApex)
        {
            DrawAllowanceLine(
                pTopLeft,
                topApex,
                interiorPoint,
                _data.TopAllowanceMm * scale);

            DrawAllowanceLine(
                topApex,
                pTopRight,
                interiorPoint,
                _data.TopAllowanceMm * scale);
        }
        else
        {
            DrawAllowanceLine(
                pTopLeft,
                pTopRight,
                interiorPoint,
                _data.TopAllowanceMm * scale);
        }

        DrawAllowanceLine(
            pBottomLeft,
            pBottomRight,
            new Point((x0 + x1) / 2, (leftTop + rightTop) / 2),
            _data.BottomAllowanceMm * scale);

        // Profilansicht ganz oben im Zeichenbereich.
        var profileY = 58.0;

        DrawProfileView(
            x0,
            leftFoldX,
            rightFoldX,
            x1,
            profileY,
            leftFoldName,
            rightFoldName);

        // "oben +..." direkt unter der Profilansicht.
        // Maßkette kompakt und direkt über der Schar.
        var topDimensionY =
            Math.Max(profileY + 92, Math.Min(leftTop, rightTop) - 22);


        DrawDimensionSegment(
            x0,
            leftFoldX,
            topDimensionY,
            FormatLength(leftFoldMm));

        DrawDimensionSegment(
            leftFoldX,
            rightFoldX,
            topDimensionY,
            FormatLength(_data.VisibleWidthMm));

        DrawDimensionSegment(
            rightFoldX,
            x1,
            topDimensionY,
            FormatLength(rightFoldMm));

        DrawHorizontalDimension(
            x0,
            x1,
            Math.Min(height - 28, Math.Max(leftBottom, rightBottom) + 40),
            FormatLength(_data.RawWidthMm));

        DrawVerticalDimension(
            x0 - 50,
            leftBottom,
            leftTop,
            FormatLength(leftHeightMm));

        DrawVerticalDimension(
            x1 + 50,
            rightBottom,
            rightTop,
            FormatLength(rightHeightMm));

        if (_data.ShowTopAngle)
        {
            if (pTopApex is Point angleApex)
            {
                var leftLength = (angleApex - pTopLeft).Length;
                var rightLength = (pTopRight - angleApex).Length;

                if (leftLength >= rightLength)
                    DrawTopAngle(pTopLeft, angleApex);
                else
                    DrawTopAngle(angleApex, pTopRight);
            }
            else
            {
                DrawTopAngle(pTopLeft, pTopRight);
            }
        }

        DrawBottomAngle(
            pBottomLeft,
            pBottomRight);
    }

    private void DrawPanCodeLabel(PointCollection polygonPoints)
    {
        if (polygonPoints.Count == 0)
            return;

        var center = new Point(
            polygonPoints.Average(p => p.X),
            polygonPoints.Average(p => p.Y));

        var label = new Border
        {
            Background =
                new SolidColorBrush(Color.FromArgb(215, 255, 255, 255)),
            BorderBrush =
                new SolidColorBrush(Color.FromRgb(181, 193, 201)),
            BorderThickness = new Thickness(0.8),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(5, 2, 5, 2),
            Child = new TextBlock
            {
                Text = PanCode,
                FontSize = 10.5,
                FontWeight = FontWeights.SemiBold,
                Foreground =
                    new SolidColorBrush(Color.FromRgb(45, 66, 80))
            }
        };

        label.Measure(
            new Size(
                double.PositiveInfinity,
                double.PositiveInfinity));

        Canvas.SetLeft(
            label,
            center.X - label.DesiredSize.Width / 2.0);

        Canvas.SetTop(
            label,
            center.Y - label.DesiredSize.Height / 2.0);

        _drawingCanvas.Children.Add(label);
    }

    private void DrawTopAngle(
        Point left,
        Point right)
    {
        var dx = right.X - left.X;
        var dy = right.Y - left.Y;

        if (Math.Abs(dx) < 0.001)
            return;

        var angleDeg =
            Math.Atan2(Math.Abs(dy), Math.Abs(dx)) *
            180.0 / Math.PI;

        if (angleDeg < 0.05)
            return;

        var interiorDeg =
            Math.Max(0, 90.0 - angleDeg);

        var mid = new Point(
            (left.X + right.X) / 2.0,
            (left.Y + right.Y) / 2.0);

        AddAngleLabel(
            $"oben {angleDeg.ToString("0.#", GermanCulture)}° · Innen {interiorDeg.ToString("0.#", GermanCulture)}°",
            new Point(mid.X, mid.Y + 16));
    }

    private void DrawBottomAngle(
        Point left,
        Point right)
    {
        var dx = right.X - left.X;
        var dy = right.Y - left.Y;

        if (Math.Abs(dx) < 0.001)
            return;

        var roofAngleDeg =
            Math.Atan2(Math.Abs(dy), Math.Abs(dx)) *
            180.0 / Math.PI;

        var interiorAngleDeg =
            Math.Max(0, 90.0 - roofAngleDeg);

        // Tiefster Punkt der schrägen Unterkante:
        // dort treffen Längsseite und Schrägschnitt zusammen.
        var low = left.Y >= right.Y ? left : right;
        var other = left.Y >= right.Y ? right : left;

        var horizontalDirection =
            other.X >= low.X ? 1.0 : -1.0;

        var stroke =
            new SolidColorBrush(Color.FromRgb(49, 86, 107));

        // Waagerechte Referenz für die Dachneigung.
        var referenceLength = 72.0;
        var referenceEnd = new Point(
            low.X + horizontalDirection * referenceLength,
            low.Y);

        _drawingCanvas.Children.Add(new Line
        {
            X1 = low.X,
            X2 = referenceEnd.X,
            Y1 = low.Y,
            Y2 = referenceEnd.Y,
            Stroke = stroke,
            StrokeThickness = 1.2
        });

        // Dachneigungswinkel zwischen Horizontaler und Schnittkante.
        DrawAngleArc(
            low,
            referenceLength * 0.42,
            0,
            roofAngleDeg,
            horizontalDirection > 0,
            stroke);

        AddAngleLabel(
            $"DN {roofAngleDeg.ToString("0.#", GermanCulture)}°",
            new Point(
                low.X + horizontalDirection * 48,
                low.Y - 18));

        // Innenwinkel zwischen Längsseite (senkrecht) und Schrägschnitt.
        DrawInteriorAngleArc(
            low,
            30,
            roofAngleDeg,
            horizontalDirection > 0,
            stroke);

        AddAngleLabel(
            $"Innen {interiorAngleDeg.ToString("0.#", GermanCulture)}°",
            new Point(
                low.X + horizontalDirection * 30,
                low.Y - 48));
    }

    private void DrawAngleArc(
        Point center,
        double radius,
        double startDeg,
        double endDeg,
        bool toRight,
        Brush stroke)
    {
        var startAngle = toRight ? 0.0 : 180.0;
        var endAngle = toRight
            ? -endDeg
            : 180.0 + endDeg;

        var start = PointOnCircle(center, radius, startAngle);
        var end = PointOnCircle(center, radius, endAngle);

        var figure = new PathFigure
        {
            StartPoint = start,
            IsClosed = false
        };

        figure.Segments.Add(new ArcSegment
        {
            Point = end,
            Size = new Size(radius, radius),
            SweepDirection = toRight
                ? SweepDirection.Counterclockwise
                : SweepDirection.Clockwise,
            IsLargeArc = false
        });

        _drawingCanvas.Children.Add(new Path
        {
            Data = new PathGeometry(new[] { figure }),
            Stroke = stroke,
            StrokeThickness = 1.15
        });
    }

    private void DrawInteriorAngleArc(
        Point center,
        double radius,
        double roofAngleDeg,
        bool toRight,
        Brush stroke)
    {
        // vom senkrechten Längsanschlag zur Schrägkante
        var verticalAngle = -90.0;
        var slopeAngle = toRight
            ? -roofAngleDeg
            : 180.0 + roofAngleDeg;

        var start = PointOnCircle(center, radius, verticalAngle);
        var end = PointOnCircle(center, radius, slopeAngle);

        var figure = new PathFigure
        {
            StartPoint = start,
            IsClosed = false
        };

        figure.Segments.Add(new ArcSegment
        {
            Point = end,
            Size = new Size(radius, radius),
            SweepDirection = toRight
                ? SweepDirection.Clockwise
                : SweepDirection.Counterclockwise,
            IsLargeArc = false
        });

        _drawingCanvas.Children.Add(new Path
        {
            Data = new PathGeometry(new[] { figure }),
            Stroke = stroke,
            StrokeThickness = 1.15
        });
    }

    private static Point PointOnCircle(
        Point center,
        double radius,
        double angleDeg)
    {
        var angle = angleDeg * Math.PI / 180.0;

        return new Point(
            center.X + Math.Cos(angle) * radius,
            center.Y + Math.Sin(angle) * radius);
    }

    private void AddAngleLabel(
        string text,
        Point center)
    {
        var label = new Border
        {
            Background = Brushes.Transparent,
            Padding = new Thickness(1, 0, 1, 0),
            Child = new TextBlock
            {
                Text = text,
                FontSize = 8.8,
                FontWeight = FontWeights.SemiBold,
                Foreground =
                    new SolidColorBrush(Color.FromRgb(49, 86, 107))
            }
        };

        label.Measure(
            new Size(
                double.PositiveInfinity,
                double.PositiveInfinity));

        Canvas.SetLeft(
            label,
            center.X - label.DesiredSize.Width / 2);

        Canvas.SetTop(
            label,
            center.Y - label.DesiredSize.Height / 2);

        _drawingCanvas.Children.Add(label);
    }

    private void DrawProfileView(
        double x0,
        double leftFoldX,
        double rightFoldX,
        double x1,
        double y,
        string leftFoldName,
        string rightFoldName)
    {
        // Diese Ansicht ist absichtlich NICHT maßstäblich.
        // Sie soll nur auf einen Blick zeigen, wo Oberfalz und Unterfalz liegen.
        var center = (x0 + x1) / 2.0;
        var profileWidth = 150.0;

        var leftBase = center - profileWidth / 2.0;
        var rightBase = center + profileWidth / 2.0;

        var deckLeft = leftBase + 28;
        var deckRight = rightBase - 28;

        var stroke = new SolidColorBrush(Color.FromRgb(36, 55, 70));
        var accent = new SolidColorBrush(Color.FromRgb(54, 111, 142));

        // Deckfläche.
        _drawingCanvas.Children.Add(new Line
        {
            X1 = deckLeft,
            X2 = deckRight,
            Y1 = y,
            Y2 = y,
            Stroke = stroke,
            StrokeThickness = 2.2
        });

        DrawSchematicFold(
            deckLeft,
            y,
            leftFoldName,
            isLeftSide: true,
            accent);

        DrawSchematicFold(
            deckRight,
            y,
            rightFoldName,
            isLeftSide: false,
            accent);

        var caption = new TextBlock
        {
            Text = "Profilansicht",
            FontSize = 8.2,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(126, 139, 149))
        };

        caption.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(caption, center - caption.DesiredSize.Width / 2);
        Canvas.SetTop(caption, y - 34);
        _drawingCanvas.Children.Add(caption);
    }

    private void DrawSchematicFold(
        double baseX,
        double baseY,
        string foldName,
        bool isLeftSide,
        Brush stroke)
    {
        var isUpper = foldName.Contains(
            "Ober",
            StringComparison.OrdinalIgnoreCase);

        var outward = isLeftSide ? -1.0 : 1.0;

        if (isUpper)
        {
            // Oberfalz schematisch:
            // 2,5 cm hoch -> 1 cm nach außen -> 1 cm nach unten.
            var rise = 25.0;
            var head = 10.0;
            var drop = 10.0;

            _drawingCanvas.Children.Add(new Line
            {
                X1 = baseX,
                X2 = baseX,
                Y1 = baseY,
                Y2 = baseY - rise,
                Stroke = stroke,
                StrokeThickness = 2.4
            });

            _drawingCanvas.Children.Add(new Line
            {
                X1 = baseX,
                X2 = baseX + outward * head,
                Y1 = baseY - rise,
                Y2 = baseY - rise,
                Stroke = stroke,
                StrokeThickness = 2.4
            });

            _drawingCanvas.Children.Add(new Line
            {
                X1 = baseX + outward * head,
                X2 = baseX + outward * head,
                Y1 = baseY - rise,
                Y2 = baseY - rise + drop,
                Stroke = stroke,
                StrokeThickness = 2.4
            });
        }
        else
        {
            // Unterfalz: Aufkantung mit ca. 1-cm-Rückkantung NACH INNEN
            // zur Deckfläche hin.
            var rise = 22.0;
            var returnLength = 11.0;
            var inward = -outward;

            _drawingCanvas.Children.Add(new Line
            {
                X1 = baseX,
                X2 = baseX,
                Y1 = baseY,
                Y2 = baseY - rise,
                Stroke = stroke,
                StrokeThickness = 2.4
            });

            _drawingCanvas.Children.Add(new Line
            {
                X1 = baseX,
                X2 = baseX + inward * returnLength,
                Y1 = baseY - rise,
                Y2 = baseY - rise,
                Stroke = stroke,
                StrokeThickness = 2.4
            });
        }

        var shortLabel = isUpper ? "O" : "U";

        var label = new Border
        {
            Background = Brushes.White,
            Padding = new Thickness(2, 0, 2, 0),
            Child = new TextBlock
            {
                Text = shortLabel,
                FontSize = 8.5,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(54, 111, 142))
            }
        };

        label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

        var labelX = baseX + outward * 18;

        Canvas.SetLeft(
            label,
            labelX - label.DesiredSize.Width / 2);

        Canvas.SetTop(
            label,
            baseY - 30);

        _drawingCanvas.Children.Add(label);
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
            FontSize = 7.8,
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
            Padding = new Thickness(2, 1, 2, 1),
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
