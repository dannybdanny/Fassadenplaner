using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;

namespace Fassadenplaner;

public partial class MainWindow : Window
{
    private const string WidthKey = "width";
    private const string HeightKey = "height";
    private const string DepthKey = "depth";
    private const string SlopeKey = "slope";
    private const string GableKey = "gable";

    private double _widthMm = 3000;
    private double _frontWallHeightMm = 1600;
    private double _depthMm;
    private double _slopeLengthMm;
    private double _gableHeightMm = 800;
    private double _roofPitchDeg = 35;

    private string _selectedDimension = WidthKey;

    private readonly Dictionary<string, List<GeometryModel3D>> _dimensionEdges = new();
    private readonly Dictionary<GeometryModel3D, string> _hitEdgeKeys = new();
    private readonly Dictionary<string, Point3D> _dimensionAnchors = new();

    private readonly Brush _normalBrush = new SolidColorBrush(Color.FromRgb(63, 77, 89));
    private readonly Brush _selectedBrush = new SolidColorBrush(Color.FromRgb(24, 119, 173));
    private readonly Brush _roofBrush = new SolidColorBrush(Color.FromRgb(110, 116, 121));
    private readonly Brush _dormerFrontBrush = new SolidColorBrush(Color.FromRgb(232, 235, 238));
    private readonly Brush _dormerSideBrush = new SolidColorBrush(Color.FromRgb(214, 220, 225));
    private readonly Brush _dormerTopBrush = new SolidColorBrush(Color.FromRgb(199, 207, 214));

    private Point _mouseDownHost;
    private Point _lastMouseHost;
    private bool _isOrbiting;
    private bool _isPanning;
    private bool _leftDragExceededThreshold;

    private double _cameraYawDeg = 38;
    private double _cameraPitchDeg = 23;
    private double _cameraDistance = 12.5;
    private Point3D _cameraTarget = new(0, 1.2, 0);

    public MainWindow()
    {
        InitializeComponent();

        ViewportHost.SizeChanged += (_, _) => UpdateDimensionOverlay();

        Loaded += (_, _) =>
        {
            RecalculateFromPitchAndHeight();
            RefreshDimensionSummary();
            BuildDormer(resetView: true);
            SelectDimension(WidthKey);
        };
    }

    private bool HasGable => DormerTypeCombo.SelectedIndex == 1;

    private void DormerTypeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded)
            return;

        GableRow.Height = HasGable ? new GridLength(30) : new GridLength(0);
        GableLabel.Visibility = HasGable ? Visibility.Visible : Visibility.Collapsed;
        GableValueText.Visibility = HasGable ? Visibility.Visible : Visibility.Collapsed;

        if (!HasGable && _selectedDimension == GableKey)
            _selectedDimension = WidthKey;

        BuildDormer(resetView: false);
        SelectDimension(_selectedDimension);
    }

    private void ApplyDimension_Click(object sender, RoutedEventArgs e)
        => ApplySelectedDimension();

    private void DimensionValueBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            ApplySelectedDimension();
            e.Handled = true;
        }
    }

    private void RoofPitchBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;

        ApplyRoofPitch();
        e.Handled = true;
    }

    private void ApplyRoofPitch_Click(object sender, RoutedEventArgs e)
        => ApplyRoofPitch();

    private void ApplyRoofPitch()
    {
        var text = RoofPitchBox.Text.Trim().Replace(',', '.');

        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var pitch) ||
            pitch <= 0 || pitch >= 80)
        {
            MessageBox.Show(
                "Bitte eine Dachneigung zwischen 0° und 80° eingeben.",
                "Ungültige Dachneigung",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            RoofPitchBox.Focus();
            RoofPitchBox.SelectAll();
            return;
        }

        _roofPitchDeg = pitch;

        // Bei direkter Eingabe der Dachneigung bleibt die gemessene Front-Wandhöhe bestehen.
        // Tiefe und untere Wangenlänge ergeben sich daraus.
        RecalculateFromPitchAndHeight();

        RefreshDimensionSummary();
        BuildDormer(resetView: false);
        SelectDimension(_selectedDimension);
    }

    private void ApplySelectedDimension()
    {
        var text = DimensionValueBox.Text
            .Replace("cm", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Trim()
            .Replace(',', '.');

        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var valueCm) || valueCm <= 0)
        {
            MessageBox.Show(
                "Bitte ein gültiges Maß größer als 0 cm eingeben.",
                "Ungültiges Maß",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            DimensionValueBox.Focus();
            DimensionValueBox.SelectAll();
            return;
        }

        var valueMm = valueCm * 10.0;

        switch (_selectedDimension)
        {
            case WidthKey:
                _widthMm = valueMm;
                break;

            case HeightKey:
                _frontWallHeightMm = valueMm;
                RecalculateFromPitchAndHeight();
                break;

            case DepthKey:
                _depthMm = valueMm;
                RecalculateFromHeightAndDepth();
                break;

            case SlopeKey:
                if (valueMm <= _frontWallHeightMm)
                {
                    MessageBox.Show(
                        "Die untere Wangenlänge muss größer als die senkrechte Wandhöhe sein.",
                        "Ungültiges Dreiecksmaß",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    return;
                }

                _slopeLengthMm = valueMm;
                RecalculateFromHeightAndSlope();
                break;

            case GableKey:
                _gableHeightMm = valueMm;
                break;
        }

        RefreshDimensionSummary();
        BuildDormer(resetView: false);
        SelectDimension(_selectedDimension);
    }

    private void RecalculateFromPitchAndHeight()
    {
        var tangent = Math.Tan(DegToRad(_roofPitchDeg));
        if (tangent <= 0.0001)
            return;

        _depthMm = _frontWallHeightMm / tangent;
        _slopeLengthMm = Math.Sqrt(
            _frontWallHeightMm * _frontWallHeightMm +
            _depthMm * _depthMm);
    }

    private void RecalculateFromHeightAndDepth()
    {
        if (_depthMm <= 0)
            return;

        _roofPitchDeg = RadToDeg(Math.Atan(_frontWallHeightMm / _depthMm));
        _slopeLengthMm = Math.Sqrt(
            _frontWallHeightMm * _frontWallHeightMm +
            _depthMm * _depthMm);
    }

    private void RecalculateFromHeightAndSlope()
    {
        var square = _slopeLengthMm * _slopeLengthMm -
                     _frontWallHeightMm * _frontWallHeightMm;

        if (square <= 0)
            return;

        _depthMm = Math.Sqrt(square);
        _roofPitchDeg = RadToDeg(Math.Atan(_frontWallHeightMm / _depthMm));
    }

    private void RefreshDimensionSummary()
    {
        WidthValueText.Text = FormatCentimeters(_widthMm);
        HeightValueText.Text = FormatCentimeters(_frontWallHeightMm);
        DepthValueText.Text = FormatCentimeters(_depthMm);
        SlopeValueText.Text = FormatCentimeters(_slopeLengthMm);
        GableValueText.Text = FormatCentimeters(_gableHeightMm);
        PitchValueText.Text = $"{_roofPitchDeg:0.#}°";
        RoofPitchBox.Text = _roofPitchDeg.ToString("0.#", CultureInfo.InvariantCulture);
    }

    private static readonly CultureInfo GermanCulture = CultureInfo.GetCultureInfo("de-DE");

    private static string FormatCentimeters(double valueMm)
        => $"{(valueMm / 10.0).ToString("0.#", GermanCulture)} cm";

    private static string FormatCentimetersInput(double valueMm)
        => (valueMm / 10.0).ToString("0.#", GermanCulture);

    private void SelectDimension(string key)
    {
        _selectedDimension = key;

        switch (key)
        {
            case WidthKey:
                SelectedDimensionTitle.Text = "Gaubenbreite";
                SelectedDimensionHint.Text = "Horizontale Kante der Gaubenfront";
                DimensionValueBox.Text = FormatCentimetersInput(_widthMm);
                break;

            case HeightKey:
                SelectedDimensionTitle.Text = "Front-Wandhöhe";
                SelectedDimensionHint.Text = "Senkrechte Kante der Gaubenfront. Änderung behält die aktuelle Dachneigung bei.";
                DimensionValueBox.Text = FormatCentimetersInput(_frontWallHeightMm);
                break;

            case DepthKey:
                SelectedDimensionTitle.Text = "Gaubentiefe";
                SelectedDimensionHint.Text = "Waagerechte obere Wangenkante. Aus Höhe + Tiefe wird die Dachneigung berechnet.";
                DimensionValueBox.Text = FormatCentimetersInput(_depthMm);
                break;

            case SlopeKey:
                SelectedDimensionTitle.Text = "Untere Wangenlänge";
                SelectedDimensionHint.Text = "Gemessene schräge Kante auf dem Hauptdach. Aus Höhe + Wangenlänge wird die Dachneigung berechnet.";
                DimensionValueBox.Text = FormatCentimetersInput(_slopeLengthMm);
                break;

            case GableKey:
                SelectedDimensionTitle.Text = "Giebelhöhe";
                SelectedDimensionHint.Text = "Senkrechte Höhe von der Trauflinie bis zur Giebelspitze";
                DimensionValueBox.Text = FormatCentimetersInput(_gableHeightMm);
                break;
        }

        HighlightSelectedDimension();
        UpdateDimensionOverlay();

        DimensionValueBox.Focus();
        DimensionValueBox.SelectAll();
    }

    private void BuildDormer(bool resetView)
    {
        DormerViewport.Children.Clear();
        _dimensionEdges.Clear();
        _hitEdgeKeys.Clear();
        _dimensionAnchors.Clear();

        var lights = new Model3DGroup();
        lights.Children.Add(new AmbientLight(Color.FromRgb(245, 245, 245)));
        lights.Children.Add(new DirectionalLight(Colors.White, new Vector3D(-1, -1, -2)));
        DormerViewport.Children.Add(new ModelVisual3D { Content = lights });

        var scale = ComputeScale();

        var width = _widthMm * scale;
        var height = _frontWallHeightMm * scale;
        var depth = _depthMm * scale;
        var gable = _gableHeightMm * scale;

        var x0 = -width / 2.0;
        var x1 = width / 2.0;
        var zFront = depth / 2.0;
        var zBack = -depth / 2.0;

        var frontRoofY = 0.0;
        var topY = height;
        var backRoofY = topY;

        AddMainRoofPlane(width, depth, gable, scale);

        var frontBottomLeft = new Point3D(x0, frontRoofY, zFront);
        var frontBottomRight = new Point3D(x1, frontRoofY, zFront);
        var frontTopLeft = new Point3D(x0, topY, zFront);
        var frontTopRight = new Point3D(x1, topY, zFront);

        var backIntersectionLeft = new Point3D(x0, backRoofY, zBack);
        var backIntersectionRight = new Point3D(x1, backRoofY, zBack);

        // Deckende Wangen: durch die Gaube darf die Hauptdachfläche nicht sichtbar sein.
        AddTriangleSurface(frontBottomLeft, frontTopLeft, backIntersectionLeft, _dormerSideBrush);
        AddTriangleSurface(frontBottomRight, backIntersectionRight, frontTopRight, _dormerSideBrush);

        // Gemeinsame, auswählbare Grundkanten.
        AddEdge(frontBottomLeft, frontBottomRight, WidthKey);
        AddEdge(frontBottomLeft, frontTopLeft, HeightKey);
        AddEdge(frontBottomRight, frontTopRight, HeightKey);

        // Untere Wangenkante = Hypotenuse des rechtwinkligen Dreiecks.
        AddEdge(frontBottomLeft, backIntersectionLeft, SlopeKey);
        AddEdge(frontBottomRight, backIntersectionRight, SlopeKey);

        _dimensionAnchors[WidthKey] = MidPoint(frontBottomLeft, frontBottomRight);
        _dimensionAnchors[HeightKey] = MidPoint(frontBottomLeft, frontTopLeft);
        _dimensionAnchors[SlopeKey] = MidPoint(frontBottomLeft, backIntersectionLeft);

        if (!HasGable)
        {
            // Flachdachgaube: geschlossene Front + waagerechte obere Fläche.
            AddQuadSurface(frontBottomLeft, frontBottomRight, frontTopRight, frontTopLeft, _dormerFrontBrush);
            AddQuadSurface(frontTopLeft, frontTopRight, backIntersectionRight, backIntersectionLeft, _dormerTopBrush);

            AddEdge(frontTopLeft, frontTopRight, WidthKey);
            AddEdge(frontTopLeft, backIntersectionLeft, DepthKey);
            AddEdge(frontTopRight, backIntersectionRight, DepthKey);
            AddPassiveEdge(backIntersectionLeft, backIntersectionRight, _normalBrush, 0.008);

            _dimensionAnchors[DepthKey] = MidPoint(frontTopLeft, backIntersectionLeft);
        }
        else
        {
            var frontApex = new Point3D(0, topY + gable, zFront);

            // Der First schneidet weiter oben in die geneigte Hauptdachfläche ein.
            var pitchTan = Math.Tan(DegToRad(_roofPitchDeg));
            var ridgeDepth = pitchTan > 0.0001
                ? (topY + gable) / pitchTan
                : depth;

            var zBackRidge = zFront - ridgeDepth;
            var backRidge = new Point3D(0, topY + gable, zBackRidge);

            // Front als eine einzige geschlossene Fläche inklusive Giebel.
            AddGableFrontSurface(
                frontBottomLeft,
                frontBottomRight,
                frontTopRight,
                frontApex,
                frontTopLeft,
                _dormerFrontBrush);

            // Beide Dachflächen der Satteldachgaube reichen bis in das Hauptdach.
            AddQuadSurface(frontTopLeft, frontApex, backRidge, backIntersectionLeft, _dormerTopBrush);
            AddQuadSurface(frontApex, frontTopRight, backIntersectionRight, backRidge, _dormerTopBrush);

            AddEdge(frontTopLeft, backIntersectionLeft, DepthKey);
            AddEdge(frontTopRight, backIntersectionRight, DepthKey);

            // Front-Giebelkanten wählen die Giebelhöhe aus.
            AddEdge(frontTopLeft, frontApex, GableKey);
            AddEdge(frontApex, frontTopRight, GableKey);

            AddPassiveEdge(frontApex, backRidge, _normalBrush, 0.008);
            AddPassiveEdge(backIntersectionLeft, backRidge, _normalBrush, 0.008);
            AddPassiveEdge(backRidge, backIntersectionRight, _normalBrush, 0.008);
            AddPassiveEdge(backIntersectionLeft, backIntersectionRight, _normalBrush, 0.008);

            _dimensionAnchors[DepthKey] = MidPoint(frontTopLeft, backIntersectionLeft);
            _dimensionAnchors[GableKey] = MidPoint(
                new Point3D(0, topY, zFront),
                frontApex);
        }

        var highestY = topY + (HasGable ? gable : 0);

        if (resetView)
        {
            _cameraTarget = new Point3D(0, highestY * 0.42, 0);

            var ridgeDepth = HasGable
                ? (topY + gable) / Math.Max(Math.Tan(DegToRad(_roofPitchDeg)), 0.0001)
                : depth;

            var size = Math.Max(width, Math.Max(highestY, ridgeDepth));
            _cameraDistance = Math.Max(9.5, size * 2.75);
            _cameraYawDeg = 38;
            _cameraPitchDeg = 23;
        }

        UpdateCamera();
        HighlightSelectedDimension();

        Dispatcher.BeginInvoke(UpdateDimensionOverlay);
    }

    private void AddMainRoofPlane(double width, double depth, double gable, double scale)
    {
        var extensionX = Math.Max(width * 0.75, 1.4);
        var extensionFront = Math.Max(depth * 0.55, 1.2);

        var pitchTan = Math.Tan(DegToRad(_roofPitchDeg));
        var ridgeExtra = HasGable && pitchTan > 0.0001
            ? gable / pitchTan
            : 0;

        var extensionBack = Math.Max(depth * 0.9 + ridgeExtra + 0.6, 1.8);

        var roofX0 = -width / 2 - extensionX;
        var roofX1 = width / 2 + extensionX;
        var roofZFront = depth / 2 + extensionFront;
        var roofZBack = -depth / 2 - extensionBack;

        double RoofY(double z)
        {
            var dz = depth / 2 - z;
            return pitchTan * dz;
        }

        var p1 = new Point3D(roofX0, RoofY(roofZFront), roofZFront);
        var p2 = new Point3D(roofX1, RoofY(roofZFront), roofZFront);
        var p3 = new Point3D(roofX1, RoofY(roofZBack), roofZBack);
        var p4 = new Point3D(roofX0, RoofY(roofZBack), roofZBack);

        AddTexturedQuadSurface(p1, p2, p3, p4);

        var roofLineRadius = Math.Max(0.004, 7 * scale);
        AddPassiveEdge(p1, p2, _roofBrush, roofLineRadius);
        AddPassiveEdge(p2, p3, _roofBrush, roofLineRadius);
        AddPassiveEdge(p3, p4, _roofBrush, roofLineRadius);
        AddPassiveEdge(p4, p1, _roofBrush, roofLineRadius);
    }

    private double ComputeScale()
    {
        var totalHeightMm = _frontWallHeightMm + (HasGable ? _gableHeightMm : 0);

        var ridgeDepthMm = HasGable
            ? totalHeightMm / Math.Max(Math.Tan(DegToRad(_roofPitchDeg)), 0.0001)
            : _depthMm;

        var maxMm = Math.Max(
            _widthMm,
            Math.Max(totalHeightMm, Math.Max(_depthMm, ridgeDepthMm)));

        return 3.5 / Math.Max(maxMm, 1);
    }

    private void ViewportHost_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        var factor = e.Delta > 0 ? 0.88 : 1.14;
        _cameraDistance *= factor;
        _cameraDistance = Math.Clamp(_cameraDistance, 2.5, 60.0);

        UpdateCamera();
        e.Handled = true;
    }

    private void ViewportHost_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var button = FindButtonAncestor(e.OriginalSource as DependencyObject);
        if (button?.Tag is string dimensionKey)
        {
            SelectDimension(dimensionKey);
            return;
        }

        _isOrbiting = true;
        _leftDragExceededThreshold = false;
        _mouseDownHost = e.GetPosition(ViewportHost);
        _lastMouseHost = _mouseDownHost;
        ViewportHost.CaptureMouse();
    }

    private void ViewportHost_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_isOrbiting && !_leftDragExceededThreshold)
        {
            var key = HitTestDimension(e.GetPosition(DormerViewport));
            if (key is not null)
                SelectDimension(key);
        }

        _isOrbiting = false;

        if (!_isPanning)
            ViewportHost.ReleaseMouseCapture();
    }

    private void ViewportHost_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (FindButtonAncestor(e.OriginalSource as DependencyObject) is not null)
            return;

        _isPanning = true;
        _lastMouseHost = e.GetPosition(ViewportHost);
        ViewportHost.CaptureMouse();
    }

    private void ViewportHost_PreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        _isPanning = false;

        if (!_isOrbiting)
            ViewportHost.ReleaseMouseCapture();
    }

    private void ViewportHost_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        var current = e.GetPosition(ViewportHost);
        var delta = current - _lastMouseHost;

        if (_isOrbiting && e.LeftButton == MouseButtonState.Pressed)
        {
            var totalDelta = current - _mouseDownHost;

            if (Math.Abs(totalDelta.X) > 4 || Math.Abs(totalDelta.Y) > 4)
                _leftDragExceededThreshold = true;

            if (_leftDragExceededThreshold)
            {
                _cameraYawDeg -= delta.X * 0.34;
                _cameraPitchDeg -= delta.Y * 0.28;
                _cameraPitchDeg = Math.Clamp(_cameraPitchDeg, -12, 82);
                UpdateCamera();
            }
        }

        if (_isPanning && e.RightButton == MouseButtonState.Pressed)
            PanCamera(delta);

        _lastMouseHost = current;
    }

    private void PanCamera(Vector delta)
    {
        var look = DormerCamera.LookDirection;
        look.Normalize();

        var up = DormerCamera.UpDirection;
        up.Normalize();

        var right = Vector3D.CrossProduct(look, up);
        if (right.Length < 0.0001)
            return;

        right.Normalize();

        var screenUp = Vector3D.CrossProduct(right, look);
        screenUp.Normalize();

        var scale = _cameraDistance * 0.0018;

        _cameraTarget += right * (-delta.X * scale);
        _cameraTarget += screenUp * (delta.Y * scale);

        UpdateCamera();
    }

    private void UpdateCamera()
    {
        var yaw = DegToRad(_cameraYawDeg);
        var pitch = DegToRad(_cameraPitchDeg);

        var horizontal = _cameraDistance * Math.Cos(pitch);

        var x = _cameraTarget.X + horizontal * Math.Sin(yaw);
        var z = _cameraTarget.Z + horizontal * Math.Cos(yaw);
        var y = _cameraTarget.Y + _cameraDistance * Math.Sin(pitch);

        DormerCamera.Position = new Point3D(x, y, z);
        DormerCamera.LookDirection = _cameraTarget - DormerCamera.Position;
        DormerCamera.UpDirection = new Vector3D(0, 1, 0);

        UpdateDimensionOverlay();
    }

    private string? HitTestDimension(Point point)
    {
        string? hitKey = null;

        VisualTreeHelper.HitTest(
            DormerViewport,
            null,
            result =>
            {
                if (result is RayMeshGeometry3DHitTestResult ray &&
                    ray.ModelHit is GeometryModel3D model &&
                    _hitEdgeKeys.TryGetValue(model, out var key))
                {
                    hitKey = key;
                    return HitTestResultBehavior.Stop;
                }

                return HitTestResultBehavior.Continue;
            },
            new PointHitTestParameters(point));

        return hitKey;
    }

    private static Button? FindButtonAncestor(DependencyObject? source)
    {
        var current = source;

        while (current is not null)
        {
            if (current is Button button)
                return button;

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    private void AddTriangleSurface(Point3D p1, Point3D p2, Point3D p3, Brush brush)
    {
        var mesh = new MeshGeometry3D();

        mesh.Positions.Add(p1);
        mesh.Positions.Add(p2);
        mesh.Positions.Add(p3);

        mesh.TriangleIndices.Add(0);
        mesh.TriangleIndices.Add(1);
        mesh.TriangleIndices.Add(2);

        AddSurface(mesh, brush);
    }

    private void AddQuadSurface(Point3D p1, Point3D p2, Point3D p3, Point3D p4, Brush brush)
    {
        var mesh = new MeshGeometry3D();

        mesh.Positions.Add(p1);
        mesh.Positions.Add(p2);
        mesh.Positions.Add(p3);
        mesh.Positions.Add(p4);

        mesh.TriangleIndices.Add(0);
        mesh.TriangleIndices.Add(1);
        mesh.TriangleIndices.Add(2);

        mesh.TriangleIndices.Add(0);
        mesh.TriangleIndices.Add(2);
        mesh.TriangleIndices.Add(3);

        AddSurface(mesh, brush);
    }

    private void AddGableFrontSurface(
        Point3D bottomLeft,
        Point3D bottomRight,
        Point3D topRight,
        Point3D apex,
        Point3D topLeft,
        Brush brush)
    {
        var mesh = new MeshGeometry3D();

        mesh.Positions.Add(bottomLeft);   // 0
        mesh.Positions.Add(bottomRight);  // 1
        mesh.Positions.Add(topRight);     // 2
        mesh.Positions.Add(apex);         // 3
        mesh.Positions.Add(topLeft);      // 4

        mesh.TriangleIndices.Add(0);
        mesh.TriangleIndices.Add(1);
        mesh.TriangleIndices.Add(2);

        mesh.TriangleIndices.Add(0);
        mesh.TriangleIndices.Add(2);
        mesh.TriangleIndices.Add(4);

        mesh.TriangleIndices.Add(4);
        mesh.TriangleIndices.Add(2);
        mesh.TriangleIndices.Add(3);

        AddSurface(mesh, brush);
    }

    private void AddSurface(MeshGeometry3D mesh, Brush brush)
    {
        var material = new DiffuseMaterial(brush);

        DormerViewport.Children.Add(new ModelVisual3D
        {
            Content = new GeometryModel3D(mesh, material)
            {
                BackMaterial = material
            }
        });
    }

    private void AddTexturedQuadSurface(Point3D p1, Point3D p2, Point3D p3, Point3D p4)
    {
        var mesh = new MeshGeometry3D();

        mesh.Positions.Add(p1);
        mesh.Positions.Add(p2);
        mesh.Positions.Add(p3);
        mesh.Positions.Add(p4);

        // Ein einziges UV-Rechteck über die gesamte Dachfläche.
        mesh.TextureCoordinates.Add(new Point(0, 1));
        mesh.TextureCoordinates.Add(new Point(1, 1));
        mesh.TextureCoordinates.Add(new Point(1, 0));
        mesh.TextureCoordinates.Add(new Point(0, 0));

        mesh.TriangleIndices.Add(0);
        mesh.TriangleIndices.Add(1);
        mesh.TriangleIndices.Add(2);

        mesh.TriangleIndices.Add(0);
        mesh.TriangleIndices.Add(2);
        mesh.TriangleIndices.Add(3);

        var roofWidth = (p2 - p1).Length;
        var roofSlopeLength = (p4 - p1).Length;

        var textureBrush = CreateSeamlessRoofTextureBrush(roofWidth, roofSlopeLength);
        var material = new DiffuseMaterial(textureBrush);

        DormerViewport.Children.Add(new ModelVisual3D
        {
            Content = new GeometryModel3D(mesh, material)
            {
                BackMaterial = material
            }
        });
    }

    private static Brush CreateSeamlessRoofTextureBrush(double roofWidth, double roofSlopeLength)
    {
        try
        {
            var resource = Application.GetResourceStream(
                new Uri("Assets/roof_tiles.jpg", UriKind.Relative));

            if (resource is null)
                throw new InvalidOperationException("Ziegeltextur wurde nicht als WPF-Ressource gefunden.");

            BitmapImage source;

            using (var stream = resource.Stream)
            {
                source = new BitmapImage();
                source.BeginInit();
                source.CacheOption = BitmapCacheOption.OnLoad;
                source.StreamSource = stream;
                source.EndInit();
                source.Freeze();
            }

            // Wir erzeugen zuerst EIN fertiges lückenloses Bild.
            // Das WPF-3D-Material muss danach selbst nichts mehr kacheln.
            var sourceWidth = Math.Max(1, source.PixelWidth);
            var sourceHeight = Math.Max(1, source.PixelHeight);

            var roofAspect = roofSlopeLength > 0.0001
                ? roofWidth / roofSlopeLength
                : 1.0;

            // Zwei komplette Texturhöhen ergeben einen ruhigen,
            // aber noch deutlich erkennbaren Ziegelmaßstab.
            var targetHeight = Math.Clamp(sourceHeight * 2, 256, 1024);
            var targetWidth = Math.Clamp(
                (int)Math.Round(targetHeight * roofAspect),
                256,
                1536);

            var visual = new DrawingVisual();

            using (var dc = visual.RenderOpen())
            {
                dc.DrawRectangle(
                    new SolidColorBrush(Color.FromRgb(72, 67, 60)),
                    null,
                    new Rect(0, 0, targetWidth, targetHeight));

                for (var y = 0; y < targetHeight; y += sourceHeight)
                {
                    for (var x = 0; x < targetWidth; x += sourceWidth)
                    {
                        dc.DrawImage(
                            source,
                            new Rect(x, y, sourceWidth, sourceHeight));
                    }
                }
            }

            var tiledBitmap = new RenderTargetBitmap(
                targetWidth,
                targetHeight,
                96,
                96,
                PixelFormats.Pbgra32);

            tiledBitmap.Render(visual);
            tiledBitmap.Freeze();

            var brush = new ImageBrush(tiledBitmap)
            {
                Stretch = Stretch.Fill,
                TileMode = TileMode.None,
                AlignmentX = AlignmentX.Left,
                AlignmentY = AlignmentY.Top
            };

            brush.Freeze();
            return brush;
        }
        catch
        {
            // Nur als Notfall-Fallback; normal darf dieser Zweig nicht mehr sichtbar werden.
            var fallback = new SolidColorBrush(Color.FromRgb(105, 100, 94));
            fallback.Freeze();
            return fallback;
        }
    }

    private void AddEdge(Point3D start, Point3D end, string dimensionKey)
    {
        // Dünne sichtbare Kontur.
        var visibleModel = CreateLineModel(start, end, _normalBrush, 0.012);

        DormerViewport.Children.Add(new ModelVisual3D
        {
            Content = visibleModel
        });

        if (!_dimensionEdges.TryGetValue(dimensionKey, out var list))
        {
            list = new List<GeometryModel3D>();
            _dimensionEdges[dimensionKey] = list;
        }

        list.Add(visibleModel);

        // Größerer unsichtbarer Trefferbereich: dünne Linien bleiben trotzdem leicht anklickbar.
        var hitModel = CreateLineModel(start, end, Brushes.Transparent, 0.060);
        _hitEdgeKeys[hitModel] = dimensionKey;

        DormerViewport.Children.Add(new ModelVisual3D
        {
            Content = hitModel
        });
    }

    private void AddPassiveEdge(Point3D start, Point3D end, Brush brush, double radius)
    {
        DormerViewport.Children.Add(new ModelVisual3D
        {
            Content = CreateLineModel(start, end, brush, radius)
        });
    }

    private void HighlightSelectedDimension()
    {
        foreach (var pair in _dimensionEdges)
        {
            var selected = pair.Key == _selectedDimension;
            var brush = selected ? _selectedBrush : _normalBrush;

            foreach (var geometry in pair.Value)
            {
                geometry.Material = new DiffuseMaterial(brush);
                geometry.BackMaterial = new DiffuseMaterial(brush);
            }
        }
    }

    private void UpdateDimensionOverlay()
    {
        if (!IsLoaded ||
            DimensionOverlay.ActualWidth <= 1 ||
            DimensionOverlay.ActualHeight <= 1)
        {
            return;
        }

        DimensionOverlay.Children.Clear();

        foreach (var pair in _dimensionAnchors)
        {
            var projected = ProjectToOverlay(pair.Value);
            if (projected is null)
                continue;

            var key = pair.Key;
            var selected = key == _selectedDimension;

            var button = new Button
            {
                Tag = key,
                Content = GetDimensionOverlayText(key),
                Height = 23,
                Padding = new Thickness(5, 0, 5, 0),
                FontSize = 10.5,
                FontWeight = FontWeights.SemiBold,
                Background = selected
                    ? new SolidColorBrush(Color.FromRgb(229, 243, 251))
                    : new SolidColorBrush(Color.FromArgb(235, 255, 255, 255)),
                Foreground = new SolidColorBrush(Color.FromRgb(42, 55, 65)),
                BorderBrush = selected
                    ? _selectedBrush
                    : new SolidColorBrush(Color.FromRgb(174, 183, 191)),
                BorderThickness = new Thickness(selected ? 1.5 : 1),
                Cursor = Cursors.Hand
            };

            button.Click += DimensionLabel_Click;
            button.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

            Canvas.SetLeft(button, projected.Value.X - button.DesiredSize.Width / 2);
            Canvas.SetTop(button, projected.Value.Y - button.DesiredSize.Height / 2 - 5);

            DimensionOverlay.Children.Add(button);
        }
    }

    private void DimensionLabel_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string key })
            SelectDimension(key);
    }

    private string GetDimensionOverlayText(string key) => key switch
    {
        WidthKey => FormatCentimeters(_widthMm),
        HeightKey => FormatCentimeters(_frontWallHeightMm),
        DepthKey => FormatCentimeters(_depthMm),
        SlopeKey => FormatCentimeters(_slopeLengthMm),
        GableKey => FormatCentimeters(_gableHeightMm),
        _ => string.Empty
    };

    private Point? ProjectToOverlay(Point3D point)
    {
        var width = DimensionOverlay.ActualWidth;
        var height = DimensionOverlay.ActualHeight;

        if (width <= 1 || height <= 1)
            return null;

        var forward = DormerCamera.LookDirection;
        if (forward.Length < 0.0001)
            return null;

        forward.Normalize();

        var up = DormerCamera.UpDirection;
        up.Normalize();

        var right = Vector3D.CrossProduct(forward, up);
        if (right.Length < 0.0001)
            return null;

        right.Normalize();

        var trueUp = Vector3D.CrossProduct(right, forward);
        trueUp.Normalize();

        var relative = point - DormerCamera.Position;
        var z = Vector3D.DotProduct(relative, forward);

        if (z <= 0.01)
            return null;

        var tanHalfHorizontal =
            Math.Tan(DegToRad(DormerCamera.FieldOfView) / 2.0);

        var aspect = width / height;
        var tanHalfVertical =
            tanHalfHorizontal / Math.Max(aspect, 0.01);

        var nx =
            Vector3D.DotProduct(relative, right) /
            (z * tanHalfHorizontal);

        var ny =
            Vector3D.DotProduct(relative, trueUp) /
            (z * tanHalfVertical);

        if (Math.Abs(nx) > 1.25 || Math.Abs(ny) > 1.25)
            return null;

        return new Point(
            (nx + 1.0) * 0.5 * width,
            (1.0 - ny) * 0.5 * height);
    }

    private static Point3D MidPoint(Point3D a, Point3D b)
        => new(
            (a.X + b.X) / 2.0,
            (a.Y + b.Y) / 2.0,
            (a.Z + b.Z) / 2.0);

    private static double DegToRad(double degrees)
        => degrees * Math.PI / 180.0;

    private static double RadToDeg(double radians)
        => radians * 180.0 / Math.PI;

    private static GeometryModel3D CreateLineModel(
        Point3D start,
        Point3D end,
        Brush brush,
        double radius)
    {
        const int sides = 10;

        var axis = end - start;

        if (axis.Length < 0.0001)
            axis = new Vector3D(0, 0.0001, 0);

        var direction = axis;
        direction.Normalize();

        var helper =
            Math.Abs(Vector3D.DotProduct(
                direction,
                new Vector3D(0, 1, 0))) > 0.9
            ? new Vector3D(1, 0, 0)
            : new Vector3D(0, 1, 0);

        var u = Vector3D.CrossProduct(direction, helper);
        u.Normalize();

        var v = Vector3D.CrossProduct(direction, u);
        v.Normalize();

        var mesh = new MeshGeometry3D();

        for (var i = 0; i < sides; i++)
        {
            var angle = 2 * Math.PI * i / sides;

            var offset =
                u * (Math.Cos(angle) * radius) +
                v * (Math.Sin(angle) * radius);

            mesh.Positions.Add(start + offset);
            mesh.Positions.Add(end + offset);
        }

        for (var i = 0; i < sides; i++)
        {
            var next = (i + 1) % sides;

            var a = i * 2;
            var b = a + 1;
            var c = next * 2;
            var d = c + 1;

            mesh.TriangleIndices.Add(a);
            mesh.TriangleIndices.Add(c);
            mesh.TriangleIndices.Add(b);

            mesh.TriangleIndices.Add(b);
            mesh.TriangleIndices.Add(c);
            mesh.TriangleIndices.Add(d);
        }

        var material = new DiffuseMaterial(brush);

        return new GeometryModel3D(mesh, material)
        {
            BackMaterial = material
        };
    }
}
