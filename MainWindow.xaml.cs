using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using System.Windows.Shapes;

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
    private double _dormerRoofPitchDeg = 10;

    private string _selectedDimension = WidthKey;

    private readonly Dictionary<string, List<GeometryModel3D>> _dimensionEdges = new();
    private readonly List<(Point3D Start, Point3D End, string Key)> _dimensionHitSegments = new();
    private readonly Dictionary<GeometryModel3D, string> _surfaceKeys = new();
    private readonly Dictionary<string, Point3D> _dimensionAnchors = new();

    private string? _selectedSurfaceKey;

    private readonly Brush _normalBrush = new SolidColorBrush(Color.FromRgb(39, 52, 64));
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

    private UpdateInfo? _pendingUpdate;

    public MainWindow()
    {
        InitializeComponent();

        InstalledVersionText.Text = $"Installiert: {UpdateService.CurrentVersionDisplay}";

        ViewportHost.SizeChanged += (_, _) =>
        {
            UpdateDimensionOverlay();
            UpdateSurfaceDetail();
        };

        Loaded += (_, _) =>
        {
            RecalculateFromPitchAndHeight();
            RefreshDimensionSummary();
            BuildDormer(resetView: true);
            SelectDimension(WidthKey);
            ShowDormerTool();
            RecalculateCladding();
        };
    }

    private async void CheckForUpdates_Click(object sender, RoutedEventArgs e)
    {
        CheckUpdatesButton.IsEnabled = false;
        InstallUpdateButton.Visibility = Visibility.Collapsed;
        UpdateStatusText.Text = "Suche nach einer neueren Version …";

        try
        {
            _pendingUpdate = await UpdateService.CheckForUpdateAsync();

            if (_pendingUpdate is null)
            {
                UpdateStatusText.Text =
                    $"Du hast bereits die aktuelle Version {UpdateService.CurrentVersionDisplay}.";
                return;
            }

            UpdateStatusText.Text =
                $"Version {_pendingUpdate.Version} ist verfügbar." +
                (string.IsNullOrWhiteSpace(_pendingUpdate.Notes)
                    ? string.Empty
                    : $"  {_pendingUpdate.Notes}");

            InstallUpdateButton.Content =
                $"Version {_pendingUpdate.Version} installieren";
            InstallUpdateButton.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            UpdateStatusText.Text =
                "Updateprüfung nicht möglich: " + ex.Message;
        }
        finally
        {
            CheckUpdatesButton.IsEnabled = true;
        }
    }

    private async void InstallUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (_pendingUpdate is null)
            return;

        InstallUpdateButton.IsEnabled = false;
        CheckUpdatesButton.IsEnabled = false;
        UpdateStatusText.Text =
            $"Version {_pendingUpdate.Version} wird vorbereitet …";

        try
        {
            var installerPath =
                await UpdateService.PrepareInstallerAsync(_pendingUpdate);

            UpdateStatusText.Text = "Installer wird gestartet …";

            UpdateService.StartInstaller(installerPath);
            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            UpdateStatusText.Text =
                "Update konnte nicht gestartet werden: " + ex.Message;
            InstallUpdateButton.IsEnabled = true;
            CheckUpdatesButton.IsEnabled = true;
        }
    }

    private void DormerTool_Click(object sender, RoutedEventArgs e)
        => ShowDormerTool();

    private void CladdingTool_Click(object sender, RoutedEventArgs e)
        => ShowCladdingTool();

    private void ShowDormerTool()
    {
        DormerPropertiesPanel.Visibility = Visibility.Visible;
        CladdingPropertiesPanel.Visibility = Visibility.Collapsed;

        ToolPanelTitle.Text = "Gaube";
        ToolPanelSubtitle.Text = "Geometrie, Maße und Flächen";

        DormerToolButton.Background =
            new SolidColorBrush(Color.FromRgb(231, 241, 247));
        DormerToolButton.BorderBrush =
            new SolidColorBrush(Color.FromRgb(112, 153, 176));

        CladdingToolButton.Background =
            new SolidColorBrush(Color.FromRgb(247, 249, 251));
        CladdingToolButton.BorderBrush =
            new SolidColorBrush(Color.FromRgb(213, 222, 229));
    }

    private void ShowCladdingTool()
    {
        DormerPropertiesPanel.Visibility = Visibility.Collapsed;
        CladdingPropertiesPanel.Visibility = Visibility.Visible;

        ToolPanelTitle.Text = "Bekleidung";
        ToolPanelSubtitle.Text = "Coil, Falz, Deckbreite und Umschläge";

        CladdingToolButton.Background =
            new SolidColorBrush(Color.FromRgb(231, 241, 247));
        CladdingToolButton.BorderBrush =
            new SolidColorBrush(Color.FromRgb(112, 153, 176));

        DormerToolButton.Background =
            new SolidColorBrush(Color.FromRgb(247, 249, 251));
        DormerToolButton.BorderBrush =
            new SolidColorBrush(Color.FromRgb(213, 222, 229));

        RecalculateCladding();
    }

    private void CladdingValue_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!IsLoaded)
            return;

        RecalculateCladding();
    }

    private void RecalculateCladding()
    {
        if (CoilWidthBox is null ||
            OverlapBox is null ||
            UnderlapBox is null ||
            TopAllowanceBox is null ||
            BottomAllowanceBox is null)
        {
            return;
        }

        var coil = ParseMillimeters(CoilWidthBox.Text);
        var overlap = ParseMillimeters(OverlapBox.Text);
        var underlap = ParseMillimeters(UnderlapBox.Text);
        var top = ParseMillimeters(TopAllowanceBox.Text);
        var bottom = ParseMillimeters(BottomAllowanceBox.Text);

        var deckWidth = coil - overlap - underlap;

        if (coil <= 0 || overlap < 0 || underlap < 0 || deckWidth <= 0)
        {
            DeckWidthText.Text = "–";
            DeckWidthText.Foreground =
                new SolidColorBrush(Color.FromRgb(173, 66, 66));

            CladdingSummaryText.Text =
                "Bitte gültige Werte eingeben. Coilbreite muss größer als Überdeck + Unterdeck sein.";
            return;
        }

        DeckWidthText.Text = $"{deckWidth:0.#} mm";
        DeckWidthText.Foreground =
            new SolidColorBrush(Color.FromRgb(30, 83, 107));

        CladdingSummaryText.Text =
            $"{coil:0.#} mm Coil → {deckWidth:0.#} mm Deckbreite · " +
            $"oben {Math.Max(top, 0):0.#} mm · unten {Math.Max(bottom, 0):0.#} mm";
    }

    private static double ParseMillimeters(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return 0;

        var normalized = text
            .Replace("mm", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Trim()
            .Replace(',', '.');

        return double.TryParse(
            normalized,
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out var value)
            ? value
            : 0;
    }

    private bool HasGable => DormerTypeCombo.SelectedIndex == 1;
    private bool HasShedRoof => DormerTypeCombo.SelectedIndex == 2;

    private void DormerTypeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded)
            return;

        GableRow.Height = HasGable ? new GridLength(30) : new GridLength(0);
        GableLabel.Visibility = HasGable ? Visibility.Visible : Visibility.Collapsed;
        GableValueText.Visibility = HasGable ? Visibility.Visible : Visibility.Collapsed;

        ShedPitchRow.Height = HasShedRoof ? new GridLength(30) : new GridLength(0);
        DormerPitchPanel.Visibility = HasShedRoof ? Visibility.Visible : Visibility.Collapsed;
        DormerPitchLabel.Visibility = HasShedRoof ? Visibility.Visible : Visibility.Collapsed;
        DormerPitchValueText.Visibility = HasShedRoof ? Visibility.Visible : Visibility.Collapsed;

        if (!HasGable && _selectedDimension == GableKey)
            _selectedDimension = WidthKey;

        RecalculateFromPitchAndHeight();
        RefreshDimensionSummary();
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

    private void DormerPitchBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;

        ApplyDormerPitch();
        e.Handled = true;
    }

    private void ApplyDormerPitch_Click(object sender, RoutedEventArgs e)
        => ApplyDormerPitch();

    private void ApplyDormerPitch()
    {
        var text = DormerPitchBox.Text.Trim().Replace(',', '.');

        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var pitch) ||
            pitch < 0 || pitch >= 60)
        {
            MessageBox.Show(
                "Bitte eine Gaubendachneigung zwischen 0° und 60° eingeben.",
                "Ungültige Gaubendachneigung",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            DormerPitchBox.Focus();
            DormerPitchBox.SelectAll();
            return;
        }

        if (pitch >= _roofPitchDeg)
        {
            MessageBox.Show(
                "Die Gaubendachneigung muss kleiner als die Hauptdachneigung sein.",
                "Neigungen passen nicht zusammen",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            DormerPitchBox.Focus();
            DormerPitchBox.SelectAll();
            return;
        }

        _dormerRoofPitchDeg = pitch;
        RecalculateFromPitchAndHeight();
        RefreshDimensionSummary();
        BuildDormer(resetView: false);
        SelectDimension(_selectedDimension);
    }

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

        if (HasShedRoof && pitch <= _dormerRoofPitchDeg)
        {
            MessageBox.Show(
                "Die Hauptdachneigung muss größer als die Gaubendachneigung sein.",
                "Neigungen passen nicht zusammen",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        _roofPitchDeg = pitch;

        // Bei direkter Eingabe der Dachneigung bleibt die gemessene Front-Wandhöhe bestehen.
        // Tiefe und Wangenlänge ergeben sich aus Hauptdach- und ggf. Gaubendachneigung.
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
                _depthMm = HasShedRoof
                    ? valueMm * Math.Cos(DegToRad(_dormerRoofPitchDeg))
                    : valueMm;
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
        var mainTangent = Math.Tan(DegToRad(_roofPitchDeg));
        var dormerTangent = HasShedRoof
            ? Math.Tan(DegToRad(_dormerRoofPitchDeg))
            : 0.0;

        var tangentDifference = mainTangent - dormerTangent;
        if (tangentDifference <= 0.0001)
            return;

        _depthMm = _frontWallHeightMm / tangentDifference;

        var backRiseMm = mainTangent * _depthMm;
        _slopeLengthMm = Math.Sqrt(
            _depthMm * _depthMm +
            backRiseMm * backRiseMm);
    }

    private void RecalculateFromHeightAndDepth()
    {
        if (_depthMm <= 0)
            return;

        var dormerTangent = HasShedRoof
            ? Math.Tan(DegToRad(_dormerRoofPitchDeg))
            : 0.0;

        _roofPitchDeg = RadToDeg(
            Math.Atan((_frontWallHeightMm / _depthMm) + dormerTangent));

        var backRiseMm = Math.Tan(DegToRad(_roofPitchDeg)) * _depthMm;
        _slopeLengthMm = Math.Sqrt(
            _depthMm * _depthMm +
            backRiseMm * backRiseMm);
    }

    private void RecalculateFromHeightAndSlope()
    {
        if (_slopeLengthMm <= 0)
            return;

        var dormerTangent = HasShedRoof
            ? Math.Tan(DegToRad(_dormerRoofPitchDeg))
            : 0.0;

        if (!HasShedRoof)
        {
            var square = _slopeLengthMm * _slopeLengthMm -
                         _frontWallHeightMm * _frontWallHeightMm;

            if (square <= 0)
                return;

            _depthMm = Math.Sqrt(square);
            _roofPitchDeg = RadToDeg(
                Math.Atan(_frontWallHeightMm / _depthMm));
            return;
        }

        // Schleppdachgaube:
        // s² = d² + (h + tan(beta) * d)²
        var rootTerm =
            (1 + dormerTangent * dormerTangent) *
            _slopeLengthMm * _slopeLengthMm -
            _frontWallHeightMm * _frontWallHeightMm;

        if (rootTerm <= 0)
            return;

        _depthMm =
            (-_frontWallHeightMm * dormerTangent + Math.Sqrt(rootTerm)) /
            (1 + dormerTangent * dormerTangent);

        if (_depthMm <= 0)
            return;

        _roofPitchDeg = RadToDeg(
            Math.Atan(
                (_frontWallHeightMm / _depthMm) +
                dormerTangent));
    }

    private double GetDisplayedDepthMm()
    {
        if (!HasShedRoof)
            return _depthMm;

        var cosine = Math.Cos(DegToRad(_dormerRoofPitchDeg));
        return Math.Abs(cosine) < 0.0001
            ? _depthMm
            : _depthMm / cosine;
    }

    private void RefreshDimensionSummary()
    {
        WidthValueText.Text = FormatCentimeters(_widthMm);
        HeightValueText.Text = FormatCentimeters(_frontWallHeightMm);
        DepthLabel.Text = HasShedRoof ? "Gaubendachkante / Tiefe" : "Gaubentiefe";
        DepthValueText.Text = FormatCentimeters(GetDisplayedDepthMm());
        SlopeValueText.Text = FormatCentimeters(_slopeLengthMm);
        GableValueText.Text = FormatCentimeters(_gableHeightMm);
        PitchValueText.Text = $"{_roofPitchDeg:0.#}°";
        DormerPitchValueText.Text = $"{_dormerRoofPitchDeg:0.#}°";
        RoofPitchBox.Text = _roofPitchDeg.ToString("0.#", CultureInfo.InvariantCulture);
        DormerPitchBox.Text = _dormerRoofPitchDeg.ToString("0.#", CultureInfo.InvariantCulture);
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
                SelectedDimensionTitle.Text = HasShedRoof ? "Gaubendachkante / Tiefe" : "Gaubentiefe";
                SelectedDimensionHint.Text = HasShedRoof
                    ? "Geneigte obere Wangenkante der Schleppdachgaube. Aus Höhe, dieser Kante und 10° Gaubendachneigung wird die Hauptdachneigung berechnet."
                    : "Waagerechte obere Wangenkante. Aus Höhe + Tiefe wird die Dachneigung berechnet.";
                DimensionValueBox.Text = FormatCentimetersInput(GetDisplayedDepthMm());
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
        _dimensionHitSegments.Clear();
        _surfaceKeys.Clear();
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
        var dormerPitchTan = HasShedRoof
            ? Math.Tan(DegToRad(_dormerRoofPitchDeg))
            : 0.0;
        var backRoofY = topY + dormerPitchTan * depth;

        AddMainRoofPlane(width, depth, gable, scale);

        var frontBottomLeft = new Point3D(x0, frontRoofY, zFront);
        var frontBottomRight = new Point3D(x1, frontRoofY, zFront);
        var frontTopLeft = new Point3D(x0, topY, zFront);
        var frontTopRight = new Point3D(x1, topY, zFront);

        var backIntersectionLeft = new Point3D(x0, backRoofY, zBack);
        var backIntersectionRight = new Point3D(x1, backRoofY, zBack);

        // Deckende Wangen: durch die Gaube darf die Hauptdachfläche nicht sichtbar sein.
        AddTriangleSurface(frontBottomLeft, frontTopLeft, backIntersectionLeft, _dormerSideBrush, "left-cheek");
        AddTriangleSurface(frontBottomRight, backIntersectionRight, frontTopRight, _dormerSideBrush, "right-cheek");

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
            // Flachdach- oder Schleppdachgaube: geschlossene Front.
            // Bei der Schleppdachgaube steigt die obere Fläche standardmäßig mit 10° nach hinten an.
            AddQuadSurface(frontBottomLeft, frontBottomRight, frontTopRight, frontTopLeft, _dormerFrontBrush, "front");
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
                _dormerFrontBrush,
                "front");

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

        Dispatcher.BeginInvoke(() =>
        {
            UpdateDimensionOverlay();
            UpdateSurfaceDetail();
        });
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
            var hit = HitTestScene(e.GetPosition(DormerViewport));

            if (hit.DimensionKey is not null)
            {
                SelectDimension(hit.DimensionKey);
            }
            else if (hit.SurfaceKey is not null)
            {
                ShowSurfaceDetail(hit.SurfaceKey);
            }
            else
            {
                HideSurfaceDetail();
            }
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

    private (string? DimensionKey, string? SurfaceKey) HitTestScene(Point point)
    {
        // Kanten werden rein in 2D gegen die projizierte Bildschirmposition geprüft.
        // Dadurch brauchen wir keine großen transparenten 3D-Zylinder mehr,
        // die an den Gaubenecken optisch Durchsicht / Artefakte erzeugen konnten.
        var dimensionKey = HitTestProjectedDimensionEdge(point);

        if (dimensionKey is not null)
            return (dimensionKey, null);

        string? surfaceKey = null;

        VisualTreeHelper.HitTest(
            DormerViewport,
            null,
            result =>
            {
                if (result is RayMeshGeometry3DHitTestResult ray &&
                    ray.ModelHit is GeometryModel3D model &&
                    _surfaceKeys.TryGetValue(model, out var foundSurface))
                {
                    surfaceKey = foundSurface;
                    return HitTestResultBehavior.Stop;
                }

                return HitTestResultBehavior.Continue;
            },
            new PointHitTestParameters(point));

        return (null, surfaceKey);
    }

    private string? HitTestProjectedDimensionEdge(Point clickPoint)
    {
        const double hitTolerancePixels = 9.0;

        string? closestKey = null;
        var closestDistance = double.MaxValue;

        foreach (var segment in _dimensionHitSegments)
        {
            var a = ProjectToViewport(segment.Start);
            var b = ProjectToViewport(segment.End);

            if (a is null || b is null)
                continue;

            var distance = DistancePointToSegment(
                clickPoint,
                a.Value,
                b.Value);

            if (distance <= hitTolerancePixels &&
                distance < closestDistance)
            {
                closestDistance = distance;
                closestKey = segment.Key;
            }
        }

        return closestKey;
    }

    private Point? ProjectToViewport(Point3D point)
    {
        var width = DormerViewport.ActualWidth;
        var height = DormerViewport.ActualHeight;

        if (width <= 1 || height <= 1)
            return null;

        var forward = DormerCamera.LookDirection;
        if (forward.Length < 0.0001)
            return null;
        forward.Normalize();

        var up = DormerCamera.UpDirection;
        if (up.Length < 0.0001)
            return null;
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

        return new Point(
            (nx + 1.0) * 0.5 * width,
            (1.0 - ny) * 0.5 * height);
    }

    private static double DistancePointToSegment(
        Point point,
        Point start,
        Point end)
    {
        var dx = end.X - start.X;
        var dy = end.Y - start.Y;

        var lengthSquared = dx * dx + dy * dy;

        if (lengthSquared < 0.0001)
        {
            var sx = point.X - start.X;
            var sy = point.Y - start.Y;
            return Math.Sqrt(sx * sx + sy * sy);
        }

        var t =
            ((point.X - start.X) * dx +
             (point.Y - start.Y) * dy) /
            lengthSquared;

        t = Math.Clamp(t, 0.0, 1.0);

        var nearestX = start.X + t * dx;
        var nearestY = start.Y + t * dy;

        var px = point.X - nearestX;
        var py = point.Y - nearestY;

        return Math.Sqrt(px * px + py * py);
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

    private void AddTriangleSurface(
        Point3D p1,
        Point3D p2,
        Point3D p3,
        Brush brush,
        string? surfaceKey = null)
    {
        var mesh = new MeshGeometry3D();

        mesh.Positions.Add(p1);
        mesh.Positions.Add(p2);
        mesh.Positions.Add(p3);

        mesh.TriangleIndices.Add(0);
        mesh.TriangleIndices.Add(1);
        mesh.TriangleIndices.Add(2);

        AddSurface(mesh, brush, surfaceKey);
    }

    private void AddQuadSurface(
        Point3D p1,
        Point3D p2,
        Point3D p3,
        Point3D p4,
        Brush brush,
        string? surfaceKey = null)
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

        AddSurface(mesh, brush, surfaceKey);
    }

    private void AddGableFrontSurface(
        Point3D bottomLeft,
        Point3D bottomRight,
        Point3D topRight,
        Point3D apex,
        Point3D topLeft,
        Brush brush,
        string? surfaceKey = null)
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

        AddSurface(mesh, brush, surfaceKey);
    }

    private void AddSurface(
        MeshGeometry3D mesh,
        Brush brush,
        string? surfaceKey = null)
    {
        var material = new DiffuseMaterial(brush);
        var model = new GeometryModel3D(mesh, material)
        {
            BackMaterial = material
        };

        if (!string.IsNullOrWhiteSpace(surfaceKey))
            _surfaceKeys[model] = surfaceKey;

        DormerViewport.Children.Add(new ModelVisual3D
        {
            Content = model
        });
    }

    private void AddTexturedQuadSurface(Point3D p1, Point3D p2, Point3D p3, Point3D p4)
    {
        var bitmap = LoadRoofTextureBitmap();

        if (bitmap is null)
        {
            AddQuadSurface(
                p1, p2, p3, p4,
                new SolidColorBrush(Color.FromRgb(105, 100, 94)));
            return;
        }

        var roofWidth = (p2 - p1).Length;
        var roofSlopeLength = (p4 - p1).Length;

        var sourceAspect =
            bitmap.PixelHeight > 0
                ? (double)bitmap.PixelWidth / bitmap.PixelHeight
                : 1.0;

        var roofAspect =
            roofSlopeLength > 0.0001
                ? roofWidth / roofSlopeLength
                : 1.0;

        // Echte Rasterung der 3D-Fläche:
        // Jede Zelle bekommt die komplette Originaltextur von 0..1.
        // Dadurch wird das Bild nicht mehr über die gesamte Dachfläche hochskaliert.
        const int tilesAlongSlope = 4;

        var tilesAcross = Math.Clamp(
            (int)Math.Round(
                (roofAspect / Math.Max(sourceAspect, 0.01)) *
                tilesAlongSlope),
            2,
            10);

        var mesh = new MeshGeometry3D();

        for (var row = 0; row < tilesAlongSlope; row++)
        {
            var v0 = (double)row / tilesAlongSlope;
            var v1 = (double)(row + 1) / tilesAlongSlope;

            for (var column = 0; column < tilesAcross; column++)
            {
                var u0 = (double)column / tilesAcross;
                var u1 = (double)(column + 1) / tilesAcross;

                var q1 = BilinearPoint(p1, p2, p3, p4, u0, v0);
                var q2 = BilinearPoint(p1, p2, p3, p4, u1, v0);
                var q3 = BilinearPoint(p1, p2, p3, p4, u1, v1);
                var q4 = BilinearPoint(p1, p2, p3, p4, u0, v1);

                var baseIndex = mesh.Positions.Count;

                mesh.Positions.Add(q1);
                mesh.Positions.Add(q2);
                mesh.Positions.Add(q3);
                mesh.Positions.Add(q4);

                // Jede Rasterzelle zeigt das komplette Texturbild.
                mesh.TextureCoordinates.Add(new Point(0, 1));
                mesh.TextureCoordinates.Add(new Point(1, 1));
                mesh.TextureCoordinates.Add(new Point(1, 0));
                mesh.TextureCoordinates.Add(new Point(0, 0));

                mesh.TriangleIndices.Add(baseIndex);
                mesh.TriangleIndices.Add(baseIndex + 1);
                mesh.TriangleIndices.Add(baseIndex + 2);

                mesh.TriangleIndices.Add(baseIndex);
                mesh.TriangleIndices.Add(baseIndex + 2);
                mesh.TriangleIndices.Add(baseIndex + 3);
            }
        }

        var brush = new ImageBrush(bitmap)
        {
            Stretch = Stretch.Fill,
            TileMode = TileMode.None,
            AlignmentX = AlignmentX.Left,
            AlignmentY = AlignmentY.Top
        };
        brush.Freeze();

        var material = new DiffuseMaterial(brush);

        DormerViewport.Children.Add(new ModelVisual3D
        {
            Content = new GeometryModel3D(mesh, material)
            {
                BackMaterial = material
            }
        });
    }

    private static BitmapImage? LoadRoofTextureBitmap()
    {
        try
        {
            var resource = Application.GetResourceStream(
                new Uri("Assets/roof_tiles.jpg", UriKind.Relative));

            if (resource is null)
                return null;

            using var stream = resource.Stream;

            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = stream;
            bitmap.EndInit();

            RenderOptions.SetBitmapScalingMode(
                bitmap,
                BitmapScalingMode.HighQuality);

            bitmap.Freeze();
            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    private static Point3D BilinearPoint(
        Point3D p1,
        Point3D p2,
        Point3D p3,
        Point3D p4,
        double u,
        double v)
    {
        var top =
            new Point3D(
                p1.X + (p2.X - p1.X) * u,
                p1.Y + (p2.Y - p1.Y) * u,
                p1.Z + (p2.Z - p1.Z) * u);

        var bottom =
            new Point3D(
                p4.X + (p3.X - p4.X) * u,
                p4.Y + (p3.Y - p4.Y) * u,
                p4.Z + (p3.Z - p4.Z) * u);

        return new Point3D(
            top.X + (bottom.X - top.X) * v,
            top.Y + (bottom.Y - top.Y) * v,
            top.Z + (bottom.Z - top.Z) * v);
    }

    private void ShowSurfaceDetail(string surfaceKey)
    {
        _selectedSurfaceKey = surfaceKey;
        SurfaceDetailPanel.Visibility = Visibility.Visible;
        UpdateSurfaceDetail();
    }

    private void HideSurfaceDetail()
    {
        _selectedSurfaceKey = null;
        SurfaceDetailPanel.Visibility = Visibility.Collapsed;
        SurfaceDetailCanvas.Children.Clear();
    }

    private void UpdateSurfaceDetail()
    {
        if (_selectedSurfaceKey is null ||
            SurfaceDetailPanel.Visibility != Visibility.Visible)
        {
            return;
        }

        SurfaceDetailCanvas.Children.Clear();

        if (_selectedSurfaceKey == "front")
        {
            DrawFrontDetail();
            return;
        }

        if (_selectedSurfaceKey is "left-cheek" or "right-cheek")
        {
            DrawCheekDetail(_selectedSurfaceKey == "left-cheek");
        }
    }

    private void DrawFrontDetail()
    {
        SurfaceDetailTitle.Text = "Gaubenfront";

        var widthMm = _widthMm;
        var wallHeightMm = _frontWallHeightMm;
        var gableMm = HasGable ? _gableHeightMm : 0;

        var areaM2 =
            (widthMm * wallHeightMm +
             (HasGable ? 0.5 * widthMm * gableMm : 0)) /
            1_000_000.0;

        SurfaceDetailAreaText.Text =
            $"Fläche: {areaM2.ToString("0.00", GermanCulture)} m²";

        const double left = 42;
        const double top = 14;
        const double maxWidth = 182;
        const double maxHeight = 128;

        var totalHeightMm = wallHeightMm + gableMm;
        var scale = Math.Min(
            maxWidth / Math.Max(widthMm, 1),
            maxHeight / Math.Max(totalHeightMm, 1));

        var w = widthMm * scale;
        var wallH = wallHeightMm * scale;
        var gableH = gableMm * scale;

        var bottomY = top + gableH + wallH;
        var wallTopY = top + gableH;

        var points = new PointCollection();

        if (HasGable)
        {
            points.Add(new Point(left, bottomY));
            points.Add(new Point(left + w, bottomY));
            points.Add(new Point(left + w, wallTopY));
            points.Add(new Point(left + w / 2, top));
            points.Add(new Point(left, wallTopY));
        }
        else
        {
            points.Add(new Point(left, bottomY));
            points.Add(new Point(left + w, bottomY));
            points.Add(new Point(left + w, wallTopY));
            points.Add(new Point(left, wallTopY));
        }

        AddDetailPolygon(points);
        AddHorizontalDimension(
            left,
            left + w,
            bottomY + 22,
            FormatCentimeters(widthMm));

        AddVerticalDimension(
            left - 25,
            bottomY,
            wallTopY,
            FormatCentimeters(wallHeightMm));

        if (HasGable)
        {
            AddVerticalDimension(
                left + w + 24,
                wallTopY,
                top,
                FormatCentimeters(gableMm));
        }
    }

    private void DrawCheekDetail(bool isLeft)
    {
        SurfaceDetailTitle.Text =
            isLeft ? "Gaubenwange links" : "Gaubenwange rechts";

        var mainTan = Math.Tan(DegToRad(_roofPitchDeg));
        var backRiseMm = mainTan * _depthMm;

        var areaM2 =
            0.5 * _frontWallHeightMm * _depthMm /
            1_000_000.0;

        SurfaceDetailAreaText.Text =
            $"Fläche: {areaM2.ToString("0.00", GermanCulture)} m²";

        const double left = 46;
        const double top = 14;
        const double maxWidth = 174;
        const double maxHeight = 126;

        var maxVerticalMm = Math.Max(backRiseMm, _frontWallHeightMm);

        var scale = Math.Min(
            maxWidth / Math.Max(_depthMm, 1),
            maxHeight / Math.Max(maxVerticalMm, 1));

        var d = _depthMm * scale;
        var frontH = _frontWallHeightMm * scale;
        var backH = backRiseMm * scale;

        var baseY = top + maxVerticalMm * scale;

        // Linke und rechte Wange werden als echte Gegenstücke dargestellt.
        // Rechts: Frontkante links, Dachanschluss rechts.
        // Links:  Frontkante rechts, Dachanschluss links.
        var frontX = isLeft
            ? left + d
            : left;

        var backX = isLeft
            ? left
            : left + d;

        var frontBottom = new Point(frontX, baseY);
        var frontTop = new Point(frontX, baseY - frontH);
        var backPoint = new Point(backX, baseY - backH);

        AddDetailPolygon(
            new PointCollection
            {
                frontBottom,
                backPoint,
                frontTop
            });

        AddVerticalDimension(
            isLeft ? frontX + 26 : frontX - 26,
            frontBottom.Y,
            frontTop.Y,
            FormatCentimeters(_frontWallHeightMm));

        AddDimensionAlongEdge(
            frontTop,
            backPoint,
            FormatCentimeters(GetDisplayedDepthMm()),
            isLeft ? 15 : -15);

        AddDimensionAlongEdge(
            frontBottom,
            backPoint,
            FormatCentimeters(_slopeLengthMm),
            isLeft ? -17 : 17);
    }

    private void AddDetailPolygon(PointCollection points)
    {
        SurfaceDetailCanvas.Children.Add(
            new Polygon
            {
                Points = points,
                Fill = new SolidColorBrush(Color.FromRgb(231, 236, 240)),
                Stroke = new SolidColorBrush(Color.FromRgb(55, 72, 84)),
                StrokeThickness = 1.4
            });
    }

    private void AddHorizontalDimension(
        double x1,
        double x2,
        double y,
        string text)
    {
        var stroke = new SolidColorBrush(Color.FromRgb(95, 107, 117));

        SurfaceDetailCanvas.Children.Add(
            new Line
            {
                X1 = x1,
                X2 = x2,
                Y1 = y,
                Y2 = y,
                Stroke = stroke,
                StrokeThickness = 1
            });

        AddSmallTick(x1, y - 5, x1, y + 5, stroke);
        AddSmallTick(x2, y - 5, x2, y + 5, stroke);
        AddDetailText(text, (x1 + x2) / 2, y - 16);
    }

    private void AddVerticalDimension(
        double x,
        double y1,
        double y2,
        string text)
    {
        var stroke = new SolidColorBrush(Color.FromRgb(95, 107, 117));

        SurfaceDetailCanvas.Children.Add(
            new Line
            {
                X1 = x,
                X2 = x,
                Y1 = y1,
                Y2 = y2,
                Stroke = stroke,
                StrokeThickness = 1
            });

        AddSmallTick(x - 5, y1, x + 5, y1, stroke);
        AddSmallTick(x - 5, y2, x + 5, y2, stroke);
        AddDetailText(text, x, (y1 + y2) / 2);
    }

    private void AddDimensionAlongEdge(
        Point a,
        Point b,
        string text,
        double normalOffset)
    {
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        var length = Math.Sqrt(dx * dx + dy * dy);

        if (length < 0.001)
            return;

        var nx = -dy / length;
        var ny = dx / length;

        var midX = (a.X + b.X) / 2 + nx * normalOffset;
        var midY = (a.Y + b.Y) / 2 + ny * normalOffset;

        AddDetailText(text, midX, midY);
    }

    private void AddSmallTick(
        double x1,
        double y1,
        double x2,
        double y2,
        Brush stroke)
    {
        SurfaceDetailCanvas.Children.Add(
            new Line
            {
                X1 = x1,
                X2 = x2,
                Y1 = y1,
                Y2 = y2,
                Stroke = stroke,
                StrokeThickness = 1
            });
    }

    private void AddDetailText(string text, double x, double y)
    {
        var label = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(235, 255, 255, 255)),
            Padding = new Thickness(3, 1, 3, 1),
            Child = new TextBlock
            {
                Text = text,
                FontSize = 10.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(52, 65, 75))
            }
        };

        label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

        Canvas.SetLeft(label, x - label.DesiredSize.Width / 2);
        Canvas.SetTop(label, y - label.DesiredSize.Height / 2);

        SurfaceDetailCanvas.Children.Add(label);
    }

    private void AddEdge(Point3D start, Point3D end, string dimensionKey)
    {
        // Schlichte, dünne und vollständig deckende Gaubenkontur.
        // Die Klickfläche wird separat in 2D berechnet und braucht keine
        // transparente 3D-Hilfsgeometrie mehr.
        var visibleModel =
            CreateLineModel(start, end, _normalBrush, 0.0065);

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
        _dimensionHitSegments.Add((start, end, dimensionKey));
    }

    private void AddPassiveEdge(Point3D start, Point3D end, Brush brush, double radius)
    {
        var contourBrush = ReferenceEquals(brush, _normalBrush)
            ? _normalBrush
            : brush;

        DormerViewport.Children.Add(new ModelVisual3D
        {
            Content = CreateLineModel(
                start,
                end,
                contourBrush,
                Math.Min(radius, 0.0065))
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
        DepthKey => FormatCentimeters(GetDisplayedDepthMm()),
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
