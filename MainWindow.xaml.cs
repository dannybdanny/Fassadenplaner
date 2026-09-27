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
    private double _heightMm = 1600;
    private double _depthMm = 1800;
    private double _gableHeightMm = 800;

    private string _selectedDimension = WidthKey;

    private readonly Dictionary<string, List<ModelUIElement3D>> _dimensionEdges = new();

    private readonly Brush _normalBrush = new SolidColorBrush(Color.FromRgb(64, 78, 90));
    private readonly Brush _selectedBrush = new SolidColorBrush(Color.FromRgb(23, 119, 173));

    public MainWindow()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            RefreshDimensionSummary();
            BuildDormer();
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
            SelectDimension(WidthKey);

        BuildDormer();
    }

    private void ApplyDimension_Click(object sender, RoutedEventArgs e)
    {
        ApplySelectedDimension();
    }

    private void DimensionValueBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            ApplySelectedDimension();
            e.Handled = true;
        }
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
                _heightMm = value;
                break;
            case DepthKey:
                _depthMm = value;
                break;
            case GableKey:
                _gableHeightMm = value;
                break;
        }

        RefreshDimensionSummary();
        BuildDormer();
        SelectDimension(_selectedDimension);
    }

    private void RefreshDimensionSummary()
    {
        WidthValueText.Text = FormatMillimeters(_widthMm);
        HeightValueText.Text = FormatMillimeters(_heightMm);
        DepthValueText.Text = FormatMillimeters(_depthMm);
        GableValueText.Text = FormatMillimeters(_gableHeightMm);
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
                SelectedDimensionTitle.Text = "Wandhöhe";
                SelectedDimensionHint.Text = "Senkrechte Kante von Unterkante bis Traufe";
                DimensionValueBox.Text = _heightMm.ToString("0", CultureInfo.InvariantCulture);
                break;
            case DepthKey:
                SelectedDimensionTitle.Text = "Gaubentiefe";
                SelectedDimensionHint.Text = "Seitliche Kante der Gaubenwange";
                DimensionValueBox.Text = _depthMm.ToString("0", CultureInfo.InvariantCulture);
                break;
            case GableKey:
                SelectedDimensionTitle.Text = "Giebelhöhe";
                SelectedDimensionHint.Text = "Höhe von Traufe bis Giebelspitze";
                DimensionValueBox.Text = _gableHeightMm.ToString("0", CultureInfo.InvariantCulture);
                break;
        }

        HighlightSelectedDimension();
        DimensionValueBox.Focus();
        DimensionValueBox.SelectAll();
    }

    private void BuildDormer()
    {
        DormerViewport.Children.Clear();
        _dimensionEdges.Clear();

        var lights = new Model3DGroup();
        lights.Children.Add(new AmbientLight(Color.FromRgb(245, 245, 245)));
        lights.Children.Add(new DirectionalLight(Colors.White, new Vector3D(-1, -1, -2)));
        DormerViewport.Children.Add(new ModelVisual3D { Content = lights });

        var scale = ComputeScale();
        var width = _widthMm * scale;
        var height = _heightMm * scale;
        var depth = _depthMm * scale;
        var gable = _gableHeightMm * scale;

        var x0 = -width / 2.0;
        var x1 = width / 2.0;
        var zFront = depth / 2.0;
        var zBack = -depth / 2.0;
        const double y0 = 0;
        var y1 = height;

        var frontBottomLeft = new Point3D(x0, y0, zFront);
        var frontBottomRight = new Point3D(x1, y0, zFront);
        var frontTopLeft = new Point3D(x0, y1, zFront);
        var frontTopRight = new Point3D(x1, y1, zFront);

        var backBottomLeft = new Point3D(x0, y0, zBack);
        var backBottomRight = new Point3D(x1, y0, zBack);
        var backTopLeft = new Point3D(x0, y1, zBack);
        var backTopRight = new Point3D(x1, y1, zBack);

        AddEdge(frontBottomLeft, frontBottomRight, WidthKey);
        AddEdge(backBottomLeft, backBottomRight, WidthKey);
        AddEdge(frontTopLeft, frontTopRight, WidthKey);
        AddEdge(backTopLeft, backTopRight, WidthKey);

        AddEdge(frontBottomLeft, frontTopLeft, HeightKey);
        AddEdge(frontBottomRight, frontTopRight, HeightKey);
        AddEdge(backBottomLeft, backTopLeft, HeightKey);
        AddEdge(backBottomRight, backTopRight, HeightKey);

        AddEdge(frontBottomLeft, backBottomLeft, DepthKey);
        AddEdge(frontBottomRight, backBottomRight, DepthKey);

        if (HasGable)
        {
            AddEdge(frontTopLeft, backTopLeft, DepthKey);
            AddEdge(frontTopRight, backTopRight, DepthKey);

            var frontApex = new Point3D(0, y1 + gable, zFront);
            var backApex = new Point3D(0, y1 + gable, zBack);

            AddEdge(frontTopLeft, frontApex, GableKey);
            AddEdge(frontApex, frontTopRight, GableKey);
            AddEdge(backTopLeft, backApex, GableKey);
            AddEdge(backApex, backTopRight, GableKey);
            AddEdge(frontApex, backApex, DepthKey);
        }
        else
        {
            AddEdge(frontTopLeft, backTopLeft, DepthKey);
            AddEdge(frontTopRight, backTopRight, DepthKey);
        }

        AddGroundReference(width, depth, scale);
        FitCamera(width, height + (HasGable ? gable : 0), depth);
        HighlightSelectedDimension();
    }

    private double ComputeScale()
    {
        var maxMm = Math.Max(_widthMm, Math.Max(_heightMm + (HasGable ? _gableHeightMm : 0), _depthMm));
        return 4.2 / Math.Max(maxMm, 1);
    }

    private void AddGroundReference(double width, double depth, double scale)
    {
        var margin = Math.Max(250 * scale, 0.25);
        var y = -0.035;
        var x0 = -width / 2 - margin;
        var x1 = width / 2 + margin;
        var z0 = -depth / 2 - margin;
        var z1 = depth / 2 + margin;

        var brush = new SolidColorBrush(Color.FromRgb(207, 214, 221));
        AddPassiveEdge(new Point3D(x0, y, z0), new Point3D(x1, y, z0), brush, 0.014);
        AddPassiveEdge(new Point3D(x1, y, z0), new Point3D(x1, y, z1), brush, 0.014);
        AddPassiveEdge(new Point3D(x1, y, z1), new Point3D(x0, y, z1), brush, 0.014);
        AddPassiveEdge(new Point3D(x0, y, z1), new Point3D(x0, y, z0), brush, 0.014);
    }

    private void FitCamera(double width, double totalHeight, double depth)
    {
        var size = Math.Max(width, Math.Max(totalHeight, depth));
        var centerY = totalHeight * 0.45;

        DormerCamera.Position = new Point3D(size * 1.45, centerY + size * 0.75, size * 1.65);
        DormerCamera.LookDirection = new Vector3D(
            -DormerCamera.Position.X,
            centerY - DormerCamera.Position.Y,
            -DormerCamera.Position.Z);
        DormerCamera.UpDirection = new Vector3D(0, 1, 0);
    }

    private void AddEdge(Point3D start, Point3D end, string dimensionKey)
    {
        var model = CreateLineModel(start, end, _normalBrush, 0.035);
        var element = new ModelUIElement3D { Model = model };
        element.MouseLeftButtonDown += (_, e) =>
        {
            SelectDimension(dimensionKey);
            e.Handled = true;
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

    private static GeometryModel3D CreateLineModel(Point3D start, Point3D end, Brush brush, double radius)
    {
        const int sides = 10;
        var axis = end - start;
        var length = axis.Length;

        if (length < 0.0001)
            length = 0.0001;

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
