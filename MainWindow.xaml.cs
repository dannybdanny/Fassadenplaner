using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Media.Imaging;

namespace Fassadenplaner;

public partial class MainWindow : Window
{
    private const string WidthKey = "width";
    private const string HeightKey = "height";
    private const string DepthKey = "depth";
    private const string GableKey = "gable";

    private double _widthMm = 3000;
    private double _frontWallHeightMm = 1600;
    private double _depthMm = 0;
    private double _gableHeightMm = 800;
    private double _roofPitchDeg = 35;

    private string _selectedDimension = WidthKey;
    private string? _pendingDimensionKey;

    private readonly Dictionary<string, List<ModelUIElement3D>> _dimensionEdges = new();

    private readonly Brush _normalBrush = new SolidColorBrush(Color.FromRgb(63, 77, 89));
    private readonly Brush _selectedBrush = new SolidColorBrush(Color.FromRgb(24, 119, 173));
    private readonly Brush _roofBrush = new SolidColorBrush(Color.FromRgb(120, 126, 132));
    private readonly Brush _roofGridBrush = new SolidColorBrush(Color.FromRgb(218, 224, 229));
    private readonly Brush _dormerFrontBrush = new SolidColorBrush(Color.FromRgb(232, 235, 238));
    private readonly Brush _dormerSideBrush = new SolidColorBrush(Color.FromRgb(214, 220, 225));
    private readonly Brush _dormerTopBrush = new SolidColorBrush(Color.FromRgb(199, 207, 214));

    private Point _mouseDownPosition;
    private Point _lastMousePosition;
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

        Loaded += (_, _) =>
        {
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

    private void ApplyRoofPitch()
    {
        var text = RoofPitchBox.Text.Trim().Replace(',', '.');

        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var pitch) ||
            pitch <= 0 || pitch >= 80)
        {
            MessageBox.Show("Bitte eine Dachneigung zwischen 0° und 80° eingeben.",
                "Ungültige Dachneigung",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            RoofPitchBox.Focus();
            RoofPitchBox.SelectAll();
            return;
        }

        _roofPitchDeg = pitch;
        RefreshDimensionSummary();
        BuildDormer(resetView: false);
        SelectDimension(_selectedDimension);
    }

    private void ApplySelectedDimension()
    {
        var text = DimensionValueBox.Text
            .Replace("mm", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Trim()
            .Replace(',', '.');

        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || value <= 0)
        {
            MessageBox.Show("Bitte ein gültiges Maß größer als 0 mm eingeben.",
                "Ungültiges Maß",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            DimensionValueBox.Focus();
            DimensionValueBox.SelectAll();
            return;
        }

        switch (_selectedDimension)
        {
            case WidthKey:
                _widthMm = value;
                break;
            case HeightKey:
                _frontWallHeightMm = value;
                break;
            case DepthKey:
                MessageBox.Show("Die Einbindetiefe wird bei der Flachdachgaube automatisch aus Wandhöhe und Hauptdachneigung berechnet.",
                    "Berechnetes Maß",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            case GableKey:
                _gableHeightMm = value;
                break;
        }

        RefreshDimensionSummary();
        BuildDormer(resetView: false);
        SelectDimension(_selectedDimension);
    }

    private void RefreshDimensionSummary()
    {
        _depthMm = CalculateDormerDepthMm();
        WidthValueText.Text = FormatMillimeters(_widthMm);
        HeightValueText.Text = FormatMillimeters(_frontWallHeightMm);
        DepthValueText.Text = FormatMillimeters(_depthMm);
        GableValueText.Text = FormatMillimeters(_gableHeightMm);
        PitchValueText.Text = $"{_roofPitchDeg:0.#}°";
        RoofPitchBox.Text = _roofPitchDeg.ToString("0.#", CultureInfo.InvariantCulture);
    }

    private static string FormatMillimeters(double value)
        => $"{value:0} mm";

    private void SelectDimension(string key)
    {
        _selectedDimension = key;

        switch (key)
        {
            case WidthKey:
                SelectedDimensionTitle.Text = "Gaubenbreite";
                SelectedDimensionHint.Text = "Horizontale Kante der Gaubenfront";
                DimensionValueBox.Text = _widthMm.ToString("0", CultureInfo.InvariantCulture);
                break;
            case HeightKey:
                SelectedDimensionTitle.Text = "Front-Wandhöhe";
                SelectedDimensionHint.Text = "Senkrechte Kante der Gaubenfront";
                DimensionValueBox.Text = _frontWallHeightMm.ToString("0", CultureInfo.InvariantCulture);
                break;
            case DepthKey:
                SelectedDimensionTitle.Text = "Einbindetiefe";
                SelectedDimensionHint.Text = "Wird automatisch aus Wandhöhe und Hauptdachneigung berechnet";
                DimensionValueBox.Text = _depthMm.ToString("0", CultureInfo.InvariantCulture);
                break;
            case GableKey:
                SelectedDimensionTitle.Text = "Giebelhöhe";
                SelectedDimensionHint.Text = "Zusätzliche Höhe von Traufe bis Giebelspitze";
                DimensionValueBox.Text = _gableHeightMm.ToString("0", CultureInfo.InvariantCulture);
                break;
        }

        HighlightSelectedDimension();
        DimensionValueBox.Focus();
        DimensionValueBox.SelectAll();
    }

    private void BuildDormer(bool resetView)
    {
        DormerViewport.Children.Clear();
        _dimensionEdges.Clear();

        var lights = new Model3DGroup();
        lights.Children.Add(new AmbientLight(Color.FromRgb(245, 245, 245)));
        lights.Children.Add(new DirectionalLight(Colors.White, new Vector3D(-1, -1, -2)));
        DormerViewport.Children.Add(new ModelVisual3D { Content = lights });

        var scale = ComputeScale();

        _depthMm = CalculateDormerDepthMm();

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

        AddMainRoofPlane(width, depth, scale);

        var frontBottomLeft = new Point3D(x0, frontRoofY, zFront);
        var frontBottomRight = new Point3D(x1, frontRoofY, zFront);
        var frontTopLeft = new Point3D(x0, topY, zFront);
        var frontTopRight = new Point3D(x1, topY, zFront);

        var backIntersectionLeft = new Point3D(x0, backRoofY, zBack);
        var backIntersectionRight = new Point3D(x1, backRoofY, zBack);

        // Deckende Gaubenflächen: Die Hauptdachtextur darf nicht durch die Gaube sichtbar sein.
        AddQuadSurface(frontBottomLeft, frontBottomRight, frontTopRight, frontTopLeft, _dormerFrontBrush);
        AddTriangleSurface(frontBottomLeft, frontTopLeft, backIntersectionLeft, _dormerSideBrush);
        AddTriangleSurface(frontBottomRight, backIntersectionRight, frontTopRight, _dormerSideBrush);

        if (!HasGable)
        {
            AddQuadSurface(frontTopLeft, frontTopRight, backIntersectionRight, backIntersectionLeft, _dormerTopBrush);
        }

        AddEdge(frontBottomLeft, frontBottomRight, WidthKey);
        AddEdge(frontTopLeft, frontTopRight, WidthKey);
        AddEdge(frontBottomLeft, frontTopLeft, HeightKey);
        AddEdge(frontBottomRight, frontTopRight, HeightKey);

        // Unterkante folgt exakt der Hauptdachneigung.
        AddPassiveEdge(frontBottomLeft, backIntersectionLeft, _normalBrush, 0.025);
        AddPassiveEdge(frontBottomRight, backIntersectionRight, _normalBrush, 0.025);

        // Flachdach-Oberkante waagerecht bis zum Schnittpunkt mit dem Hauptdach.
        AddPassiveEdge(frontTopLeft, backIntersectionLeft, _normalBrush, 0.025);
        AddPassiveEdge(frontTopRight, backIntersectionRight, _normalBrush, 0.025);
        AddPassiveEdge(backIntersectionLeft, backIntersectionRight, _normalBrush, 0.025);

        if (HasGable)
        {
            var frontApex = new Point3D(0, topY + gable, zFront);
            var backCenter = new Point3D(0, topY, zBack);

            AddTriangleSurface(frontTopLeft, frontApex, frontTopRight, _dormerFrontBrush);
            AddTriangleSurface(frontTopLeft, backCenter, frontApex, _dormerTopBrush);
            AddTriangleSurface(frontApex, backCenter, frontTopRight, _dormerTopBrush);

            AddEdge(frontTopLeft, frontApex, GableKey);
            AddEdge(frontApex, frontTopRight, GableKey);
            AddPassiveEdge(frontTopLeft, backCenter, _normalBrush, 0.025);
            AddPassiveEdge(frontTopRight, backCenter, _normalBrush, 0.025);
            AddPassiveEdge(frontApex, backCenter, _normalBrush, 0.025);
        }

        var highestY = topY + (HasGable ? gable : 0);

        if (resetView)
        {
            _cameraTarget = new Point3D(0, highestY * 0.42, 0);
            var size = Math.Max(width, Math.Max(highestY, depth));
            _cameraDistance = Math.Max(9.5, size * 2.75);
            _cameraYawDeg = 38;
            _cameraPitchDeg = 23;
        }

        UpdateCamera();
        HighlightSelectedDimension();
    }

    private void AddMainRoofPlane(double width, double depth, double scale)
    {
        var extensionX = Math.Max(width * 0.75, 1.4);
        var extensionFront = Math.Max(depth * 0.55, 1.2);
        var extensionBack = Math.Max(depth * 0.9, 1.8);

        var roofX0 = -width / 2 - extensionX;
        var roofX1 = width / 2 + extensionX;
        var roofZFront = depth / 2 + extensionFront;
        var roofZBack = -depth / 2 - extensionBack;

        double RoofY(double z)
        {
            var dz = depth / 2 - z;
            return Math.Tan(DegToRad(_roofPitchDeg)) * dz;
        }

        var p1 = new Point3D(roofX0, RoofY(roofZFront), roofZFront);
        var p2 = new Point3D(roofX1, RoofY(roofZFront), roofZFront);
        var p3 = new Point3D(roofX1, RoofY(roofZBack), roofZBack);
        var p4 = new Point3D(roofX0, RoofY(roofZBack), roofZBack);

        // Echte texturierte Hauptdachfläche.
        AddTexturedQuadSurface(p1, p2, p3, p4);

        var roofLineRadius = Math.Max(0.012, 18 * scale);

        AddPassiveEdge(p1, p2, _roofBrush, roofLineRadius);
        AddPassiveEdge(p2, p3, _roofBrush, roofLineRadius);
        AddPassiveEdge(p3, p4, _roofBrush, roofLineRadius);
        AddPassiveEdge(p4, p1, _roofBrush, roofLineRadius);

    }

    private double ComputeScale()
    {
        var depthMm = CalculateDormerDepthMm();
        var totalHeightMm = _frontWallHeightMm + (HasGable ? _gableHeightMm : 0);
        var maxMm = Math.Max(_widthMm, Math.Max(totalHeightMm, depthMm));
        return 3.5 / Math.Max(maxMm, 1);
    }

    private double CalculateDormerDepthMm()
    {
        var tangent = Math.Tan(DegToRad(_roofPitchDeg));
        if (tangent <= 0.0001)
            return 0;

        return _frontWallHeightMm / tangent;
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
        _isOrbiting = true;
        _leftDragExceededThreshold = false;
        _mouseDownPosition = e.GetPosition(ViewportHost);
        _lastMousePosition = _mouseDownPosition;
        ViewportHost.CaptureMouse();
    }

    private void ViewportHost_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_leftDragExceededThreshold && _pendingDimensionKey is not null)
            SelectDimension(_pendingDimensionKey);

        _pendingDimensionKey = null;
        _isOrbiting = false;

        if (!_isPanning)
            ViewportHost.ReleaseMouseCapture();
    }

    private void ViewportHost_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        _isPanning = true;
        _lastMousePosition = e.GetPosition(ViewportHost);
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
        var delta = current - _lastMousePosition;

        if (_isOrbiting && e.LeftButton == MouseButtonState.Pressed)
        {
            var totalDelta = current - _mouseDownPosition;
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
        {
            PanCamera(delta);
        }

        _lastMousePosition = current;
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

        var material = new DiffuseMaterial(brush);
        DormerViewport.Children.Add(new ModelVisual3D
        {
            Content = new GeometryModel3D(mesh, material)
            {
                BackMaterial = material
            }
        });
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

        var textureBrush = CreateRoofTextureBrush();
        var material = new DiffuseMaterial(textureBrush);

        DormerViewport.Children.Add(new ModelVisual3D
        {
            Content = new GeometryModel3D(mesh, material)
            {
                BackMaterial = material
            }
        });
    }

    private static ImageBrush CreateRoofTextureBrush()
    {
        // Kleine eingebettete Vorschau der vom Nutzer gelieferten Ziegeltextur.
        // Damit bleibt der Windows-Build vollständig standalone.
        byte[] jpeg =
        {
255,216,255,224,0,16,74,70,73,70,0,1,1,0,0,1,0,1,0,0,255,219,0,67,0,18,12,13,16,13,11,18,16,14,16,20,19,18,21,27,44,29,27,24,24,27,54,39,41,32,44,64,57,68,67,63,57,62,61,71,80,102,87,71,75,97,77,61,62,89,121,90,97,105,109,114,115,114,69,85,125,134,124,111,133,102,112,114,110,255,219,0,67,1,19,20,20,27,23,27,52,29,29,52,110,73,62,73,110,110,110,110,110,110,110,110,110,110,110,110,110,110,110,110,110,110,110,110,110,110,110,110,110,110,110,110,110,110,110,110,110,110,110,110,110,110,110,110,110,110,110,110,110,110,110,255,192,0,17,8,0,96,0,90,3,1,34,0,2,17,1,3,17,1,255,196,0,24,0,1,1,1,1,1,0,0,0,0,0,0,0,0,0,0,0,0,1,3,2,5,255,196,0,30,16,0,3,0,2,3,1,1,1,0,0,0,0,0,0,0,0,0,1,17,33,49,18,34,65,81,97,161,255,196,0,22,1,1,1,1,0,0,0,0,0,0,0,0,0,0,0,0,0,0,1,2,255,196,0,20,17,1,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,255,218,0,12,3,1,0,2,17,3,17,0,63,0,240,154,89,17,127,72,249,71,161,218,121,179,13,44,88,36,88,29,177,161,218,45,0,105,21,165,72,249,126,7,202,248,5,138,136,169,31,43,224,237,124,2,197,73,21,29,175,131,181,240,10,146,51,107,44,237,114,190,28,54,235,209,70,141,168,242,27,83,126,134,148,120,13,47,158,144,42,198,73,84,89,44,88,193,34,198,0,54,190,149,181,118,70,151,192,210,186,2,182,174,208,170,236,69,116,72,174,128,181,93,160,154,187,36,87,66,43,160,42,106,236,205,181,94,77,18,95,12,218,203,192,29,190,81,232,118,252,217,91,81,228,85,55,232,19,182,52,59,69,162,213,22,73,84,89,0,249,126,7,202,248,27,95,74,218,187,2,62,87,193,218,248,86,213,218,21,93,129,59,95,7,107,225,106,187,66,171,176,34,229,124,56,109,215,163,68,213,217,155,106,188,129,163,75,56,17,124,244,143,148,122,29,191,54,2,44,96,69,140,14,216,208,237,22,128,52,190,21,165,116,71,203,240,62,87,192,17,93,8,174,131,229,124,29,175,128,34,186,17,93,14,215,193,218,248,5,73,124,51,107,44,237,114,190,28,54,235,209,70,141,168,242,133,83,126,134,148,120,13,47,158,144,42,198,73,84,89,17,99,2,44,96,3,107,233,91,87,100,105,124,43,74,232,3,106,236,85,118,34,186,36,87,64,90,174,208,77,93,146,43,160,146,186,2,166,174,204,219,85,228,209,37,240,205,172,188,1,219,229,30,135,111,205,149,181,30,67,106,111,208,39,108,104,118,139,69,170,44,146,168,178,1,242,252,15,149,240,173,175,161,181,118,4,124,175,131,181,240,173,171,177,85,216,19,181,240,118,190,22,171,176,154,187,2,46,87,195,134,221,122,52,77,93,153,185,94,64,255,217
        };

        using var stream = new MemoryStream(jpeg);
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.StreamSource = stream;
        bitmap.EndInit();
        bitmap.Freeze();

        var brush = new ImageBrush(bitmap)
        {
            Stretch = Stretch.Fill,
            TileMode = TileMode.Tile,
            ViewportUnits = BrushMappingMode.RelativeToBoundingBox,
            Viewport = new Rect(0, 0, 0.42, 0.42),
            AlignmentX = AlignmentX.Left,
            AlignmentY = AlignmentY.Top
        };
        brush.Freeze();
        return brush;
    }

    private void AddEdge(Point3D start, Point3D end, string dimensionKey)
    {
        var model = CreateLineModel(start, end, _normalBrush, 0.035);
        var element = new ModelUIElement3D { Model = model };

        element.MouseLeftButtonDown += (_, _) =>
        {
            _pendingDimensionKey = dimensionKey;
        };

        if (!_dimensionEdges.TryGetValue(dimensionKey, out var list))
        {
            list = new List<ModelUIElement3D>();
            _dimensionEdges[dimensionKey] = list;
        }

        list.Add(element);
        DormerViewport.Children.Add(element);
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

            foreach (var element in pair.Value)
            {
                if (element.Model is GeometryModel3D geometry)
                {
                    var brush = selected ? _selectedBrush : _normalBrush;
                    geometry.Material = new DiffuseMaterial(brush);
                    geometry.BackMaterial = new DiffuseMaterial(brush);
                }
            }
        }
    }

    private static double DegToRad(double degrees)
        => degrees * Math.PI / 180.0;

    private static GeometryModel3D CreateLineModel(Point3D start, Point3D end, Brush brush, double radius)
    {
        const int sides = 10;

        var axis = end - start;
        if (axis.Length < 0.0001)
            axis = new Vector3D(0, 0.0001, 0);

        var direction = axis;
        direction.Normalize();

        var helper = Math.Abs(Vector3D.DotProduct(direction, new Vector3D(0, 1, 0))) > 0.9
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
            var offset = u * (Math.Cos(angle) * radius) + v * (Math.Sin(angle) * radius);

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
