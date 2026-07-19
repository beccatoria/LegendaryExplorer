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

        private bool _isInitialized;
        private bool _isProgrammaticScrubUpdate;
        private bool _isPanning;
        private InterpData _interpData;
        private ExportEntry _selectedExport;
        private float _currentTime;
        private Point _lastPanMousePoint;
        private readonly List<MoveTrackPath> _movePaths = [];
        private readonly List<CameraOverlay> _cameraOverlays = [];

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
                ShowCameraOverlays = true;
                ShowCameraAim = true;
                ShowCameraFrustum = true;
                FitToDataBounds();
                RefreshVisualization();
            });

            DataContext = this;
            InitializeComponent();
            PreviewCanvas.SizeChanged += (_, _) =>
            {
                if (_isInitialized && _interpData is not null)
                {
                    RefreshVisualization();
                }
            };
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
            SummaryText = "No InterpData loaded";
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

            if (_interpData is null)
            {
                SummaryText = "No InterpData loaded";
                UpdateOverlayTipText(0);
                return;
            }

            int groupCount = _interpData.Groups.Count;
            int trackCount = _interpData.Groups.Sum(g => g.Tracks.Count);

            var moveTrackPoints = CollectMoveTrackPoints();
            if (moveTrackPoints.Count == 0)
            {
                SummaryText = $"Groups: {groupCount} | Tracks: {trackCount} | Move tracks: 0";
                UpdateOverlayTipText(0);
                return;
            }

            ComputeDataBounds(moveTrackPoints);
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
            UpdateCameraOverlays();
            UpdateOverlayTipText(cameraCount);
            SummaryText = $"Groups: {groupCount} | Tracks: {trackCount} | Move tracks: {_movePaths.Count} | Cameras: {cameraCount}";
        }

        private void UpdateOverlayTipText(int cameraCount)
        {
            string planeGuide = SelectedPlane switch
            {
                "XZ" => "Plane XZ (X horizontal, Z vertical)",
                "YZ" => "Plane YZ (Y horizontal, Z vertical)",
                _ => "Plane XY (X horizontal, Y vertical)"
            };

            if (!ShowCameraOverlays)
            {
                OverlayTipText = $"{planeGuide}\nCamera overlays off";
                return;
            }

            if (cameraCount <= 0)
            {
                OverlayTipText = $"{planeGuide}\nNo camera tracks detected";
                return;
            }

            var overlay = _cameraOverlays[0];
            var orientation = GetOrientationAtTime(overlay.OrientationPoints, _currentTime);
            float fov = GetFloatAtTime(overlay.FovPoints, _currentTime, DefaultCameraFov);
            string orientationText = orientation is null
                ? "Aim: n/a"
                : $"Yaw {orientation.Yaw:0.0}°  Pitch {orientation.Pitch:0.0}°";
            OverlayTipText = $"{planeGuide}\n{orientationText}  FOV {fov:0.0}°  ({cameraCount} cam)";
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
