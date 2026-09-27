using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace Fassadenplaner;

public partial class MainWindow : Window
{
    private const string WidthKey = "width";
    private const string HeightKey = "height";
    private const string DepthKey = "depth";
    private const string GableKey = "gable";

    private double _widthMm = 3000;
    private double _frontWallHeightMm = 1600;
    private double _depthMm = 1800;
    private double _gableHeightMm = 800;
    private double _roofPitchDeg = 35;

    private string _selectedDimension = WidthKey;
    private string? _pendingDimensionKey;

    private readonly Dictionary<string, List<ModelUIElement3D>> _dimensionEdges = new();

    private readonly Brush _normalBrush = new SolidColorBrush(Color.FromRgb(63, 77, 89));
    private readonly Brush _selectedBrush = new SolidColorBrush(Color.FromRgb(24, 119, 173));
    private readonly Brush _roofBrush = new SolidColorBrush(Color.FromRgb(161, 173, 183));
    private readonly Brush _roofGridBrush = new SolidColorBrush(Color.FromRgb(218, 224, 229));

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
                _depthMm = value;
                break;
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
                SelectedDimensionTitle.Text = "Gaubentiefe";
                SelectedDimensionHint.Text = "Tiefe der Gaube entlang der Hauptdachfläche";
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

        var width = _widthMm * scale;
        var height = _frontWallHeightMm * scale;
        var depth = _depthMm * scale;
        var gable = _gableHeightMm * scale;
        var roofRise = Math.Tan(DegToRad(_roofPitchDeg)) * depth;

        var x0 = -width / 2.0;
        var x1 = width / 2.0;
        var zFront = depth / 2.0;
        var zBack = -depth / 2.0;

        var frontRoofY = 0.0;
        var backRoofY = roofRise;
        var eaveY = frontRoofY + height;

        AddMainRoofPlane(width, depth, scale);

        var frontBottomLeft = new Point3D(x0, frontRoofY, zFront);
        var frontBottomRight = new Point3D(x1, frontRoofY, zFront);
        var frontTopLeft = new Point3D(x0, eaveY, zFront);
        var frontTopRight = new Point3D(x1, eaveY, zFront);

        var backBottomLeft = new Point3D(x0, backRoofY, zBack);
        var backBottomRight = new Point3D(x1, backRoofY, zBack);

        // Standardgeometrie: Die Gaube läuft hinten direkt in die Hauptdachfläche.
        // Es gibt dort keine zusätzliche senkrechte Rückwand.
        var backIntersectionLeft = backBottomLeft;
        var backIntersectionRight = backBottomRight;

        AddEdge(frontBottomLeft, frontBottomRight, WidthKey);
        AddEdge(frontTopLeft, frontTopRight, WidthKey);
        AddEdge(frontBottomLeft, frontTopLeft, HeightKey);
        AddEdge(frontBottomRight, frontTopRight, HeightKey);
        AddEdge(frontBottomLeft, backIntersectionLeft, DepthKey);
        AddEdge(frontBottomRight, backIntersectionRight, DepthKey);

        // Dreieckige Gaubenwangen.
        AddPassiveEdge(frontTopLeft, backIntersectionLeft, _normalBrush, 0.025);
        AddPassiveEdge(frontTopRight, backIntersectionRight, _normalBrush, 0.025);
        AddPassiveEdge(backIntersectionLeft, backIntersectionRight, _normalBrush, 0.025);

        if (HasGable)
        {
            var frontApex = new Point3D(0, eaveY + gable, zFront);
            var backCenter = new Point3D(0, backRoofY, zBack);

            AddEdge(frontTopLeft, frontApex, GableKey);
            AddEdge(frontApex, frontTopRight, GableKey);

            // Die beiden Gaubendachflächen laufen hinten ebenfalls in der Hauptdachfläche aus.
            AddPassiveEdge(frontTopLeft, backCenter, _normalBrush, 0.025);
            AddPassiveEdge(frontTopRight, backCenter, _normalBrush, 0.025);
            AddPassiveEdge(frontApex, backCenter, _normalBrush, 0.025);
        }

        var highestY = Math.Max(backRoofY, eaveY + (HasGable ? gable : 0));

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

        var roofLineRadius = Math.Max(0.012, 18 * scale);

        AddPassiveEdge(p1, p2, _roofBrush, roofLineRadius);
        AddPassiveEdge(p2, p3, _roofBrush, roofLineRadius);
        AddPassiveEdge(p3, p4, _roofBrush, roofLineRadius);
        AddPassiveEdge(p4, p1, _roofBrush, roofLineRadius);

        for (var i = 1; i <= 5; i++)
        {
            var t = i / 6.0;
            var z = roofZFront + (roofZBack - roofZFront) * t;

            AddPassiveEdge(
                new Point3D(roofX0, RoofY(z), z),
                new Point3D(roofX1, RoofY(z), z),
                _roofGridBrush,
                Math.Max(0.008, 10 * scale));
        }
    }

    private double ComputeScale()
    {
        var roofRiseMm = Math.Tan(DegToRad(_roofPitchDeg)) * _depthMm;
        var totalHeightMm = Math.Max(
            _frontWallHeightMm + (HasGable ? _gableHeightMm : 0),
            roofRiseMm);

        var maxMm = Math.Max(_widthMm, Math.Max(totalHeightMm, _depthMm));
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
