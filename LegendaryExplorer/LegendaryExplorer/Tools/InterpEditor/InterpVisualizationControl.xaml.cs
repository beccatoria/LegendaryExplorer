using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Numerics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using LegendaryExplorer.Misc;
using LegendaryExplorer.SharedUI;
using LegendaryExplorer.Tools.LevelEditor;
using LegendaryExplorer.Tools.LevelEditor.Scene3D;
using LegendaryExplorerCore.Helpers;
using LegendaryExplorerCore.Packages;
using LegendaryExplorerCore.Unreal;
using LegendaryExplorerCore.Unreal.BinaryConverters;

namespace LegendaryExplorer.Tools.InterpEditor
{
    public partial class InterpVisualizationControl : NotifyPropertyChangedControlBase
    {
        private const double CanvasPadding = 18d;
        private const double MarkerSize = 8d;
        private const float ContextAxisLengthWorld = 120f;
        private const float CameraAimLengthWorld = 140f;
        private const float CameraFrustumLengthWorld = 220f;
        private const float DefaultCameraFov = 60f;

        private static readonly Brush[] PathBrushes =
        [
            Brushes.DeepSkyBlue,
            Brushes.LimeGreen,
            Brushes.Orange,
            Brushes.MediumOrchid,
            Brushes.Gold,
            Brushes.Cyan
        ];

        private sealed class PathPoint
        {
            public float Time { get; init; }
            public float X { get; init; }
            public float Y { get; init; }
            public float Z { get; init; }
        }

        private sealed class MoveTrackData
        {
            public required InterpTrackMove Track { get; init; }
            public required ExportEntry TrackExport { get; init; }
            public required List<PathPoint> Points { get; init; }
        }

        private sealed class OrientationPoint
        {
            public float Time { get; init; }
            public float Roll { get; init; }
            public float Pitch { get; init; }
            public float Yaw { get; init; }
        }

        private sealed class FloatCurvePoint
        {
            public float Time { get; init; }
            public float Value { get; init; }
        }

        private sealed class MoveTrackPath
        {
            public required ExportEntry TrackExport { get; init; }
            public required List<PathPoint> Points { get; init; }
            public required Ellipse Marker { get; init; }
        }

        private sealed class CameraOverlay
        {
            public required ExportEntry TrackExport { get; init; }
            public required string CameraGroupName { get; init; }
            public required List<PathPoint> PositionPoints { get; init; }
            public required List<OrientationPoint> OrientationPoints { get; init; }
            public required List<FloatCurvePoint> FovPoints { get; init; }
            public required Ellipse CameraRing { get; init; }
            public Line AimLine { get; init; }
            public Polygon FrustumPolygon { get; init; }
        }

        private sealed class ContextOverlayData
        {
            public bool HasAnchorContext { get; set; }
            public bool HasBioStageContext { get; set; }
            public Vector3 AnchorOrWorldPivot { get; set; }
            public string BioStageName { get; set; }
            public Vector3 BioStagePivot { get; set; }
            public float BioStageYawDegrees { get; set; }
            public List<Vector3> BioStageNodePoints { get; init; } = [];
            public List<Vector3> BioStageHullPoints { get; init; } = [];
            public int ExtractedBioStageNodeCount { get; set; }
            public string BioStageNodeSource { get; set; }
        }

        private bool _isInitialized;
        private bool _isProgrammaticScrubUpdate;
        private bool _isPanning;
        private InterpData _interpData;
        private IMEPackage _contextPackage;
        private string _contextPackageSource = "interp package";
        private ExportEntry _selectedExport;
        private float _currentTime;
        private Point _lastPanMousePoint;
        private readonly List<MoveTrackPath> _movePaths = [];
        private readonly List<CameraOverlay> _cameraOverlays = [];
        private ContextOverlayData _contextOverlayData;
        private readonly LevelEditorRenderContext _renderContext = new(readOnly: true);
        private bool _sceneHooksInitialized;
        private bool _needs3DCameraFocus = true;
        private string _lastBioStageNodeSource = "none";

        private float _dataMinA;
        private float _dataMaxA;
        private float _dataMinB;
        private float _dataMaxB;

        private float _viewMinA;
        private float _viewMaxA;
        private float _viewMinB;
        private float _viewMaxB;
        private bool _hasViewport;

        private string _summaryText = "Viewer inactive";
        public string SummaryText
        {
            get => _summaryText;
            set => SetProperty(ref _summaryText, value);
        }

        private string _currentTimeText = "Time: 0.00s";
        public string CurrentTimeText
        {
            get => _currentTimeText;
            set => SetProperty(ref _currentTimeText, value);
        }

        private string _overlayTipText = "Plane XY (X horizontal, Y vertical)";
        public string OverlayTipText
        {
            get => _overlayTipText;
            set => SetProperty(ref _overlayTipText, value);
        }

        private string _contextStatusText = "Context: World fallback";

        public ObservableCollection<string> PlaneOptions { get; } = ["XY", "XZ", "YZ"];

        private string _selectedPlane = "XY";
        public string SelectedPlane
        {
            get => _selectedPlane;
            set
            {
                if (SetProperty(ref _selectedPlane, value))
                {
                    _hasViewport = false;
                    RefreshVisualization();
                }
            }
        }

        private bool _show3DViewer;
        public bool Show3DViewer
        {
            get => _show3DViewer;
            set
            {
                if (SetProperty(ref _show3DViewer, value))
                {
                    if (value)
                    {
                        _needs3DCameraFocus = true;
                    }
                    UpdateViewerMode();
                    RefreshVisualization();
                }
            }
        }

        private bool _showContextOverlays = true;
        public bool ShowContextOverlays
        {
            get => _showContextOverlays;
            set
            {
                if (SetProperty(ref _showContextOverlays, value))
                {
                    RefreshVisualization();
                }
            }
        }

        private bool _showCameraOverlays = true;
        public bool ShowCameraOverlays
        {
            get => _showCameraOverlays;
            set
            {
                if (SetProperty(ref _showCameraOverlays, value))
                {
                    RefreshVisualization();
                }
            }
        }

        private bool _showCameraAim = true;
        public bool ShowCameraAim
        {
            get => _showCameraAim;
            set
            {
                if (SetProperty(ref _showCameraAim, value))
                {
                    RefreshVisualization();
                }
            }
        }

        private bool _showCameraFrustum = true;
        public bool ShowCameraFrustum
        {
            get => _showCameraFrustum;
            set
            {
                if (SetProperty(ref _showCameraFrustum, value))
                {
                    RefreshVisualization();
                }
            }
        }

        private bool _showPaths = true;
        public bool ShowPaths
        {
            get => _showPaths;
            set
            {
                if (SetProperty(ref _showPaths, value))
                {
                    RefreshVisualization();
                }
            }
        }

        private bool _showMarkers = true;
        public bool ShowMarkers
        {
            get => _showMarkers;
            set
            {
                if (SetProperty(ref _showMarkers, value))
                {
                    RefreshVisualization();
                }
            }
        }

        private bool _showKeyframes = true;
        public bool ShowKeyframes
        {
            get => _showKeyframes;
            set
            {
                if (SetProperty(ref _showKeyframes, value))
                {
                    RefreshVisualization();
                }
            }
        }

        private bool _showKeyLabels;
        public bool ShowKeyLabels
        {
            get => _showKeyLabels;
            set
            {
                if (SetProperty(ref _showKeyLabels, value))
                {
                    RefreshVisualization();
                }
            }
        }

        public ICommand FitToViewCommand { get; }
        public ICommand ResetViewCommand { get; }
        public ICommand FocusPath3DCommand { get; }
        public ICommand FocusContext3DCommand { get; }

        private double _scrubValue;
        public double ScrubValue
        {
            get => _scrubValue;
            set
            {
                if (!SetProperty(ref _scrubValue, value))
                {
                    return;
                }

                CurrentTimeText = $"Time: {value:0.00}s";
                if (!_isProgrammaticScrubUpdate)
                {
                    ScrubRequested?.Invoke((float)value);
                }
            }
        }

        private double _scrubMax;
        public double ScrubMax
        {
            get => _scrubMax;
            set => SetProperty(ref _scrubMax, value);
        }

        public event Action<float> ScrubRequested;

        public InterpVisualizationControl()
        {
            FitToViewCommand = new GenericCommand(() =>
            {
                if (Show3DViewer)
                {
                    Focus3DCameraOnBounds(includePath: true, includeContext: ShowContextOverlays);
                    return;
                }

                FitToDataBounds();
                RefreshVisualization();
            });
            ResetViewCommand = new GenericCommand(() =>
            {
                SelectedPlane = "XY";
                ShowPaths = true;
                ShowMarkers = true;
                ShowKeyframes = true;
                ShowKeyLabels = false;
                ShowContextOverlays = true;
                ShowCameraOverlays = true;
                ShowCameraAim = true;
                ShowCameraFrustum = true;
                if (Show3DViewer)
                {
                    Focus3DCameraOnBounds(includePath: true, includeContext: ShowContextOverlays);
                }
                else
                {
                    FitToDataBounds();
                    RefreshVisualization();
                }
            });
            FocusPath3DCommand = new GenericCommand(() =>
            {
                if (!Show3DViewer)
                {
                    return;
                }

                Focus3DCameraOnBounds(includePath: true, includeContext: false);
            });
            FocusContext3DCommand = new GenericCommand(() =>
            {
                if (!Show3DViewer)
                {
                    return;
                }

                Focus3DCameraOnBounds(includePath: false, includeContext: true);
            });

            DataContext = this;
            InitializeComponent();
            Initialize3DViewer();
            PreviewCanvas.SizeChanged += (_, _) =>
            {
                if (_isInitialized && _interpData is not null)
                {
                    RefreshVisualization();
                }
            };
        }

        private void Initialize3DViewer()
        {
            if (_sceneHooksInitialized || SceneViewer is null)
            {
                return;
            }

            SceneViewer.Context = _renderContext;
            _renderContext.Camera.FirstPerson = false;
            _renderContext.UpdateScene += On3DUpdateScene;
            _renderContext.RenderScene += On3DRenderScene;
            _sceneHooksInitialized = true;
            UpdateViewerMode();
        }

        private void UpdateViewerMode()
        {
            if (SceneViewer is null || TwoDPreviewBorder is null)
            {
                return;
            }

            SceneViewer.Visibility = Show3DViewer ? Visibility.Visible : Visibility.Collapsed;
            TwoDPreviewBorder.Visibility = Show3DViewer ? Visibility.Collapsed : Visibility.Visible;
            SceneViewer.SetShouldRender(Show3DViewer);
        }

        public void EnsureInitialized()
        {
            if (_isInitialized)
            {
                return;
            }

            _isInitialized = true;
            SummaryText = "Viewer initialized";
        }

        public void SetInterpData(InterpData interpData)
        {
            _interpData = interpData;
            _needs3DCameraFocus = true;
            RefreshVisualization();
        }

        public void SetContextPackage(IMEPackage contextPackage, string sourceDescription)
        {
            _contextPackage = contextPackage;
            _contextPackageSource = string.IsNullOrWhiteSpace(sourceDescription) ? "interp package" : sourceDescription;
            _needs3DCameraFocus = true;
            RefreshVisualization();
        }

        public void SetSelectedExport(ExportEntry export)
        {
            _selectedExport = export;
            RefreshVisualization();
        }

        public void SetCurrentTime(float time)
        {
            _currentTime = time;
            _isProgrammaticScrubUpdate = true;
            ScrubValue = time;
            _isProgrammaticScrubUpdate = false;
            UpdateMarkers();
            UpdateCameraOverlays();
            UpdateOverlayTipText(_cameraOverlays.Count);
        }

        public void SetDuration(float duration)
        {
            ScrubMax = Math.Max(duration, 0f);
            if (ScrubValue > ScrubMax)
            {
                SetCurrentTime((float)ScrubMax);
            }
        }

        public void Clear()
        {
            _interpData = null;
            _isPanning = false;
            _hasViewport = false;
            PreviewCanvas.Children.Clear();
            _movePaths.Clear();
            _cameraOverlays.Clear();
            _contextOverlayData = null;
            SummaryText = "No InterpData loaded";
            _contextPackage = null;
            _contextPackageSource = "interp package";
            _contextStatusText = "Context: World fallback";
            UpdateOverlayTipText(0);
            CurrentTimeText = "Time: 0.00s";
            ScrubMax = 0;
            _isProgrammaticScrubUpdate = true;
            ScrubValue = 0;
            _isProgrammaticScrubUpdate = false;
        }

        public void RefreshVisualization()
        {
            if (!_isInitialized)
            {
                return;
            }

            PreviewCanvas.Children.Clear();
            _movePaths.Clear();
            _cameraOverlays.Clear();
            _contextOverlayData = null;

            if (_interpData is null)
            {
                SummaryText = "No InterpData loaded";
                _contextStatusText = "Context: World fallback";
                UpdateOverlayTipText(0);
                return;
            }

            int groupCount = _interpData.Groups.Count;
            int trackCount = _interpData.Groups.Sum(g => g.Tracks.Count);

            var moveTrackPoints = CollectMoveTrackPoints();
            if (moveTrackPoints.Count == 0)
            {
                SummaryText = $"Groups: {groupCount} | Tracks: {trackCount} | Move tracks: 0";
                _contextStatusText = "Context: World fallback (no move tracks)";
                UpdateOverlayTipText(0);
                return;
            }

            _contextStatusText = ResolveContextStatus(moveTrackPoints);
            _contextOverlayData = BuildContextOverlayData(moveTrackPoints);

            ComputeDataBounds(moveTrackPoints);
            ExpandBoundsWithContext(_contextOverlayData);
            if (!_hasViewport)
            {
                FitToDataBounds();
            }

            bool hasSelection = _selectedExport is not null;
            int colorIndex = 0;
            int cameraCount = 0;
            foreach (var path in moveTrackPoints)
            {
                bool isSelectedPath = IsSelectedTrack(path.TrackExport);
                double selectedDim = hasSelection && !isSelectedPath ? 0.20 : 1.0;
                var brush = PathBrushes[colorIndex++ % PathBrushes.Length];
                if (ShowPaths)
                {
                    var polyline = new Polyline
                    {
                        Stroke = brush,
                        StrokeThickness = isSelectedPath ? 2 : 1,
                        Opacity = 0.25 * selectedDim
                    };

                    foreach (var p in path.Points)
                    {
                        (float projectedA, float projectedB) = ProjectToPlane(p);
                        polyline.Points.Add(MapToCanvas(projectedA, projectedB));
                    }

                    PreviewCanvas.Children.Add(polyline);
                }

                if (ShowKeyframes || ShowKeyLabels)
                {
                    foreach (var point in path.Points)
                    {
                        (float projectedA, float projectedB) = ProjectToPlane(point);
                        Point keyPoint = MapToCanvas(projectedA, projectedB);

                        if (ShowKeyframes)
                        {
                            var keyEllipse = new Ellipse
                            {
                                Width = 4,
                                Height = 4,
                                Fill = brush,
                                Stroke = Brushes.Black,
                                StrokeThickness = 0.5,
                                Opacity = 0.8 * selectedDim
                            };
                            Canvas.SetLeft(keyEllipse, keyPoint.X - 2);
                            Canvas.SetTop(keyEllipse, keyPoint.Y - 2);
                            PreviewCanvas.Children.Add(keyEllipse);
                        }

                        if (ShowKeyLabels)
                        {
                            var label = new TextBlock
                            {
                                Text = point.Time.ToString("0.00"),
                                Foreground = Brushes.White,
                                FontSize = 9,
                                Opacity = 0.85 * selectedDim
                            };
                            Canvas.SetLeft(label, keyPoint.X + 3);
                            Canvas.SetTop(label, keyPoint.Y - 8);
                            PreviewCanvas.Children.Add(label);
                        }
                    }
                }

                var marker = new Ellipse
                {
                    Width = MarkerSize,
                    Height = MarkerSize,
                    Fill = brush,
                    Stroke = Brushes.Black,
                    StrokeThickness = isSelectedPath ? 1.5 : 1,
                    Opacity = selectedDim,
                    Visibility = ShowMarkers ? Visibility.Visible : Visibility.Hidden
                };
                PreviewCanvas.Children.Add(marker);

                _movePaths.Add(new MoveTrackPath
                {
                    TrackExport = path.TrackExport,
                    Points = path.Points,
                    Marker = marker
                });

                if (ShowCameraOverlays && TryCreateCameraOverlay(path, isSelectedPath, selectedDim, out var cameraOverlay))
                {
                    cameraCount++;
                    _cameraOverlays.Add(cameraOverlay);
                }
            }

            UpdateMarkers();
            if (Show3DViewer)
            {
                Update3DScene();
            }
            else
            {
                RenderContextOverlays();
                UpdateCameraOverlays();
            }
            UpdateOverlayTipText(cameraCount);
            SummaryText = $"Groups: {groupCount} | Tracks: {trackCount} | Move tracks: {_movePaths.Count} | Cameras: {cameraCount} | Mode: {(Show3DViewer ? "3D" : "2D")}";
        }

        private void On3DUpdateScene(object sender, float deltaTime)
        {
            // Render context camera updates internally; scene is regenerated from current scrubbed state.
        }

        private void On3DRenderScene(object sender, EventArgs e)
        {
            if (!Show3DViewer)
            {
                return;
            }

            Update3DScene();
            _renderContext.Primitives.Render(_renderContext);
            _renderContext.DrawUI();
        }

        private void Update3DScene()
        {
            bool hasSelection = _selectedExport is not null;
            foreach (var path in _movePaths)
            {
                bool isSelectedPath = IsSelectedTrack(path.TrackExport);
                float selectedDim = hasSelection && !isSelectedPath ? 0.2f : 1f;
                var baseColor = isSelectedPath
                    ? new Vector4(0.2f, 0.9f, 1f, 1f)
                    : new Vector4(0.55f, 0.55f, 0.6f, 1f);
                var color = ScaleColor(baseColor, selectedDim);
                for (int i = 1; i < path.Points.Count; i++)
                {
                    var prev = path.Points[i - 1];
                    var next = path.Points[i];
                    _renderContext.Primitives.AddLine(new Vector3(prev.X, prev.Y, prev.Z), new Vector3(next.X, next.Y, next.Z), color, 0);
                }

                var current = GetPositionAtTime(path.Points, _currentTime);
                float markerSize = 14f;
                var currentPos = new Vector3(current.X, current.Y, current.Z);
                var markerColor = ScaleColor(new Vector4(1f, 1f, 1f, 1f), selectedDim);
                _renderContext.Primitives.AddLine(currentPos + new Vector3(-markerSize, 0, 0), currentPos + new Vector3(markerSize, 0, 0), markerColor, 0);
                _renderContext.Primitives.AddLine(currentPos + new Vector3(0, -markerSize, 0), currentPos + new Vector3(0, markerSize, 0), markerColor, 0);
                _renderContext.Primitives.AddLine(currentPos + new Vector3(0, 0, -markerSize), currentPos + new Vector3(0, 0, markerSize), markerColor, 0);
            }

            if (_contextOverlayData is not null)
            {
                Add3DContextPrimitives(_contextOverlayData);
            }

            Focus3DCameraIfNeeded();
        }

        private static Vector4 ScaleColor(Vector4 color, float factor)
        {
            factor = Math.Clamp(factor, 0f, 1f);
            return new Vector4(color.X * factor, color.Y * factor, color.Z * factor, color.W);
        }

        private void Add3DContextPrimitives(ContextOverlayData context)
        {
            var origin = context.AnchorOrWorldPivot;
            _renderContext.Primitives.AddLine(origin, origin + new Vector3(ContextAxisLengthWorld, 0, 0), new Vector4(0.35f, 0.65f, 1f, 1f), 0);
            _renderContext.Primitives.AddLine(origin, origin + new Vector3(0, ContextAxisLengthWorld, 0), new Vector4(0.3f, 0.85f, 0.4f, 1f), 0);
            _renderContext.Primitives.AddLine(origin, origin + new Vector3(0, 0, ContextAxisLengthWorld), new Vector4(1f, 0.5f, 0.25f, 1f), 0);

            foreach (var node in context.BioStageNodePoints)
            {
                float s = 10f;
                _renderContext.Primitives.AddLine(node + new Vector3(-s, 0, 0), node + new Vector3(s, 0, 0), new Vector4(1f, 1f, 0.2f, 1f), 0);
                _renderContext.Primitives.AddLine(node + new Vector3(0, -s, 0), node + new Vector3(0, s, 0), new Vector4(1f, 1f, 0.2f, 1f), 0);
                _renderContext.Primitives.AddLine(node + new Vector3(0, 0, -s), node + new Vector3(0, 0, s), new Vector4(1f, 1f, 0.2f, 1f), 0);
            }

            bool drawSequentialNodeLinks = !string.Equals(context.BioStageNodeSource, "skeletalmesh-bones", StringComparison.OrdinalIgnoreCase);
            if (drawSequentialNodeLinks && context.BioStageNodePoints.Count >= 2)
            {
                for (int i = 1; i < context.BioStageNodePoints.Count; i++)
                {
                    var a = context.BioStageNodePoints[i - 1];
                    var b = context.BioStageNodePoints[i];
                    _renderContext.Primitives.AddLine(a, b, new Vector4(0.95f, 0.9f, 0.5f, 1f), 0);
                }

                if (context.BioStageNodePoints.Count >= 3)
                {
                    var first = context.BioStageNodePoints[0];
                    var last = context.BioStageNodePoints[^1];
                    _renderContext.Primitives.AddLine(last, first, new Vector4(0.95f, 0.9f, 0.5f, 1f), 0);
                }
            }

            if (context.BioStageHullPoints.Count >= 3)
            {
                var hullFill = _renderContext.Primitives.BuildMesh(new Vector4(0.95f, 0.9f, 0.5f, 0.22f), 0, Matrix4x4.Identity);
                foreach (var point in context.BioStageHullPoints)
                {
                    hullFill.AddVertex(point);
                }

                for (int i = 1; i < context.BioStageHullPoints.Count - 1; i++)
                {
                    hullFill.AddTriangle(0, i, i + 1);
                    hullFill.AddTriangle(0, i + 1, i);
                }

                for (int i = 0; i < context.BioStageHullPoints.Count; i++)
                {
                    var a = context.BioStageHullPoints[i];
                    var b = context.BioStageHullPoints[(i + 1) % context.BioStageHullPoints.Count];
                    _renderContext.Primitives.AddLine(a, b, new Vector4(0.95f, 0.9f, 0.5f, 1f), 0);
                }
            }
        }

        private void Focus3DCameraIfNeeded()
        {
            if (!_needs3DCameraFocus || _movePaths.Count == 0)
            {
                return;
            }

            Focus3DCameraOnBounds(includePath: true, includeContext: ShowContextOverlays);
            _needs3DCameraFocus = false;
        }

        private void Focus3DCameraOnBounds(bool includePath, bool includeContext)
        {
            if (_renderContext is null)
            {
                return;
            }

            var allPoints = _movePaths.SelectMany(p => p.Points)
                .Select(p => new Vector3(p.X, p.Y, p.Z))
                .ToList();

            if (!includePath)
            {
                allPoints.Clear();
            }

            if (includeContext && _contextOverlayData is not null)
            {
                allPoints.Add(_contextOverlayData.AnchorOrWorldPivot);
                if (_contextOverlayData.HasBioStageContext)
                {
                    allPoints.Add(_contextOverlayData.BioStagePivot);
                    allPoints.AddRange(_contextOverlayData.BioStageNodePoints);
                    allPoints.AddRange(_contextOverlayData.BioStageHullPoints);
                }
            }

            if (allPoints.Count == 0)
            {
                return;
            }

            float minX = allPoints.Min(p => p.X);
            float minY = allPoints.Min(p => p.Y);
            float minZ = allPoints.Min(p => p.Z);
            float maxX = allPoints.Max(p => p.X);
            float maxY = allPoints.Max(p => p.Y);
            float maxZ = allPoints.Max(p => p.Z);
            var center = new Vector3((minX + maxX) * 0.5f, (minY + maxY) * 0.5f, (minZ + maxZ) * 0.5f);
            var extents = new Vector3(Math.Abs(maxX - minX), Math.Abs(maxY - minY), Math.Abs(maxZ - minZ));
            float minRadius = includePath ? 120f : 12f;
            float radius = Math.Max(minRadius, Math.Max(extents.X, Math.Max(extents.Y, extents.Z)) * 1.1f);
            _renderContext.Camera.Position = center + new Vector3(radius, -radius, radius * 0.55f);
            _renderContext.Camera.OrientTowards(center);
            _renderContext.Camera.FocusDepth = radius;
            _needs3DCameraFocus = false;
        }

        private void RenderContextOverlays()
        {
            if (!ShowContextOverlays || _contextOverlayData is null)
            {
                return;
            }

            DrawContextAxes(_contextOverlayData.AnchorOrWorldPivot, Brushes.CornflowerBlue, Brushes.MediumSeaGreen);

            if (_contextOverlayData.HasBioStageContext)
            {
                DrawBioStageOverlay(_contextOverlayData);
            }

            DrawContextLegend();
        }

        private void DrawContextAxes(Vector3 origin, Brush axisABrush, Brush axisBBrush)
        {
            var point = new PathPoint { X = origin.X, Y = origin.Y, Z = origin.Z };
            (float originA, float originB) = ProjectToPlane(point);

            string axisALabel;
            string axisBLabel;

            Vector3 axisAVector = SelectedPlane switch
            {
                "XZ" => new(ContextAxisLengthWorld, 0, 0),
                "YZ" => new(0, ContextAxisLengthWorld, 0),
                _ => new(ContextAxisLengthWorld, 0, 0)
            };
            Vector3 axisBVector = SelectedPlane switch
            {
                "XZ" => new(0, 0, ContextAxisLengthWorld),
                "YZ" => new(0, 0, ContextAxisLengthWorld),
                _ => new(0, ContextAxisLengthWorld, 0)
            };

            (axisALabel, axisBLabel) = SelectedPlane switch
            {
                "XZ" => ("X", "Z"),
                "YZ" => ("Y", "Z"),
                _ => ("X", "Y")
            };

            var axisAEnd = origin + axisAVector;
            var axisBEnd = origin + axisBVector;

            (float axisAEndA, float axisAEndB) = ProjectToPlane(new PathPoint { X = axisAEnd.X, Y = axisAEnd.Y, Z = axisAEnd.Z });
            (float axisBEndA, float axisBEndB) = ProjectToPlane(new PathPoint { X = axisBEnd.X, Y = axisBEnd.Y, Z = axisBEnd.Z });

            Point originCanvas = MapToCanvas(originA, originB);
            Point axisACanvas = MapToCanvas(axisAEndA, axisAEndB);
            Point axisBCanvas = MapToCanvas(axisBEndA, axisBEndB);

            PreviewCanvas.Children.Add(new Line
            {
                X1 = originCanvas.X,
                Y1 = originCanvas.Y,
                X2 = axisACanvas.X,
                Y2 = axisACanvas.Y,
                Stroke = axisABrush,
                StrokeThickness = 1.5,
                Opacity = 0.75
            });
            PreviewCanvas.Children.Add(new Line
            {
                X1 = originCanvas.X,
                Y1 = originCanvas.Y,
                X2 = axisBCanvas.X,
                Y2 = axisBCanvas.Y,
                Stroke = axisBBrush,
                StrokeThickness = 1.5,
                Opacity = 0.75
            });

            var originMarker = new Ellipse
            {
                Width = 6,
                Height = 6,
                Fill = Brushes.White,
                Stroke = Brushes.Black,
                StrokeThickness = 1,
                Opacity = 0.9
            };
            Canvas.SetLeft(originMarker, originCanvas.X - 3);
            Canvas.SetTop(originMarker, originCanvas.Y - 3);
            PreviewCanvas.Children.Add(originMarker);

            var axisAText = new TextBlock
            {
                Text = axisALabel,
                Foreground = axisABrush,
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                Opacity = 0.9
            };
            Canvas.SetLeft(axisAText, axisACanvas.X + 3);
            Canvas.SetTop(axisAText, axisACanvas.Y - 8);
            PreviewCanvas.Children.Add(axisAText);

            var axisBText = new TextBlock
            {
                Text = axisBLabel,
                Foreground = axisBBrush,
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                Opacity = 0.9
            };
            Canvas.SetLeft(axisBText, axisBCanvas.X + 3);
            Canvas.SetTop(axisBText, axisBCanvas.Y - 8);
            PreviewCanvas.Children.Add(axisBText);
        }

        private void DrawContextLegend()
        {
            string axisAName;
            string axisBName;
            (axisAName, axisBName) = SelectedPlane switch
            {
                "XZ" => ("X", "Z"),
                "YZ" => ("Y", "Z"),
                _ => ("X", "Y")
            };

            var legend = new TextBlock
            {
                Text = $"Legend\nBlue={axisAName} axis  Green={axisBName} axis\nWhite dot=Context pivot  Khaki=BioStage hull  Yellow=BioStage node",
                Foreground = Brushes.Gainsboro,
                FontSize = 10,
                Opacity = 0.85,
                Background = new SolidColorBrush(Color.FromArgb(120, 16, 16, 16)),
                Padding = new Thickness(4, 2, 4, 2),
                IsHitTestVisible = false
            };

            double left = Math.Max(8, PreviewCanvas.ActualWidth - 300);
            Canvas.SetLeft(legend, left);
            Canvas.SetTop(legend, 8);
            PreviewCanvas.Children.Add(legend);
        }

        private void DrawBioStageOverlay(ContextOverlayData context)
        {
            if (context.BioStageHullPoints.Count >= 3)
            {
                var hull = new Polygon
                {
                    Stroke = Brushes.Khaki,
                    Fill = new SolidColorBrush(Color.FromArgb(40, 255, 235, 140)),
                    StrokeThickness = 1.0,
                    Opacity = 0.65
                };
                foreach (var hullPoint in context.BioStageHullPoints)
                {
                    (float a, float b) = ProjectToPlane(new PathPoint { X = hullPoint.X, Y = hullPoint.Y, Z = hullPoint.Z });
                    hull.Points.Add(MapToCanvas(a, b));
                }
                PreviewCanvas.Children.Add(hull);
            }

            foreach (var node in context.BioStageNodePoints)
            {
                (float a, float b) = ProjectToPlane(new PathPoint { X = node.X, Y = node.Y, Z = node.Z });
                Point p = MapToCanvas(a, b);
                var nodeMarker = new Ellipse
                {
                    Width = 4,
                    Height = 4,
                    Fill = Brushes.Yellow,
                    Stroke = Brushes.Black,
                    StrokeThickness = 0.5,
                    Opacity = 0.8
                };
                Canvas.SetLeft(nodeMarker, p.X - 2);
                Canvas.SetTop(nodeMarker, p.Y - 2);
                PreviewCanvas.Children.Add(nodeMarker);
            }
        }

        private void UpdateOverlayTipText(int cameraCount)
        {
            if (Show3DViewer)
            {
                if (!ShowCameraOverlays || cameraCount <= 0)
                {
                    string cameraState = !ShowCameraOverlays ? "Camera overlays off" : "No camera tracks detected";
                    string contextLegend = ShowContextOverlays
                        ? (_contextOverlayData?.HasBioStageContext == true
                            ? "Legend: cyan=move path, white cross=current time, yellow=BioStage nodes/links, RGB axes=context"
                            : "Legend: cyan=move path, white cross=current time, RGB axes=context")
                        : "Legend: cyan=move path, white cross=current time";
                    string contextDebug = _contextOverlayData?.HasBioStageContext == true
                        ? $"BioStage debug: {_contextOverlayData.BioStageName} | nodes={_contextOverlayData.ExtractedBioStageNodeCount} | hull={_contextOverlayData.BioStageHullPoints.Count} | src={_contextOverlayData.BioStageNodeSource ?? "none"}"
                        : "BioStage debug: n/a";
                    OverlayTipText = $"3D mode\n{cameraState}\n{_contextStatusText}\n{contextLegend}\n{contextDebug}";
                    return;
                }

                CameraOverlay overlay3D = ResolveReadoutCameraOverlay(out string source3D);
                var orientation3D = GetOrientationAtTime(overlay3D.OrientationPoints, _currentTime);
                float fov3D = GetFloatAtTime(overlay3D.FovPoints, _currentTime, DefaultCameraFov);
                string orientationText3D = orientation3D is null
                    ? "Aim: n/a"
                    : $"Yaw {orientation3D.Yaw:0.0}°  Pitch {orientation3D.Pitch:0.0}°";
                string groupName3D = string.IsNullOrWhiteSpace(overlay3D.CameraGroupName) ? "(unnamed)" : overlay3D.CameraGroupName;
                string contextLegend3D = ShowContextOverlays
                    ? (_contextOverlayData?.HasBioStageContext == true
                        ? "Legend: cyan=move path, white cross=current time, yellow=BioStage nodes/links, RGB axes=context"
                        : "Legend: cyan=move path, white cross=current time, RGB axes=context")
                    : "Legend: cyan=move path, white cross=current time";

                string contextDebug3D = _contextOverlayData?.HasBioStageContext == true
                    ? $"BioStage debug: {_contextOverlayData.BioStageName} | nodes={_contextOverlayData.ExtractedBioStageNodeCount} | hull={_contextOverlayData.BioStageHullPoints.Count} | src={_contextOverlayData.BioStageNodeSource ?? "none"}"
                    : "BioStage debug: n/a";
                OverlayTipText = $"3D mode\n{orientationText3D}  FOV {fov3D:0.0}°  [{source3D}: {groupName3D}] ({cameraCount} cam)\n{_contextStatusText}\n{contextLegend3D}\n{contextDebug3D}";
                return;
            }

            string planeGuide = SelectedPlane switch
            {
                "XZ" => "Plane XZ (X horizontal, Z vertical)",
                "YZ" => "Plane YZ (Y horizontal, Z vertical)",
                _ => "Plane XY (X horizontal, Y vertical)"
            };

            if (!ShowCameraOverlays)
            {
                OverlayTipText = $"{planeGuide}\nCamera overlays off\n{_contextStatusText}";
                return;
            }

            if (cameraCount <= 0)
            {
                OverlayTipText = $"{planeGuide}\nNo camera tracks detected\n{_contextStatusText}";
                return;
            }

            CameraOverlay overlay = ResolveReadoutCameraOverlay(out string source);
            var orientation = GetOrientationAtTime(overlay.OrientationPoints, _currentTime);
            float fov = GetFloatAtTime(overlay.FovPoints, _currentTime, DefaultCameraFov);
            string orientationText = orientation is null
                ? "Aim: n/a"
                : $"Yaw {orientation.Yaw:0.0}°  Pitch {orientation.Pitch:0.0}°";
            string groupName = string.IsNullOrWhiteSpace(overlay.CameraGroupName) ? "(unnamed)" : overlay.CameraGroupName;
            OverlayTipText = $"{planeGuide}\n{orientationText}  FOV {fov:0.0}°  [{source}: {groupName}] ({cameraCount} cam)\n{_contextStatusText}";
        }

        private CameraOverlay ResolveReadoutCameraOverlay(out string source)
        {
            string activeCameraGroup = GetActiveCameraGroupAtTime();
            if (!string.IsNullOrWhiteSpace(activeCameraGroup))
            {
                var activeOverlay = _cameraOverlays.FirstOrDefault(overlay =>
                    overlay.CameraGroupName.Equals(activeCameraGroup, StringComparison.OrdinalIgnoreCase));
                if (activeOverlay is not null)
                {
                    source = "active";
                    return activeOverlay;
                }
            }

            if (_selectedExport is not null)
            {
                var selectedOverlay = _cameraOverlays.FirstOrDefault(overlay => IsSelectedTrack(overlay.TrackExport));
                if (selectedOverlay is not null)
                {
                    source = "selected";
                    return selectedOverlay;
                }
            }

            source = "default";
            return _cameraOverlays[0];
        }

        private string GetActiveCameraGroupAtTime()
        {
            if (_interpData is null)
            {
                return null;
            }

            string activeGroup = null;
            float activeSwitchTime = float.MinValue;

            foreach (var directorTrack in _interpData.Groups.SelectMany(g => g.Tracks).OfType<InterpTrackDirector>())
            {
                var cutTrack = directorTrack.Export.GetProperties().GetProp<ArrayProperty<StructProperty>>("CutTrack");
                if (cutTrack is null)
                {
                    continue;
                }

                foreach (var cutKey in cutTrack)
                {
                    float switchTime = cutKey.GetProp<FloatProperty>("Time")?.Value ?? 0f;
                    if (switchTime > _currentTime || switchTime < activeSwitchTime)
                    {
                        continue;
                    }

                    string targetGroup = cutKey.GetProp<NameProperty>("TargetCamGroup")?.Value.Instanced;
                    if (string.IsNullOrWhiteSpace(targetGroup))
                    {
                        continue;
                    }

                    activeSwitchTime = switchTime;
                    activeGroup = targetGroup;
                }
            }

            return activeGroup;
        }

        private string ResolveContextStatus(List<MoveTrackData> moveTrackPoints)
        {
            int anchorRelativeCount = 0;
            int taggedAnchorCount = 0;

            foreach (var moveTrack in moveTrackPoints)
            {
                var groupProps = moveTrack.Track.Group?.Export?.GetProperties();
                string moveFrame = moveTrack.Track.Export.GetProperty<EnumProperty>("MoveFrame")?.Value;
                if (string.Equals(moveFrame, "IMF_AnchorObject", StringComparison.OrdinalIgnoreCase))
                {
                    anchorRelativeCount++;
                }

                if (groupProps?.GetProp<NameProperty>("m_nmSFXFindActor")?.Value is NameReference tag && tag != NameReference.None)
                {
                    taggedAnchorCount++;
                }
            }

            if (anchorRelativeCount > 0)
            {
                if (taggedAnchorCount > 0)
                {
                    return $"Context: AnchorObject ({anchorRelativeCount} anchor-relative tracks, {taggedAnchorCount} tagged group actors)";
                }

                return $"Context: AnchorObject ({anchorRelativeCount} anchor-relative tracks)";
            }

            if (TryGetBioStagePivot(out Vector3 pivot, out string stageName))
            {
                return $"Context: BioStage ({stageName} @ {pivot.X:0.#}, {pivot.Y:0.#}, {pivot.Z:0.#}) [{_contextPackageSource}]";
            }

            return $"Context: World origin fallback (no anchor/BioStage context) [{_contextPackageSource}]";
        }

        private ContextOverlayData BuildContextOverlayData(List<MoveTrackData> moveTrackPoints)
        {
            var data = new ContextOverlayData();

            int anchorRelativeCount = moveTrackPoints.Count(moveTrack =>
                string.Equals(moveTrack.Track.Export.GetProperty<EnumProperty>("MoveFrame")?.Value, "IMF_AnchorObject", StringComparison.OrdinalIgnoreCase));

            data.HasAnchorContext = anchorRelativeCount > 0;
            data.AnchorOrWorldPivot = Vector3.Zero;

            if (TryGetBioStageContext(out string stageName, out Vector3 stagePivot, out float stageYawDegrees, out List<Vector3> stageNodes))
            {
                data.HasBioStageContext = true;
                data.BioStageName = stageName;
                data.BioStagePivot = stagePivot;
                data.BioStageYawDegrees = stageYawDegrees;
                var resolvedNodes = ResolveBioStageNodeSpace(stageNodes, stagePivot, moveTrackPoints);
                if (string.Equals(_lastBioStageNodeSource, "skeletalmesh-bones", StringComparison.OrdinalIgnoreCase))
                {
                    resolvedNodes = ReduceMeshDerivedNodeClutter(resolvedNodes, stagePivot);
                }
                data.BioStageNodePoints.AddRange(resolvedNodes);
                data.ExtractedBioStageNodeCount = resolvedNodes.Count;
                data.BioStageNodeSource = _lastBioStageNodeSource;
                data.AnchorOrWorldPivot = stagePivot;
                data.BioStageHullPoints.AddRange(BuildApproximateHullFromNodes(resolvedNodes, stagePivot, stageYawDegrees));
            }

            return data;
        }

        private static IEnumerable<Vector3> BuildApproximateHullFromNodes(List<Vector3> nodes, Vector3 pivot, float yawDegrees)
        {
            if (nodes.Count < 3)
            {
                return [];
            }

            var hull = ComputeConvexHull2D(nodes.Select(n => (n.X, n.Y)).ToList());
            return hull.Select(p => new Vector3(p.X, p.Y, pivot.Z)).ToList();
        }

        private static List<Vector3> ResolveBioStageNodeSpace(List<Vector3> rawNodes, Vector3 stagePivot, List<MoveTrackData> moveTrackPoints)
        {
            if (rawNodes.Count == 0)
            {
                return [];
            }

            var pathPoints = moveTrackPoints.SelectMany(m => m.Points).ToList();
            var target = pathPoints.Count > 0
                ? new Vector3(pathPoints.Average(p => p.X), pathPoints.Average(p => p.Y), pathPoints.Average(p => p.Z))
                : stagePivot;

            var asWorld = rawNodes;
            var plusPivot = rawNodes.Select(n => n + stagePivot).ToList();

            static float Score(List<Vector3> nodes, Vector3 t)
            {
                if (nodes.Count == 0)
                {
                    return float.MaxValue;
                }

                return nodes.Average(n => Vector3.DistanceSquared(n, t));
            }

            return Score(plusPivot, target) < Score(asWorld, target)
                ? plusPivot
                : asWorld.ToList();
        }

        private static List<Vector3> ReduceMeshDerivedNodeClutter(List<Vector3> nodes, Vector3 stagePivot)
        {
            if (nodes.Count <= 8)
            {
                return nodes;
            }

            var hull = ComputeConvexHull2D(nodes.Select(n => (n.X, n.Y)).ToList());
            if (hull.Count >= 3)
            {
                float z = nodes.Average(n => n.Z);
                return hull.Select(p => new Vector3(p.X, p.Y, z)).ToList();
            }

            return nodes.Where((_, idx) => idx % Math.Max(1, nodes.Count / 8) == 0).ToList();
        }

        private static List<(float X, float Y)> ComputeConvexHull2D(List<(float X, float Y)> points)
        {
            if (points.Count <= 3)
            {
                return points.Distinct().ToList();
            }

            var sorted = points.Distinct().OrderBy(p => p.X).ThenBy(p => p.Y).ToList();
            var lower = new List<(float X, float Y)>();
            foreach (var p in sorted)
            {
                while (lower.Count >= 2 && Cross(lower[^2], lower[^1], p) <= 0f)
                {
                    lower.RemoveAt(lower.Count - 1);
                }
                lower.Add(p);
            }

            var upper = new List<(float X, float Y)>();
            for (int i = sorted.Count - 1; i >= 0; i--)
            {
                var p = sorted[i];
                while (upper.Count >= 2 && Cross(upper[^2], upper[^1], p) <= 0f)
                {
                    upper.RemoveAt(upper.Count - 1);
                }
                upper.Add(p);
            }

            lower.RemoveAt(lower.Count - 1);
            upper.RemoveAt(upper.Count - 1);
            lower.AddRange(upper);
            return lower;
        }

        private static float Cross((float X, float Y) o, (float X, float Y) a, (float X, float Y) b)
        {
            return (a.X - o.X) * (b.Y - o.Y) - (a.Y - o.Y) * (b.X - o.X);
        }

        private bool TryGetBioStagePivot(out Vector3 pivot, out string stageName)
        {
            pivot = Vector3.Zero;
            stageName = string.Empty;

            var sourcePackage = _contextPackage ?? _interpData?.Export?.FileRef;
            var exports = sourcePackage?.Exports;
            if (exports is null)
            {
                return false;
            }

            foreach (var export in exports)
            {
                if (!string.Equals(export.ClassName, "BioStage", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var location = export.GetProperty<StructProperty>("Location") ?? export.GetProperty<StructProperty>("location");
                if (location is null)
                {
                    continue;
                }

                pivot = CommonStructs.GetVector3(location);
                stageName = export.ObjectName.Instanced;
                return true;
            }

            return false;
        }

        private bool TryGetBioStageContext(out string stageName, out Vector3 pivot, out float yawDegrees, out List<Vector3> nodePoints)
        {
            stageName = string.Empty;
            pivot = Vector3.Zero;
            yawDegrees = 0f;
            nodePoints = [];

            var sourcePackage = _contextPackage ?? _interpData?.Export?.FileRef;
            var exports = sourcePackage?.Exports;
            if (exports is null)
            {
                return false;
            }

            var movePathCenter = GetMovePathCenter();
            _lastBioStageNodeSource = "none";
            ExportEntry bestStage = null;
            float bestScore = float.MaxValue;
            int bestNodeCount = -1;
            float bestYawDegrees = 0f;
            Vector3 bestPivot = Vector3.Zero;
            List<Vector3> bestNodes = [];

            foreach (var export in exports)
            {
                if (!string.Equals(export.ClassName, "BioStage", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                var location = export.GetProperty<StructProperty>("Location") ?? export.GetProperty<StructProperty>("location");
                if (location is null)
                {
                    continue;
                }

                var stagePivot = CommonStructs.GetVector3(location);
                float score = Vector3.DistanceSquared(stagePivot, movePathCenter);

                float stageYawDegrees = 0f;
                var stageRotation = export.GetProperty<StructProperty>("Rotation") ?? export.GetProperty<StructProperty>("rotation");
                if (stageRotation is not null)
                {
                    int stageYawUnreal = stageRotation.GetProp<IntProperty>("Yaw")?.Value
                                        ?? (int)(stageRotation.GetProp<FloatProperty>("Yaw")?.Value ?? 0f);
                    stageYawDegrees = stageYawUnreal.UnrealRotationUnitsToDegrees();
                }

                var stageNodes = new List<Vector3>();
                TryCollectBioStageCameraNodes(export, stagePivot, stageNodes);
                int nodeCount = stageNodes.Count;

                bool isBetter = nodeCount > bestNodeCount
                    || (nodeCount == bestNodeCount && score < bestScore);

                if (isBetter)
                {
                    bestScore = score;
                    bestStage = export;
                    bestNodeCount = nodeCount;
                    bestYawDegrees = stageYawDegrees;
                    bestPivot = stagePivot;
                    bestNodes = stageNodes;
                }
            }

            if (bestStage is null)
            {
                return false;
            }

            stageName = bestStage.ObjectName.Instanced;
            pivot = bestPivot;
            yawDegrees = bestYawDegrees;
            nodePoints.AddRange(bestNodes);
            return true;
        }

        private Vector3 GetMovePathCenter()
        {
            var movePoints = _interpData?.Groups?
                .SelectMany(g => g.Tracks)
                .OfType<InterpTrackMove>()
                .SelectMany(track =>
                {
                    var props = track.Export.GetProperties();
                    var posPoints = props.GetProp<StructProperty>("PosTrack")?
                        .GetProp<ArrayProperty<StructProperty>>("Points");
                    if (posPoints is null)
                    {
                        return Enumerable.Empty<Vector3>();
                    }

                    return posPoints
                        .Select(point => point.GetProp<StructProperty>("OutVal"))
                        .Where(outVal => outVal is not null)
                        .Select(CommonStructs.GetVector3);
                })
                .ToList();

            if (movePoints is null || movePoints.Count == 0)
            {
                return Vector3.Zero;
            }

            return new Vector3(
                movePoints.Average(p => p.X),
                movePoints.Average(p => p.Y),
                movePoints.Average(p => p.Z));
        }

        private void TryCollectBioStageCameraNodes(ExportEntry bioStageExport, Vector3 stagePivot, List<Vector3> nodePoints)
        {
            nodePoints.Clear();

            try
            {
                var bioStageBinary = bioStageExport.GetBinaryData<BioStage>();
                if (bioStageBinary?.CameraList is null)
                {
                    // fall through to non-binary property path
                }

                if (bioStageBinary?.CameraList is not null)
                {
                    foreach ((NameReference _, PropertyCollection cameraProps) in bioStageBinary.CameraList)
                    {
                        if (TryExtractBioStageNodePosition(cameraProps, out var nodePosition))
                        {
                            nodePoints.Add(nodePosition);
                        }
                    }

                    if (nodePoints.Count > 0)
                    {
                        _lastBioStageNodeSource = "binary-camera-list";
                    }
                }

                if (nodePoints.Count == 0)
                {
                    TryCollectBioStageNodesFromProperties(bioStageExport, nodePoints);
                    if (nodePoints.Count > 0)
                    {
                        _lastBioStageNodeSource = "property-camera-list";
                    }
                }

                if (nodePoints.Count == 0)
                {
                    TryCollectBioStageNodesFromSkeletalMeshBones(bioStageExport, nodePoints);
                    if (nodePoints.Count > 0)
                    {
                        _lastBioStageNodeSource = "skeletalmesh-bones";
                    }
                }
            }
            catch
            {
                // best-effort context extraction
                if (nodePoints.Count == 0)
                {
                    TryCollectBioStageNodesFromProperties(bioStageExport, nodePoints);
                    if (nodePoints.Count > 0)
                    {
                        _lastBioStageNodeSource = "property-camera-list";
                    }
                }

                if (nodePoints.Count == 0)
                {
                    TryCollectBioStageNodesFromSkeletalMeshBones(bioStageExport, nodePoints);
                    if (nodePoints.Count > 0)
                    {
                        _lastBioStageNodeSource = "skeletalmesh-bones";
                    }
                }
            }
        }

        private static void TryCollectBioStageNodesFromSkeletalMeshBones(ExportEntry bioStageExport, List<Vector3> nodePoints)
        {
            ExportEntry skeletalMeshComponent = ResolveBioStageSkeletalMeshComponent(bioStageExport);
            if (skeletalMeshComponent is null)
            {
                return;
            }

            ExportEntry skeletalMeshExport = skeletalMeshComponent.GetProperty<ObjectProperty>("SkeletalMesh")?.ResolveToEntry(bioStageExport.FileRef) as ExportEntry;
            if (skeletalMeshExport is null)
            {
                return;
            }

            SkeletalMesh skelMesh;
            try
            {
                skelMesh = skeletalMeshExport.GetBinaryData<SkeletalMesh>();
            }
            catch
            {
                return;
            }

            if (skelMesh?.RefSkeleton is null || skelMesh.RefSkeleton.Length == 0)
            {
                return;
            }

            var stageTransform = BuildTransformFromProperties(bioStageExport.GetProperties(), defaultScale: Vector3.One);
            var componentTransform = BuildTransformFromProperties(skeletalMeshComponent.GetProperties(), defaultScale: Vector3.One);
            var localToWorld = componentTransform * stageTransform;

            var worldBonePositions = BuildApproximateWorldBonePositions(skelMesh.RefSkeleton);
            var filtered = new List<Vector3>();
            for (int i = 0; i < skelMesh.RefSkeleton.Length; i++)
            {
                string boneName = skelMesh.RefSkeleton[i].Name.Instanced;
                if (IsLikelyBioStageNodeBoneName(boneName))
                {
                    filtered.Add(Vector3.Transform(worldBonePositions[i], localToWorld));
                }
            }

            if (filtered.Count > 24)
            {
                // If a mesh naming scheme still over-matches, keep a sparse subset so the view remains readable.
                int step = (int)Math.Ceiling(filtered.Count / 24.0);
                filtered = filtered.Where((_, idx) => idx % step == 0).ToList();
            }

            if (filtered.Count == 0)
            {
                var bounds = skelMesh.Bounds;
                Vector3 c = Vector3.Transform(bounds.Origin, localToWorld);
                Vector3 e = bounds.BoxExtent;
                filtered.Add(c + new Vector3(-e.X, -e.Y, 0));
                filtered.Add(c + new Vector3(e.X, -e.Y, 0));
                filtered.Add(c + new Vector3(e.X, e.Y, 0));
                filtered.Add(c + new Vector3(-e.X, e.Y, 0));
            }

            nodePoints.AddRange(filtered);
        }

        private static bool IsLikelyBioStageNodeBoneName(string boneName)
        {
            if (string.IsNullOrWhiteSpace(boneName))
            {
                return false;
            }

            // Prefer explicit node/camera markers; avoid broad stage/floor matches that pull full rig detail.
            if (boneName.Contains("node", StringComparison.OrdinalIgnoreCase)
                || boneName.Contains("cam", StringComparison.OrdinalIgnoreCase)
                || boneName.Contains("camera", StringComparison.OrdinalIgnoreCase)
                || boneName.Contains("shot", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return false;
        }

        private static ExportEntry ResolveBioStageSkeletalMeshComponent(ExportEntry bioStageExport)
        {
            var meshExport = bioStageExport.GetProperty<ObjectProperty>("Mesh")?.ResolveToEntry(bioStageExport.FileRef) as ExportEntry;
            if (meshExport is not null && meshExport.ClassName.Contains("SkeletalMeshComponent", StringComparison.OrdinalIgnoreCase))
            {
                return meshExport;
            }

            var components = bioStageExport.GetProperty<ArrayProperty<ObjectProperty>>("Components");
            if (components is not null)
            {
                foreach (var entry in components.ResolveToEntries(bioStageExport.FileRef).OfType<ExportEntry>())
                {
                    if (entry.ClassName.Contains("SkeletalMeshComponent", StringComparison.OrdinalIgnoreCase))
                    {
                        return entry;
                    }
                }
            }

            return null;
        }

        private static Matrix4x4 BuildTransformFromProperties(PropertyCollection properties, Vector3 defaultScale)
        {
            if (properties is null)
            {
                return Matrix4x4.Identity;
            }

            var location = properties.GetProp<StructProperty>("location")
                           ?? properties.GetProp<StructProperty>("Location")
                           ?? properties.GetProp<StructProperty>("Translation")
                           ?? properties.GetProp<StructProperty>("translation");

            var rotation = properties.GetProp<StructProperty>("Rotation")
                           ?? properties.GetProp<StructProperty>("rotation");

            var drawScale3D = properties.GetProp<StructProperty>("DrawScale3D")
                            ?? properties.GetProp<StructProperty>("Scale3D")
                            ?? properties.GetProp<StructProperty>("scale3D");

            var prePivot = properties.GetProp<StructProperty>("PrePivot")
                          ?? properties.GetProp<StructProperty>("prePivot");

            float drawScale = properties.GetProp<FloatProperty>("DrawScale")?.Value
                           ?? properties.GetProp<FloatProperty>("Scale")?.Value
                           ?? 1f;

            Vector3 loc = location is null ? Vector3.Zero : CommonStructs.GetVector3(location);
            Vector3 scale = drawScale * (drawScale3D is null ? defaultScale : CommonStructs.GetVector3(drawScale3D));
            Vector3 pivot = prePivot is null ? Vector3.Zero : CommonStructs.GetVector3(prePivot);
            Rotator rot = rotation is null ? new Rotator(0, 0, 0) : CommonStructs.GetRotator(rotation);

            return ActorUtils.ComposeLocalToWorld(loc, rot, scale, pivot);
        }

        private static Vector3[] BuildApproximateWorldBonePositions(MeshBone[] bones)
        {
            var world = new Vector3[bones.Length];
            for (int i = 0; i < bones.Length; i++)
            {
                int parent = bones[i].ParentIndex;
                world[i] = bones[i].Position + (parent >= 0 && parent < bones.Length ? world[parent] : Vector3.Zero);
            }

            return world;
        }

        private static void TryCollectBioStageNodesFromProperties(ExportEntry bioStageExport, List<Vector3> nodePoints)
        {
            var props = bioStageExport.GetProperties();
            var cameraList = props.GetProp<ArrayProperty<StructProperty>>("m_aCameraList")
                             ?? props.GetProp<ArrayProperty<StructProperty>>("CameraList")
                             ?? props.GetProp<ArrayProperty<StructProperty>>("cameraList");

            if (cameraList is null)
            {
                return;
            }

            foreach (var cameraStruct in cameraList)
            {
                if (TryExtractBioStageNodePosition(cameraStruct, out var nodePosition))
                {
                    nodePoints.Add(nodePosition);
                }
            }
        }

        private static bool TryExtractBioStageNodePosition(StructProperty cameraStruct, out Vector3 nodePosition)
        {
            nodePosition = Vector3.Zero;
            if (cameraStruct is null)
            {
                return false;
            }

            var posStruct = cameraStruct.GetProp<StructProperty>("vPos")
                           ?? cameraStruct.GetProp<StructProperty>("m_vPos")
                           ?? cameraStruct.GetProp<StructProperty>("Location")
                           ?? cameraStruct.GetProp<StructProperty>("location")
                           ?? cameraStruct.GetProp<StructProperty>("Pos")
                           ?? cameraStruct.GetProp<StructProperty>("Position")
                           ?? cameraStruct.GetProp<StructProperty>("vPosition")
                           ?? cameraStruct.GetProp<StructProperty>("m_vPosition");

            if (TryExtractVectorFromStruct(posStruct, out nodePosition))
            {
                return true;
            }

            return TryExtractVectorRecursively(cameraStruct.Properties, out nodePosition);
        }

        private static bool TryExtractBioStageNodePosition(PropertyCollection cameraProps, out Vector3 nodePosition)
        {
            nodePosition = Vector3.Zero;

            var posStruct = cameraProps.GetProp<StructProperty>("vPos")
                           ?? cameraProps.GetProp<StructProperty>("m_vPos")
                           ?? cameraProps.GetProp<StructProperty>("Location")
                           ?? cameraProps.GetProp<StructProperty>("location")
                           ?? cameraProps.GetProp<StructProperty>("Pos")
                           ?? cameraProps.GetProp<StructProperty>("Position")
                           ?? cameraProps.GetProp<StructProperty>("vPosition")
                           ?? cameraProps.GetProp<StructProperty>("m_vPosition");

            if (TryExtractVectorFromStruct(posStruct, out nodePosition))
            {
                return true;
            }

            foreach (var structProp in cameraProps.OfType<StructProperty>())
            {
                if (TryExtractVectorFromStruct(structProp, out nodePosition))
                {
                    return true;
                }

                if (TryExtractVectorRecursively(structProp.Properties, out nodePosition))
                {
                    return true;
                }
            }

            foreach (var nestedStructArray in cameraProps.OfType<ArrayProperty<StructProperty>>())
            {
                foreach (var nested in nestedStructArray)
                {
                    if (TryExtractVectorFromStruct(nested, out nodePosition))
                    {
                        return true;
                    }

                    if (TryExtractVectorRecursively(nested.Properties, out nodePosition))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool TryExtractVectorRecursively(PropertyCollection properties, out Vector3 vector)
        {
            vector = Vector3.Zero;
            if (properties is null)
            {
                return false;
            }

            foreach (var prop in properties)
            {
                if (prop is StructProperty sp)
                {
                    if (TryExtractVectorFromStruct(sp, out vector))
                    {
                        return true;
                    }

                    if (TryExtractVectorRecursively(sp.Properties, out vector))
                    {
                        return true;
                    }
                }
                else if (prop is ArrayProperty<StructProperty> array)
                {
                    foreach (var item in array)
                    {
                        if (TryExtractVectorFromStruct(item, out vector))
                        {
                            return true;
                        }

                        if (TryExtractVectorRecursively(item.Properties, out vector))
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        private static bool TryExtractVectorFromStruct(StructProperty structProp, out Vector3 vector)
        {
            vector = Vector3.Zero;
            if (structProp is null)
            {
                return false;
            }

            try
            {
                vector = CommonStructs.GetVector3(structProp);
                return true;
            }
            catch
            {
                // fallback below
            }

            float? x = structProp.GetProp<FloatProperty>("X")?.Value
                       ?? structProp.GetProp<FloatProperty>("x")?.Value
                       ?? structProp.GetProp<IntProperty>("X")?.Value
                       ?? structProp.GetProp<IntProperty>("x")?.Value;

            float? y = structProp.GetProp<FloatProperty>("Y")?.Value
                       ?? structProp.GetProp<FloatProperty>("y")?.Value
                       ?? structProp.GetProp<IntProperty>("Y")?.Value
                       ?? structProp.GetProp<IntProperty>("y")?.Value;

            float? z = structProp.GetProp<FloatProperty>("Z")?.Value
                       ?? structProp.GetProp<FloatProperty>("z")?.Value
                       ?? structProp.GetProp<IntProperty>("Z")?.Value
                       ?? structProp.GetProp<IntProperty>("z")?.Value;

            if (x is null || y is null || z is null)
            {
                return false;
            }

            vector = new Vector3(x.Value, y.Value, z.Value);
            return true;
        }

        private void ExpandBoundsWithContext(ContextOverlayData context)
        {
            if (context is null)
            {
                return;
            }

            var points = new List<(float A, float B)>();
            points.Add(ProjectToPlane(new PathPoint { X = context.AnchorOrWorldPivot.X, Y = context.AnchorOrWorldPivot.Y, Z = context.AnchorOrWorldPivot.Z }));

            if (context.HasBioStageContext)
            {
                points.Add(ProjectToPlane(new PathPoint { X = context.BioStagePivot.X, Y = context.BioStagePivot.Y, Z = context.BioStagePivot.Z }));
                points.AddRange(context.BioStageNodePoints.Select(p => ProjectToPlane(new PathPoint { X = p.X, Y = p.Y, Z = p.Z })));
                points.AddRange(context.BioStageHullPoints.Select(p => ProjectToPlane(new PathPoint { X = p.X, Y = p.Y, Z = p.Z })));
            }

            foreach ((float A, float B) in points)
            {
                _dataMinA = Math.Min(_dataMinA, A);
                _dataMaxA = Math.Max(_dataMaxA, A);
                _dataMinB = Math.Min(_dataMinB, B);
                _dataMaxB = Math.Max(_dataMaxB, B);
            }
        }

        private bool TryCreateCameraOverlay(MoveTrackData path, bool isSelectedPath, double selectedDim, out CameraOverlay cameraOverlay)
        {
            cameraOverlay = null;
            if (!IsLikelyCameraTrack(path.Track))
            {
                return false;
            }

            var orientationPoints = CollectOrientationPoints(path.Track.Export);
            var fovPoints = CollectFovPoints(path.Track.Group);

            if (ShowPaths)
            {
                var cameraPath = new Polyline
                {
                    Stroke = Brushes.Tomato,
                    StrokeThickness = isSelectedPath ? 2.5 : 1.6,
                    Opacity = 0.65 * selectedDim,
                    StrokeDashArray = [4, 2]
                };

                foreach (var point in path.Points)
                {
                    (float projectedA, float projectedB) = ProjectToPlane(point);
                    cameraPath.Points.Add(MapToCanvas(projectedA, projectedB));
                }

                PreviewCanvas.Children.Add(cameraPath);
            }

            var cameraRing = new Ellipse
            {
                Width = MarkerSize + 8,
                Height = MarkerSize + 8,
                Fill = Brushes.Transparent,
                Stroke = Brushes.Tomato,
                StrokeThickness = isSelectedPath ? 2.0 : 1.3,
                Opacity = selectedDim,
                Visibility = ShowMarkers ? Visibility.Visible : Visibility.Hidden
            };
            PreviewCanvas.Children.Add(cameraRing);

            Line aimLine = null;
            if (ShowCameraAim)
            {
                aimLine = new Line
                {
                    Stroke = Brushes.OrangeRed,
                    StrokeThickness = isSelectedPath ? 1.8 : 1.3,
                    Opacity = 0.9 * selectedDim,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round
                };
                PreviewCanvas.Children.Add(aimLine);
            }

            Polygon frustumPolygon = null;
            if (ShowCameraFrustum)
            {
                frustumPolygon = new Polygon
                {
                    Stroke = Brushes.Orange,
                    Fill = new SolidColorBrush(Color.FromArgb(40, 255, 165, 0)),
                    StrokeThickness = isSelectedPath ? 1.4 : 1.0,
                    Opacity = 0.7 * selectedDim
                };
                PreviewCanvas.Children.Add(frustumPolygon);
            }

            cameraOverlay = new CameraOverlay
            {
                TrackExport = path.TrackExport,
                CameraGroupName = path.Track.Group?.GroupName ?? string.Empty,
                PositionPoints = path.Points,
                OrientationPoints = orientationPoints,
                FovPoints = fovPoints,
                CameraRing = cameraRing,
                AimLine = aimLine,
                FrustumPolygon = frustumPolygon
            };

            return true;
        }

        private void FitToDataBounds()
        {
            if (_dataMaxA <= _dataMinA && _dataMaxB <= _dataMinB)
            {
                return;
            }

            float rangeA = Math.Max(_dataMaxA - _dataMinA, 1f);
            float rangeB = Math.Max(_dataMaxB - _dataMinB, 1f);
            float marginA = rangeA * 0.1f;
            float marginB = rangeB * 0.1f;

            _viewMinA = _dataMinA - marginA;
            _viewMaxA = _dataMaxA + marginA;
            _viewMinB = _dataMinB - marginB;
            _viewMaxB = _dataMaxB + marginB;
            _hasViewport = true;
        }

        private List<MoveTrackData> CollectMoveTrackPoints()
        {
            var results = new List<MoveTrackData>();
            foreach (var moveTrack in _interpData.Groups.SelectMany(g => g.Tracks).OfType<InterpTrackMove>())
            {
                var props = moveTrack.Export.GetProperties();
                var posPoints = props.GetProp<StructProperty>("PosTrack")?
                    .GetProp<ArrayProperty<StructProperty>>("Points");

                if (posPoints is null || posPoints.Count == 0)
                {
                    continue;
                }

                var points = new List<PathPoint>(posPoints.Count);
                for (int i = 0; i < posPoints.Count; i++)
                {
                    var point = posPoints[i];
                    var outVal = point.GetProp<StructProperty>("OutVal");
                    if (outVal is null)
                    {
                        continue;
                    }

                    Vector3 vector = CommonStructs.GetVector3(outVal);
                    float time = point.GetProp<FloatProperty>("InVal")?.Value ?? 0f;
                    points.Add(new PathPoint
                    {
                        Time = time,
                        X = vector.X,
                        Y = vector.Y,
                        Z = vector.Z
                    });
                }

                if (points.Count > 0)
                {
                    points.Sort((a, b) => a.Time.CompareTo(b.Time));
                    results.Add(new MoveTrackData
                    {
                        Track = moveTrack,
                        TrackExport = moveTrack.Export,
                        Points = points
                    });
                }
            }

            return results;
        }

        private void ComputeDataBounds(List<MoveTrackData> paths)
        {
            var projected = paths.SelectMany(p => p.Points.Select(ProjectToPlane)).ToList();
            _dataMinA = projected.Min(p => p.A);
            _dataMaxA = projected.Max(p => p.A);
            _dataMinB = projected.Min(p => p.B);
            _dataMaxB = projected.Max(p => p.B);
        }

        private (float A, float B) ProjectToPlane(PathPoint point)
        {
            return SelectedPlane switch
            {
                "XZ" => (point.X, point.Z),
                "YZ" => (point.Y, point.Z),
                _ => (point.X, point.Y)
            };
        }

        private (float A, float B) ProjectDirectionToPlane(Vector3 direction)
        {
            return SelectedPlane switch
            {
                "XZ" => (direction.X, direction.Z),
                "YZ" => (direction.Y, direction.Z),
                _ => (direction.X, direction.Y)
            };
        }

        private static bool IsLikelyCameraTrack(InterpTrackMove moveTrack)
        {
            var group = moveTrack.Group;
            if (group is null)
            {
                return false;
            }

            if (!string.IsNullOrEmpty(group.GroupName)
                && (group.GroupName.Contains("camera", StringComparison.OrdinalIgnoreCase)
                    || group.GroupName.StartsWith("cam", StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }

            return group.Tracks.Any(track =>
            {
                if (track is not InterpTrackFloatBase)
                {
                    return false;
                }

                string propertyName = track.Export.GetProperty<NameProperty>("PropertyName")?.Value.Instanced;
                if (!string.IsNullOrEmpty(propertyName) && propertyName.Equals("FOVAngle", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                return !string.IsNullOrEmpty(track.TrackTitle)
                       && track.TrackTitle.Contains("FOV", StringComparison.OrdinalIgnoreCase);
            });
        }

        private static List<OrientationPoint> CollectOrientationPoints(ExportEntry moveTrackExport)
        {
            var results = new List<OrientationPoint>();
            var eulerPoints = moveTrackExport.GetProperties()
                .GetProp<StructProperty>("EulerTrack")?
                .GetProp<ArrayProperty<StructProperty>>("Points");

            if (eulerPoints is null)
            {
                return results;
            }

            foreach (var point in eulerPoints)
            {
                var outVal = point.GetProp<StructProperty>("OutVal");
                if (outVal is null)
                {
                    continue;
                }

                Vector3 degrees = CommonStructs.GetVector3(outVal);
                float time = point.GetProp<FloatProperty>("InVal")?.Value ?? 0f;
                results.Add(new OrientationPoint
                {
                    Time = time,
                    Roll = degrees.X,
                    Pitch = degrees.Y,
                    Yaw = degrees.Z
                });
            }

            results.Sort((a, b) => a.Time.CompareTo(b.Time));
            return results;
        }

        private static List<FloatCurvePoint> CollectFovPoints(InterpGroup group)
        {
            var results = new List<FloatCurvePoint>();
            if (group is null)
            {
                return results;
            }

            ExportEntry fovTrackExport = group.Tracks
                .FirstOrDefault(track =>
                {
                    if (track is not InterpTrackFloatBase)
                    {
                        return false;
                    }

                    string propertyName = track.Export.GetProperty<NameProperty>("PropertyName")?.Value.Instanced;
                    return (!string.IsNullOrEmpty(propertyName)
                            && propertyName.Equals("FOVAngle", StringComparison.OrdinalIgnoreCase))
                           || (!string.IsNullOrEmpty(track.TrackTitle)
                               && track.TrackTitle.Contains("FOV", StringComparison.OrdinalIgnoreCase));
                })?.Export;

            var floatPoints = fovTrackExport?.GetProperties()
                .GetProp<StructProperty>("FloatTrack")?
                .GetProp<ArrayProperty<StructProperty>>("Points");

            if (floatPoints is null)
            {
                return results;
            }

            foreach (var point in floatPoints)
            {
                float time = point.GetProp<FloatProperty>("InVal")?.Value ?? 0f;
                float value = point.GetProp<FloatProperty>("OutVal")?.Value ?? DefaultCameraFov;
                results.Add(new FloatCurvePoint
                {
                    Time = time,
                    Value = value
                });
            }

            results.Sort((a, b) => a.Time.CompareTo(b.Time));
            return results;
        }

        private bool IsSelectedTrack(ExportEntry trackExport)
        {
            if (_selectedExport is null)
            {
                return false;
            }

            return _selectedExport == trackExport
                   || _selectedExport.IsDescendantOf(trackExport)
                   || trackExport.IsDescendantOf(_selectedExport);
        }

        private Point MapToCanvas(float x, float y)
        {
            GetDrawableDimensions(out double drawableWidth, out double drawableHeight);

            double rangeX = Math.Max(_viewMaxA - _viewMinA, 0.001f);
            double rangeY = Math.Max(_viewMaxB - _viewMinB, 0.001f);

            double normalizedX = (x - _viewMinA) / rangeX;
            double normalizedY = (y - _viewMinB) / rangeY;

            return new Point(
                CanvasPadding + normalizedX * drawableWidth,
                CanvasPadding + (1d - normalizedY) * drawableHeight);
        }

        private void GetDrawableDimensions(out double drawableWidth, out double drawableHeight)
        {
            double width = Math.Max(PreviewCanvas.ActualWidth, 1d);
            double height = Math.Max(PreviewCanvas.ActualHeight, 1d);
            drawableWidth = Math.Max(width - CanvasPadding * 2d, 1d);
            drawableHeight = Math.Max(height - CanvasPadding * 2d, 1d);
        }

        private (float A, float B) CanvasToWorld(Point canvasPoint)
        {
            GetDrawableDimensions(out double drawableWidth, out double drawableHeight);
            double clampedX = Math.Clamp(canvasPoint.X - CanvasPadding, 0d, drawableWidth);
            double clampedY = Math.Clamp(canvasPoint.Y - CanvasPadding, 0d, drawableHeight);

            double normalizedX = clampedX / drawableWidth;
            double normalizedY = 1d - (clampedY / drawableHeight);

            float worldA = (float)(_viewMinA + normalizedX * (_viewMaxA - _viewMinA));
            float worldB = (float)(_viewMinB + normalizedY * (_viewMaxB - _viewMinB));
            return (worldA, worldB);
        }

        private void ZoomAt(Point canvasPoint, float zoomFactor)
        {
            if (!_hasViewport)
            {
                return;
            }

            (float focusA, float focusB) = CanvasToWorld(canvasPoint);
            float oldRangeA = Math.Max(_viewMaxA - _viewMinA, 0.001f);
            float oldRangeB = Math.Max(_viewMaxB - _viewMinB, 0.001f);
            float newRangeA = Math.Max(oldRangeA * zoomFactor, 0.001f);
            float newRangeB = Math.Max(oldRangeB * zoomFactor, 0.001f);

            float tA = oldRangeA > 0.0001f ? (focusA - _viewMinA) / oldRangeA : 0.5f;
            float tB = oldRangeB > 0.0001f ? (focusB - _viewMinB) / oldRangeB : 0.5f;

            _viewMinA = focusA - tA * newRangeA;
            _viewMaxA = _viewMinA + newRangeA;
            _viewMinB = focusB - tB * newRangeB;
            _viewMaxB = _viewMinB + newRangeB;
        }

        private void PanByPixels(double deltaX, double deltaY)
        {
            if (!_hasViewport)
            {
                return;
            }

            GetDrawableDimensions(out double drawableWidth, out double drawableHeight);
            float rangeA = _viewMaxA - _viewMinA;
            float rangeB = _viewMaxB - _viewMinB;

            float deltaA = (float)(deltaX / drawableWidth) * rangeA;
            float deltaB = (float)(deltaY / drawableHeight) * rangeB;

            _viewMinA -= deltaA;
            _viewMaxA -= deltaA;
            _viewMinB += deltaB;
            _viewMaxB += deltaB;
        }

        private void PreviewCanvas_OnMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (_interpData is null || !_hasViewport)
            {
                return;
            }

            float factor = e.Delta > 0 ? 0.9f : 1.1f;
            ZoomAt(e.GetPosition(PreviewCanvas), factor);
            RefreshVisualization();
            e.Handled = true;
        }

        private void PreviewCanvas_OnMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_interpData is null || !_hasViewport)
            {
                return;
            }

            _isPanning = true;
            _lastPanMousePoint = e.GetPosition(PreviewCanvas);
            PreviewCanvas.CaptureMouse();
            e.Handled = true;
        }

        private void PreviewCanvas_OnMouseMove(object sender, MouseEventArgs e)
        {
            if (!_isPanning)
            {
                return;
            }

            Point current = e.GetPosition(PreviewCanvas);
            System.Windows.Vector delta = current - _lastPanMousePoint;
            _lastPanMousePoint = current;
            PanByPixels(delta.X, delta.Y);
            RefreshVisualization();
        }

        private void PreviewCanvas_OnMouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_isPanning)
            {
                return;
            }

            _isPanning = false;
            PreviewCanvas.ReleaseMouseCapture();
            e.Handled = true;
        }

        private void UpdateMarkers()
        {
            if (_movePaths.Count == 0)
            {
                return;
            }

            foreach (var path in _movePaths)
            {
                var current = GetPositionAtTime(path.Points, _currentTime);
                (float projectedA, float projectedB) = ProjectToPlane(current);
                Point canvasPoint = MapToCanvas(projectedA, projectedB);
                Canvas.SetLeft(path.Marker, canvasPoint.X - MarkerSize / 2d);
                Canvas.SetTop(path.Marker, canvasPoint.Y - MarkerSize / 2d);
            }
        }

        private void UpdateCameraOverlays()
        {
            if (_cameraOverlays.Count == 0)
            {
                return;
            }

            foreach (var overlay in _cameraOverlays)
            {
                var position = GetPositionAtTime(overlay.PositionPoints, _currentTime);
                (float projectedA, float projectedB) = ProjectToPlane(position);
                Point cameraPoint = MapToCanvas(projectedA, projectedB);

                Canvas.SetLeft(overlay.CameraRing, cameraPoint.X - overlay.CameraRing.Width / 2d);
                Canvas.SetTop(overlay.CameraRing, cameraPoint.Y - overlay.CameraRing.Height / 2d);

                var orientation = GetOrientationAtTime(overlay.OrientationPoints, _currentTime);
                if (orientation is null)
                {
                    if (overlay.AimLine is not null)
                    {
                        overlay.AimLine.Visibility = Visibility.Collapsed;
                    }

                    if (overlay.FrustumPolygon is not null)
                    {
                        overlay.FrustumPolygon.Visibility = Visibility.Collapsed;
                    }

                    continue;
                }

                var direction3 = Rotator.FromDegreesVector(new Vector3(orientation.Roll, orientation.Pitch, orientation.Yaw)).GetDirectionalVector();
                (float dirA, float dirB) = ProjectDirectionToPlane(direction3);
                float dirLen = MathF.Sqrt(dirA * dirA + dirB * dirB);
                if (dirLen < 0.0001f)
                {
                    if (overlay.AimLine is not null)
                    {
                        overlay.AimLine.Visibility = Visibility.Collapsed;
                    }

                    if (overlay.FrustumPolygon is not null)
                    {
                        overlay.FrustumPolygon.Visibility = Visibility.Collapsed;
                    }

                    continue;
                }

                dirA /= dirLen;
                dirB /= dirLen;

                if (overlay.AimLine is not null)
                {
                    overlay.AimLine.Visibility = Visibility.Visible;
                    var endPoint = MapToCanvas(projectedA + dirA * CameraAimLengthWorld, projectedB + dirB * CameraAimLengthWorld);
                    overlay.AimLine.X1 = cameraPoint.X;
                    overlay.AimLine.Y1 = cameraPoint.Y;
                    overlay.AimLine.X2 = endPoint.X;
                    overlay.AimLine.Y2 = endPoint.Y;
                }

                if (overlay.FrustumPolygon is not null)
                {
                    overlay.FrustumPolygon.Visibility = Visibility.Visible;
                    float fov = GetFloatAtTime(overlay.FovPoints, _currentTime, DefaultCameraFov);
                    float halfAngle = Math.Clamp(fov, 1f, 170f) * MathF.PI / 360f;

                    var leftDir = Rotate2D(dirA, dirB, halfAngle);
                    var rightDir = Rotate2D(dirA, dirB, -halfAngle);

                    var leftPoint = MapToCanvas(projectedA + leftDir.X * CameraFrustumLengthWorld, projectedB + leftDir.Y * CameraFrustumLengthWorld);
                    var rightPoint = MapToCanvas(projectedA + rightDir.X * CameraFrustumLengthWorld, projectedB + rightDir.Y * CameraFrustumLengthWorld);

                    overlay.FrustumPolygon.Points.Clear();
                    overlay.FrustumPolygon.Points.Add(cameraPoint);
                    overlay.FrustumPolygon.Points.Add(leftPoint);
                    overlay.FrustumPolygon.Points.Add(rightPoint);
                }
            }
        }

        private static (float X, float Y) Rotate2D(float x, float y, float radians)
        {
            float cos = MathF.Cos(radians);
            float sin = MathF.Sin(radians);
            return (x * cos - y * sin, x * sin + y * cos);
        }

        private static OrientationPoint GetOrientationAtTime(List<OrientationPoint> points, float time)
        {
            if (points is null || points.Count == 0)
            {
                return null;
            }

            if (time <= points[0].Time)
            {
                return points[0];
            }

            if (time >= points[^1].Time)
            {
                return points[^1];
            }

            for (int i = 1; i < points.Count; i++)
            {
                var next = points[i];
                if (time > next.Time)
                {
                    continue;
                }

                var prev = points[i - 1];
                float duration = Math.Max(next.Time - prev.Time, 0.0001f);
                float t = Math.Clamp((time - prev.Time) / duration, 0f, 1f);
                return new OrientationPoint
                {
                    Time = time,
                    Roll = prev.Roll + (next.Roll - prev.Roll) * t,
                    Pitch = prev.Pitch + (next.Pitch - prev.Pitch) * t,
                    Yaw = prev.Yaw + (next.Yaw - prev.Yaw) * t
                };
            }

            return points[^1];
        }

        private static float GetFloatAtTime(List<FloatCurvePoint> points, float time, float fallback)
        {
            if (points is null || points.Count == 0)
            {
                return fallback;
            }

            if (time <= points[0].Time)
            {
                return points[0].Value;
            }

            if (time >= points[^1].Time)
            {
                return points[^1].Value;
            }

            for (int i = 1; i < points.Count; i++)
            {
                var next = points[i];
                if (time > next.Time)
                {
                    continue;
                }

                var prev = points[i - 1];
                float duration = Math.Max(next.Time - prev.Time, 0.0001f);
                float t = Math.Clamp((time - prev.Time) / duration, 0f, 1f);
                return prev.Value + (next.Value - prev.Value) * t;
            }

            return points[^1].Value;
        }

        private static PathPoint GetPositionAtTime(List<PathPoint> points, float time)
        {
            if (time <= points[0].Time)
            {
                return points[0];
            }

            if (time >= points[^1].Time)
            {
                return points[^1];
            }

            for (int i = 1; i < points.Count; i++)
            {
                var next = points[i];
                if (time > next.Time)
                {
                    continue;
                }

                var prev = points[i - 1];
                float segmentDuration = Math.Max(next.Time - prev.Time, 0.0001f);
                float t = Math.Clamp((time - prev.Time) / segmentDuration, 0f, 1f);
                return new PathPoint
                {
                    Time = time,
                    X = prev.X + (next.X - prev.X) * t,
                    Y = prev.Y + (next.Y - prev.Y) * t,
                    Z = prev.Z + (next.Z - prev.Z) * t
                };
            }

            return points[^1];
        }
    }
}
