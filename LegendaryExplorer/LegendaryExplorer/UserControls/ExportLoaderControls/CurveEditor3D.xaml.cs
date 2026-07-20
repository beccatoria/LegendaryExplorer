using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using LegendaryExplorer.Dialogs;
using LegendaryExplorer.Misc;
using LegendaryExplorer.Misc.AppSettings;
using LegendaryExplorer.SharedUI;
using LegendaryExplorer.Tools.LevelEditor;
using LegendaryExplorer.Tools.LevelEditor.Scene3D;
using LegendaryExplorer.Tools.PackageEditor.Experiments;
using LegendaryExplorer.UserControls.Interfaces;
using LegendaryExplorerCore.Packages;
using LegendaryExplorerCore.SharpDX;
using LegendaryExplorerCore.Unreal;
using LegendaryExplorerCore.Unreal.BinaryConverters;
using LegendaryExplorerCore.Unreal.ObjectInfo;
using Newtonsoft.Json;
using System.Windows.Threading;
using MessageBox = Xceed.Wpf.Toolkit.MessageBox;

namespace LegendaryExplorer.UserControls.ExportLoaderControls;

public sealed partial class CurveEditor3D : ExportLoaderControl, IActorEditorContext, ISceneRenderContextConfigurable
{
    private static readonly RenderPass[] RenderPasses = [RenderPass.Base, RenderPass.Hair];

    private enum AddKeyframePlacement
    {
        Before,
        After
    }

    private readonly CurveEditor3DModel model = new();
    private readonly List<IMEPackage> levelPackages = [];
    private readonly List<ActorProxy> levelActors = [];
    private readonly List<string> levelPaths = [];
    private IReadOnlyList<Vector3> trajectorySamples = [];
    private bool eventsAttached;
    private bool isPlayingMove;
    private bool trajectorySamplesDirty;
    private Button playMoveButton;
    private CurveEditor3DKeyframe selectedKeyframe;
    private string currentExportName;
    private string sceneStatus = "Select an InterpTrackMove export, then optionally open a level backdrop.";
    private string playbackKeyframeStatus = "Not playing";
    private float playbackStartTime;
    private float playbackEndTime;
    private float playbackElapsed;
    private Vector3 pendingViewportKeyframeLocation;
    private Vector3 pendingViewportSelectedKeyframeLocation;
    private bool showCollision = false;
    private bool showLightIcons = true;
    private bool showVolumes = false;
    private bool showVolumetrics;
    private bool unlit = false;
    private bool setAlphaToBlack = true;
    private bool showRedChannel = true;
    private bool showGreenChannel = true;
    private bool showBlueChannel = true;
    private bool showAlphaChannel = true;
    private System.Windows.Media.Color backgroundColor;
    private string cameraPositionX = "0";
    private string cameraPositionY = "0";
    private string cameraPositionZ = "0";
    private string cameraRotationX = "0";
    private string cameraRotationY = "0";
    private string cameraRotationZ = "0";
    private float cameraPositionStep = 10f;
    private float cameraRotationStep = 5f;
    private bool updatingCameraPositionText;
    private int cameraPositionEditorsFocused;
    private bool updatingCameraRotationText;
    private int cameraRotationEditorsFocused;

    public CurveEditor3D() : base("3D Curve Editor")
    {
        RenderContext = new LevelEditorRenderContext();
        backgroundColor = GetThemeDefaultBackgroundColor();
        RenderContext.BackgroundColor = backgroundColor;
        RenderContext.ShowLightIcons = showLightIcons;
        if (unlit)
        {
            RenderContext.RenderFlags |= LevelEditorRenderContext.ShaderFlags.Unlit;
        }
        InterpModes = Enum.GetValues<EInterpCurveMode>();
        LoadCommands();
        InitializeComponent();
        ConfigureKeyframeContextMenu();
        SceneViewer.Context = RenderContext;
        RenderContext.EnableTransformWidget();
        model.Changed += Model_Changed;
        RenderContext.UpdateScene += UpdatePlayback;
    }

    public LevelEditorRenderContext RenderContext { get; }

    public bool IsApplyingUndoRedo => false;

    public bool ShowCollision
    {
        get => showCollision;
        set
        {
            if (SetProperty(ref showCollision, value))
            {
                SceneViewer?.MarkRenderDirty();
            }
        }
    }

    public bool ShowLightIcons
    {
        get => showLightIcons;
        set
        {
            if (SetProperty(ref showLightIcons, value))
            {
                RenderContext.ShowLightIcons = value;
                SceneViewer?.MarkRenderDirty();
            }
        }
    }

    public bool ShowVolumes
    {
        get => showVolumes;
        set
        {
            if (SetProperty(ref showVolumes, value))
            {
                SceneViewer?.MarkRenderDirty();
            }
        }
    }

    public bool ShowVolumetrics
    {
        get => showVolumetrics;
        set
        {
            if (SetProperty(ref showVolumetrics, value))
            {
                SceneViewer?.MarkRenderDirty();
            }
        }
    }

    public bool Unlit
    {
        get => unlit;
        set
        {
            if (SetProperty(ref unlit, value))
            {
                if (value)
                {
                    RenderContext.RenderFlags |= LevelEditorRenderContext.ShaderFlags.Unlit;
                }
                else
                {
                    RenderContext.RenderFlags &= ~LevelEditorRenderContext.ShaderFlags.Unlit;
                }
                SceneViewer?.MarkRenderDirty();
            }
        }
    }

    public bool SetAlphaToBlack
    {
        get => setAlphaToBlack;
        set
        {
            if (SetProperty(ref setAlphaToBlack, value))
            {
                if (value)
                {
                    RenderContext.RenderFlags |= LevelEditorRenderContext.ShaderFlags.AlphaAsBlack;
                }
                else
                {
                    RenderContext.RenderFlags &= ~LevelEditorRenderContext.ShaderFlags.AlphaAsBlack;
                }
            }
        }
    }

    public bool ShowRedChannel
    {
        get => showRedChannel;
        set
        {
            if (SetProperty(ref showRedChannel, value))
            {
                if (value)
                {
                    RenderContext.RenderFlags |= LevelEditorRenderContext.ShaderFlags.EnableRedChannel;
                }
                else
                {
                    RenderContext.RenderFlags &= ~LevelEditorRenderContext.ShaderFlags.EnableRedChannel;
                }
            }
        }
    }

    public bool ShowGreenChannel
    {
        get => showGreenChannel;
        set
        {
            if (SetProperty(ref showGreenChannel, value))
            {
                if (value)
                {
                    RenderContext.RenderFlags |= LevelEditorRenderContext.ShaderFlags.EnableGreenChannel;
                }
                else
                {
                    RenderContext.RenderFlags &= ~LevelEditorRenderContext.ShaderFlags.EnableGreenChannel;
                }
            }
        }
    }

    public bool ShowBlueChannel
    {
        get => showBlueChannel;
        set
        {
            if (SetProperty(ref showBlueChannel, value))
            {
                if (value)
                {
                    RenderContext.RenderFlags |= LevelEditorRenderContext.ShaderFlags.EnableBlueChannel;
                }
                else
                {
                    RenderContext.RenderFlags &= ~LevelEditorRenderContext.ShaderFlags.EnableBlueChannel;
                }
            }
        }
    }

    public bool ShowAlphaChannel
    {
        get => showAlphaChannel;
        set
        {
            if (SetProperty(ref showAlphaChannel, value))
            {
                if (value)
                {
                    RenderContext.RenderFlags |= LevelEditorRenderContext.ShaderFlags.EnableAlphaChannel;
                }
                else
                {
                    RenderContext.RenderFlags &= ~LevelEditorRenderContext.ShaderFlags.EnableAlphaChannel;
                }
            }
        }
    }

    public System.Windows.Media.Color BackgroundColor
    {
        get => backgroundColor;
        set
        {
            if (SetProperty(ref backgroundColor, value))
            {
                RenderContext.BackgroundColor = value;
                SceneViewer?.MarkRenderDirty();
            }
        }
    }

    public bool UseLocalCoordsForWidget
    {
        get => RenderContext.TransformWidget.UseLocalCoords;
        set => SetProperty(ref RenderContext.TransformWidget.UseLocalCoords, value);
    }

    public string CameraPositionX
    {
        get => cameraPositionX;
        set => SetProperty(ref cameraPositionX, value);
    }

    public string CameraPositionY
    {
        get => cameraPositionY;
        set => SetProperty(ref cameraPositionY, value);
    }

    public string CameraPositionZ
    {
        get => cameraPositionZ;
        set => SetProperty(ref cameraPositionZ, value);
    }

    public string CameraRotationX
    {
        get => cameraRotationX;
        set => SetProperty(ref cameraRotationX, value);
    }

    public string CameraRotationY
    {
        get => cameraRotationY;
        set => SetProperty(ref cameraRotationY, value);
    }

    public string CameraRotationZ
    {
        get => cameraRotationZ;
        set => SetProperty(ref cameraRotationZ, value);
    }

    public float CameraPositionStep
    {
        get => cameraPositionStep;
        set => SetProperty(ref cameraPositionStep, value);
    }

    public float CameraRotationStep
    {
        get => cameraRotationStep;
        set => SetProperty(ref cameraRotationStep, value);
    }

    public ICommand ToggleTranslateCommand { get; private set; }

    public ICommand ToggleRotateCommand { get; private set; }

    public ICommand ToggleScaleCommand { get; private set; }

    public ICommand ToggleUniformScaleCommand { get; private set; }

    public ICommand ToggleLocalCoordsCommand { get; private set; }

    public IReadOnlyList<EInterpCurveMode> InterpModes { get; }

    public string CurrentExportName
    {
        get => currentExportName;
        private set => SetProperty(ref currentExportName, value);
    }

    public string SceneStatus
    {
        get => sceneStatus;
        private set => SetProperty(ref sceneStatus, value);
    }

    public string PlaybackKeyframeStatus
    {
        get => playbackKeyframeStatus;
        private set => SetProperty(ref playbackKeyframeStatus, value);
    }

    public CurveEditor3DKeyframe SelectedKeyframe
    {
        get => selectedKeyframe;
        private set
        {
            bool selectionChanged = SetProperty(ref selectedKeyframe, value);
            SnapToKeyButton.IsEnabled = value is not null;
            KeyframeList.SelectedItem = value;
            if (value is not null && (selectionChanged || !KeyframeList.IsKeyboardFocusWithin))
            {
                KeyframeList.ScrollIntoView(value);
            }
            RenderContext.TransformWidget.Attach = value;
            SceneViewer?.MarkRenderDirty();
        }
    }

    public override bool CanParse(ExportEntry exportEntry)
        => exportEntry?.ClassName == "InterpTrackMove"
           && exportEntry.GetProperty<StructProperty>("PosTrack") is not null
           && exportEntry.GetProperty<StructProperty>("EulerTrack") is not null;

    public override void LoadExport(ExportEntry exportEntry)
    {
        bool isSameExport = CurrentLoadedExport is not null
                            && CurrentLoadedExport.FileRef == exportEntry.FileRef
                            && CurrentLoadedExport.UIndex == exportEntry.UIndex;
        float? selectedKeyframeTime = isSameExport ? SelectedKeyframe?.Time : null;
        StopPlayback(false);
        UnregisterKeyframes();
        CurrentLoadedExport = exportEntry;
        model.Load(exportEntry);
        trajectorySamplesDirty = true;
        KeyframeList.ItemsSource = model.Keyframes;
        RegisterKeyframes();
        SelectedKeyframe = selectedKeyframeTime.HasValue && model.Keyframes.Count > 0
            ? model.Keyframes.MinBy(keyframe => MathF.Abs(keyframe.Time - selectedKeyframeTime.Value))
            : model.Keyframes.FirstOrDefault();
        CurrentExportName = $"{exportEntry.UIndex}: {exportEntry.InstancedFullPath}";
        SceneStatus = $"{model.Keyframes.Count} trajectory keyframe(s); {levelPaths.Count} level backdrop file(s).";
        UpdatePlaybackButton();
        SceneViewer?.MarkRenderDirty();
    }

    public override void UnloadExport()
    {
        StopPlayback(false);
        UnregisterKeyframes();
        model.Clear();
        trajectorySamples = [];
        trajectorySamplesDirty = false;
        PlaybackKeyframeStatus = "Not playing";
        KeyframeList.ItemsSource = null;
        SelectedKeyframe = null;
        CurrentLoadedExport = null;
        CurrentExportName = null;
        UpdatePlaybackButton();
        SceneViewer?.MarkRenderDirty();
    }

    public override void PopOut()
    {
        if (CurrentLoadedExport is null)
        {
            return;
        }

        var window = new ExportLoaderHostedWindow(new CurveEditor3D(), CurrentLoadedExport)
        {
            Title = $"3D Curve Editor - {CurrentLoadedExport.UIndex} {CurrentLoadedExport.InstancedFullPath}"
        };
        window.Show();
    }

    public override void Dispose()
    {
        UnloadExport();
        CloseLevels();
        DetachEvents();
        model.Changed -= Model_Changed;
        RenderContext.UpdateScene -= UpdatePlayback;
        KeyframeList.PreviewMouseRightButtonDown -= KeyframeList_PreviewMouseRightButtonDown;
        SceneViewer.Dispose();
    }

    private void ConfigureKeyframeContextMenu()
    {
        KeyframeList.PreviewMouseRightButtonDown += KeyframeList_PreviewMouseRightButtonDown;
        KeyframeList.ContextMenu = CreateKeyframeContextMenu();
    }

    private ContextMenu CreateKeyframeContextMenu()
    {
        var menu = new ContextMenu();

        var addItem = new MenuItem { Header = "Add Keyframe..." };
        addItem.Click += AddKeyframe_Click;
        menu.Items.Add(addItem);

        var deleteItem = new MenuItem { Header = "Delete Keyframe" };
        deleteItem.Click += DeleteKeyframe_Click;
        menu.Items.Add(deleteItem);

        menu.Items.Add(new Separator());

        var shiftItem = new MenuItem { Header = "Shift InterpTrack..." };
        shiftItem.Click += ShiftInterpTrack_Click;
        menu.Items.Add(shiftItem);

        return menu;
    }

    private void OnThemeChanged(object sender, bool isDarkMode)
    {
        BackgroundColor = GetThemeDefaultBackgroundColor();
    }

    private static System.Windows.Media.Color GetThemeDefaultBackgroundColor()
    {
        return System.Windows.Media.Color.FromRgb(128, 128, 128);
    }

    private void LoadCommands()
    {
        ToggleTranslateCommand = new GenericCommand(() => RenderContext.TransformWidget.Mode = EWidgetMode.Translate);
        ToggleRotateCommand = new GenericCommand(() => RenderContext.TransformWidget.Mode = EWidgetMode.Rotate);
        ToggleScaleCommand = new GenericCommand(() => RenderContext.TransformWidget.Mode = EWidgetMode.Scale);
        ToggleUniformScaleCommand = new GenericCommand(() => RenderContext.TransformWidget.Mode = EWidgetMode.UniformScale);
        ToggleLocalCoordsCommand = new GenericCommand(() => UseLocalCoordsForWidget = !UseLocalCoordsForWidget);
    }

    private void CurveEditor3D_Loaded(object sender, RoutedEventArgs e)
    {
        AttachEvents();
        SceneViewer.SetShouldRender(true);
    }

    private void CurveEditor3D_Unloaded(object sender, RoutedEventArgs e)
    {
        SceneViewer.SetShouldRender(false);
    }

    private void AttachEvents()
    {
        if (eventsAttached)
        {
            return;
        }

        RenderContext.RenderScene += RenderScene;
        RenderContext.SelectHitProxy += SelectHitProxy;
        RenderContext.RightClickHitProxy += RightClickHitProxy;
        RenderContext.SelectActor += IgnoreActorSelection;
        RenderContext.RightClickActor += RightClickActor;
        RenderContext.RightClickViewport += RightClickViewport;
        eventsAttached = true;
    }

    private void DetachEvents()
    {
        if (!eventsAttached)
        {
            return;
        }

        RenderContext.RenderScene -= RenderScene;
        RenderContext.SelectHitProxy -= SelectHitProxy;
        RenderContext.RightClickHitProxy -= RightClickHitProxy;
        RenderContext.SelectActor -= IgnoreActorSelection;
        RenderContext.RightClickActor -= RightClickActor;
        RenderContext.RightClickViewport -= RightClickViewport;
        eventsAttached = false;
    }

    private void SelectHitProxy(IHitProxy hitProxy)
    {
        if (hitProxy is CurveEditor3DKeyframe keyframe)
        {
            SelectedKeyframe = keyframe;
        }
    }

    private void RightClickHitProxy(IHitProxy hitProxy)
    {
        if (hitProxy is CurveEditor3DKeyframe keyframe)
        {
            SelectedKeyframe = keyframe;
            ShowKeyframeContextMenu(SceneViewer);
        }
        else
        {
            ShowViewportContextMenu(SceneViewer);
        }
    }

    private void IgnoreActorSelection(ActorProxy actor)
    {
        RenderContext.TransformWidget.Attach = SelectedKeyframe;
    }

    private void RightClickActor(ActorProxy actor)
    {
        ShowViewportContextMenu(SceneViewer);
    }

    private void RightClickViewport()
    {
        ShowViewportContextMenu(SceneViewer);
    }

    private void TranslateMode_Click(object sender, RoutedEventArgs e)
    {
        RenderContext.TransformWidget.Mode = EWidgetMode.Translate;
        SceneViewer.MarkRenderDirty();
        SceneViewer.Focus();
    }

    private void RotateMode_Click(object sender, RoutedEventArgs e)
    {
        RenderContext.TransformWidget.Mode = EWidgetMode.Rotate;
        SceneViewer.MarkRenderDirty();
        SceneViewer.Focus();
    }

    private void CameraPositionAdjustButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string tag }) return;

        string[] parts = tag.Split(',');
        if (parts.Length != 2 || !float.TryParse(parts[1], out float direction)) return;

        Vector3 position = RenderContext.Camera.Position;
        if (float.TryParse(CameraPositionX, out float x)) position.X = x;
        if (float.TryParse(CameraPositionY, out float y)) position.Y = y;
        if (float.TryParse(CameraPositionZ, out float z)) position.Z = z;

        float delta = CameraPositionStep * direction;
        switch (parts[0])
        {
            case "X":
                position.X += delta;
                break;
            case "Y":
                position.Y += delta;
                break;
            case "Z":
                position.Z += delta;
                break;
            default:
                return;
        }

        CameraPositionX = position.X.ToString("0.##");
        CameraPositionY = position.Y.ToString("0.##");
        CameraPositionZ = position.Z.ToString("0.##");
        MoveCameraToEnteredPosition();
    }

    private void CameraRotationAdjustButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string tag }) return;

        string[] parts = tag.Split(',');
        if (parts.Length != 2 || !float.TryParse(parts[1], out float direction)) return;

        float x = MathUtil.RadiansToDegrees(RenderContext.Camera.Roll);
        float y = MathUtil.RadiansToDegrees(RenderContext.Camera.Pitch);
        float z = MathUtil.RadiansToDegrees(RenderContext.Camera.Yaw);
        if (float.TryParse(CameraRotationX, out float enteredX)) x = enteredX;
        if (float.TryParse(CameraRotationY, out float enteredY)) y = enteredY;
        if (float.TryParse(CameraRotationZ, out float enteredZ)) z = enteredZ;

        float delta = CameraRotationStep * direction;
        switch (parts[0])
        {
            case "X":
                x += delta;
                break;
            case "Y":
                y += delta;
                break;
            case "Z":
                z += delta;
                break;
            default:
                return;
        }

        CameraRotationX = x.ToString("0.##");
        CameraRotationY = y.ToString("0.##");
        CameraRotationZ = z.ToString("0.##");
        MoveCameraToEnteredRotation();
    }

    private bool AreCameraPositionBoxesFocused() => cameraPositionEditorsFocused > 0;

    private bool AreCameraRotationBoxesFocused() => cameraRotationEditorsFocused > 0;

    private void UpdateCameraPositionText()
    {
        if (AreCameraPositionBoxesFocused()) return;

        updatingCameraPositionText = true;
        try
        {
            Vector3 position = RenderContext.Camera.Position;
            CameraPositionX = position.X.ToString("0.##");
            CameraPositionY = position.Y.ToString("0.##");
            CameraPositionZ = position.Z.ToString("0.##");
        }
        finally
        {
            updatingCameraPositionText = false;
        }
    }

    private void UpdateCameraRotationText()
    {
        if (AreCameraRotationBoxesFocused()) return;

        updatingCameraRotationText = true;
        try
        {
            CameraRotationX = MathUtil.RadiansToDegrees(RenderContext.Camera.Roll).ToString("0.##");
            CameraRotationY = MathUtil.RadiansToDegrees(RenderContext.Camera.Pitch).ToString("0.##");
            CameraRotationZ = MathUtil.RadiansToDegrees(RenderContext.Camera.Yaw).ToString("0.##");
        }
        finally
        {
            updatingCameraRotationText = false;
        }
    }

    private void CameraPositionBoxes_KeyUp(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Return)
        {
            MoveCameraToEnteredPosition();
            e.Handled = true;
        }
    }

    private void CameraPositionBoxes_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        cameraPositionEditorsFocused++;
    }

    private void CameraPositionBoxes_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        cameraPositionEditorsFocused = Math.Max(0, cameraPositionEditorsFocused - 1);
        if (updatingCameraPositionText) return;

        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            if (!updatingCameraPositionText && !AreCameraPositionBoxesFocused())
            {
                MoveCameraToEnteredPosition();
            }
        }));
    }

    private void MoveCameraToEnteredPosition()
    {
        if (!float.TryParse(CameraPositionX, out float x)
            || !float.TryParse(CameraPositionY, out float y)
            || !float.TryParse(CameraPositionZ, out float z))
        {
            UpdateCameraPositionText();
            return;
        }

        RenderContext.Camera.Position = new Vector3(x, y, z);
        UpdateCameraPositionText();
        UpdateCameraRotationText();
        SceneViewer?.MarkRenderDirty();
        SceneViewer?.Focus();
    }

    private void CameraRotationBoxes_KeyUp(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Return)
        {
            MoveCameraToEnteredRotation();
            e.Handled = true;
        }
    }

    private void CameraRotationBoxes_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        cameraRotationEditorsFocused++;
    }

    private void CameraRotationBoxes_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        cameraRotationEditorsFocused = Math.Max(0, cameraRotationEditorsFocused - 1);
        if (updatingCameraRotationText) return;

        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            if (!updatingCameraRotationText && !AreCameraRotationBoxesFocused())
            {
                MoveCameraToEnteredRotation();
            }
        }));
    }

    private void MoveCameraToEnteredRotation()
    {
        if (!float.TryParse(CameraRotationX, out float x)
            || !float.TryParse(CameraRotationY, out float y)
            || !float.TryParse(CameraRotationZ, out float z))
        {
            UpdateCameraRotationText();
            return;
        }

        RenderContext.Camera.Roll = MathUtil.DegreesToRadians(x);
        RenderContext.Camera.Pitch = MathUtil.Clamp(MathUtil.DegreesToRadians(y), -MathF.PI / 2 + 0.01f, MathF.PI / 2 - 0.01f);
        RenderContext.Camera.Yaw = MathUtil.DegreesToRadians(z);
        UpdateCameraRotationText();
        SceneViewer?.MarkRenderDirty();
        SceneViewer?.Focus();
    }

    private void CoordinateEditor_GotFocus(object sender, RoutedEventArgs e)
    {
        if (Keyboard.PrimaryDevice.IsKeyDown(Key.Tab) && sender is TextBox textBox)
        {
            textBox.SelectAll();
        }
    }

    private void KeyframeList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (KeyframeList.SelectedItem is CurveEditor3DKeyframe keyframe && keyframe != SelectedKeyframe)
        {
            SelectedKeyframe = keyframe;
        }
    }

    private void AddKeyframe_Click(object sender, RoutedEventArgs e)
    {
        StopPlayback();
        if (SelectedKeyframe is not { } keyframe)
        {
            return;
        }

        AddKeyframePlacement? placement = ShowAddKeyframePlacementDialog(Window.GetWindow(this), keyframe);
        if (placement is null)
        {
            return;
        }

        bool addAfter = placement == AddKeyframePlacement.After;
        CurveEditor3DKeyframe newKeyframe = model.AddKeyframe(keyframe, addAfter);
        if (newKeyframe is null)
        {
            return;
        }

        RenderContext.AddHitProxy(newKeyframe);
        SelectedKeyframe = newKeyframe;
        SceneStatus = $"Added keyframe at InVal {newKeyframe.Time:0.###}; {model.Keyframes.Count} trajectory keyframe(s).";
        RefreshKeyframePanel();
        SceneViewer.MarkRenderDirty();
    }

    private static AddKeyframePlacement? ShowAddKeyframePlacementDialog(Window owner, CurveEditor3DKeyframe keyframe)
    {
        AddKeyframePlacement? placement = null;
        var dialog = new Window
        {
            Title = "Add Keyframe",
            SizeToContent = SizeToContent.WidthAndHeight,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner,
            Content = new StackPanel
            {
                Margin = new Thickness(16),
                MinWidth = 300,
                Children =
                {
                    new TextBlock
                    {
                        Text = $"Add a new keyframe relative to InVal {keyframe.Time:0.###}.",
                        TextWrapping = TextWrapping.Wrap,
                        Margin = new Thickness(0, 0, 0, 12)
                    },
                    new Button
                    {
                        Content = "Before selected keyframe",
                        Margin = new Thickness(0, 0, 0, 6),
                        Padding = new Thickness(10, 4, 10, 4),
                        IsDefault = true,
                        Tag = AddKeyframePlacement.Before
                    },
                    new Button
                    {
                        Content = "After selected keyframe",
                        Margin = new Thickness(0, 0, 0, 12),
                        Padding = new Thickness(10, 4, 10, 4),
                        Tag = AddKeyframePlacement.After
                    },
                    new Button
                    {
                        Content = "Cancel",
                        Padding = new Thickness(10, 4, 10, 4),
                        IsCancel = true
                    }
                }
            }
        };

        if (owner is not null)
        {
            dialog.Owner = owner;
        }

        if (dialog.Content is Panel panel)
        {
            foreach (Button button in panel.Children.OfType<Button>().Where(button => button.Tag is AddKeyframePlacement))
            {
                button.Click += (_, _) =>
                {
                    placement = (AddKeyframePlacement)button.Tag;
                    dialog.DialogResult = true;
                };
            }
        }

        return dialog.ShowDialog() == true ? placement : null;
    }

    private void SnapSelectedKeyframeToViewport_Click(object sender, RoutedEventArgs e)
    {
        StopPlayback();
        if (SelectedKeyframe is not { } keyframe)
        {
            return;
        }

        keyframe.Location = pendingViewportSelectedKeyframeLocation;
        SceneStatus = $"Snapped keyframe at InVal {keyframe.Time:0.###} to the viewport cursor.";
    }

    private void AddKeyframeAfterLast_Click(object sender, RoutedEventArgs e)
    {
        StopPlayback();
        CurveEditor3DKeyframe newKeyframe = model.AddKeyframeAfterLast(pendingViewportKeyframeLocation);
        if (newKeyframe is null)
        {
            return;
        }

        RenderContext.AddHitProxy(newKeyframe);
        SelectedKeyframe = newKeyframe;
        SceneStatus = $"Added keyframe at InVal {newKeyframe.Time:0.###}; {model.Keyframes.Count} trajectory keyframe(s).";
        RefreshKeyframePanel();
        SceneViewer.MarkRenderDirty();
    }

    private void DeleteKeyframe_Click(object sender, RoutedEventArgs e)
    {
        StopPlayback();
        if (SelectedKeyframe is not { } keyframe)
        {
            return;
        }

        RenderContext.RemoveHitProxy(keyframe);
        SelectedKeyframe = model.DeleteKeyframe(keyframe);
        SceneStatus = $"{model.Keyframes.Count} trajectory keyframe(s); {levelPaths.Count} level backdrop file(s).";
        RefreshKeyframePanel();
        SceneViewer.MarkRenderDirty();
    }

    private void ShiftInterpTrack_Click(object sender, RoutedEventArgs e)
    {
        StopPlayback();
        if (CurrentLoadedExport is null)
        {
            return;
        }

        var dialog = new ShiftInterpTrackDialog
        {
            Owner = Window.GetWindow(this)
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        float selectedTime = SelectedKeyframe?.Time ?? 0f;
        PackageEditorExperimentsM.ShiftInterpTrackMove(CurrentLoadedExport, dialog.Parameters);
        ReloadCurrentExport(selectedTime + dialog.Parameters.TimeOffset);
        SceneStatus = $"Shifted {model.Keyframes.Count} trajectory keyframe(s); {levelPaths.Count} level backdrop file(s).";
    }

    private void ApplyPosTrackInterpModeToAll_Click(object sender, RoutedEventArgs e)
    {
        StopPlayback();
        if (SelectedKeyframe is not { } keyframe || model.Keyframes.Count == 0)
        {
            return;
        }

        model.SetAllPosTrackInterpModes(keyframe.PosTrackInterpMode);
        RefreshKeyframePanel();
        trajectorySamplesDirty = true;
        SceneStatus = $"Set PosTrack InterpMode to {keyframe.PosTrackInterpMode} for {model.Keyframes.Count} keyframe(s).";
        SceneViewer.MarkRenderDirty();
    }

    private void ApplyEulerTrackInterpModeToAll_Click(object sender, RoutedEventArgs e)
    {
        StopPlayback();
        if (SelectedKeyframe is not { } keyframe || model.Keyframes.Count == 0)
        {
            return;
        }

        model.SetAllEulerTrackInterpModes(keyframe.EulerTrackInterpMode);
        RefreshKeyframePanel();
        trajectorySamplesDirty = true;
        SceneStatus = $"Set EulerTrack InterpMode to {keyframe.EulerTrackInterpMode} for {model.Keyframes.Count} keyframe(s).";
        SceneViewer.MarkRenderDirty();
    }

    private void ReloadCurrentExport(float preferredSelectionTime)
    {
        UnregisterKeyframes();
        model.Load(CurrentLoadedExport);
        trajectorySamplesDirty = true;
        KeyframeList.ItemsSource = model.Keyframes;
        RegisterKeyframes();
        SelectedKeyframe = model.Keyframes.Count == 0
            ? null
            : model.Keyframes.MinBy(keyframe => MathF.Abs(keyframe.Time - preferredSelectionTime));
        RefreshKeyframePanel();
        SceneViewer.MarkRenderDirty();
    }

    private void KeyframeList_PreviewMouseRightButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (ItemsControl.ContainerFromElement(KeyframeList, (DependencyObject)e.OriginalSource) is ListBoxItem { DataContext: CurveEditor3DKeyframe keyframe })
        {
            SelectedKeyframe = keyframe;
        }
    }

    private void ShowKeyframeContextMenu(FrameworkElement placementTarget)
    {
        ContextMenu menu = CreateKeyframeContextMenu();
        menu.PlacementTarget = placementTarget;
        menu.IsOpen = true;
    }

    private void ShowViewportContextMenu(FrameworkElement placementTarget)
    {
        Point viewportPoint = Mouse.GetPosition(SceneViewer);
        pendingViewportKeyframeLocation = GetViewportKeyframeLocation(viewportPoint);
        if (SelectedKeyframe is { } selectedKeyframe)
        {
            pendingViewportSelectedKeyframeLocation = GetViewportKeyframeLocation(viewportPoint, selectedKeyframe.Location);
        }

        var menu = new ContextMenu { PlacementTarget = placementTarget };
        var snapItem = new MenuItem
        {
            Header = "Snap Selected Keyframe Here",
            IsEnabled = SelectedKeyframe is not null
        };
        snapItem.Click += SnapSelectedKeyframeToViewport_Click;
        menu.Items.Add(snapItem);
        menu.Items.Add(new Separator());

        var addItem = new MenuItem
        {
            Header = "Add Keyframe After Last",
            IsEnabled = model.Keyframes.Count > 0
        };
        addItem.Click += AddKeyframeAfterLast_Click;
        menu.Items.Add(addItem);
        menu.IsOpen = true;
    }

    private Vector3 GetViewportKeyframeLocation(Point viewportPoint, Vector3? depthReference = null)
    {
        if (depthReference is null && model.Keyframes.Count == 0)
        {
            return RenderContext.Camera.Position + RenderContext.Camera.CameraForward * 100f;
        }

        Vector3 referenceLocation = depthReference ?? model.Keyframes[^1].Location;
        float width = MathF.Max(RenderContext.Width, 1f);
        float height = MathF.Max(RenderContext.Height, 1f);
        float normalizedX = ((float)viewportPoint.X / width * 2f) - 1f;
        float normalizedY = 1f - ((float)viewportPoint.Y / height * 2f);
        Vector3 forward = RenderContext.Camera.CameraForward;
        Vector3 right = RenderContext.Camera.CameraRight;
        Vector3 up = RenderContext.Camera.CameraUp;
        Vector3 cameraPosition = RenderContext.Camera.Position;

        if (RenderContext.Camera.IsOrthographic)
        {
            return cameraPosition
                   + (right * (normalizedX * RenderContext.Camera.OrthoWidth * 0.5f))
                   + (up * (normalizedY * RenderContext.Camera.OrthoWidth / MathF.Max(RenderContext.Camera.aspect, float.Epsilon) * 0.5f))
                   + (forward * Vector3.Dot(referenceLocation - cameraPosition, forward));
        }

        float halfHeightAtUnitDepth = MathF.Tan(RenderContext.Camera.FOV * 0.5f);
        Vector3 rayDirection = Vector3.Normalize(forward + right * normalizedX * halfHeightAtUnitDepth * RenderContext.Camera.aspect + up * normalizedY * halfHeightAtUnitDepth);
        float denominator = Vector3.Dot(rayDirection, forward);
        if (MathF.Abs(denominator) < 0.0001f)
        {
            return referenceLocation;
        }

        float distance = Vector3.Dot(referenceLocation - cameraPosition, forward) / denominator;
        if (distance <= 0f || float.IsNaN(distance) || float.IsInfinity(distance))
        {
            return referenceLocation;
        }

        return cameraPosition + rayDirection * distance;
    }

    private void RefreshKeyframePanel()
    {
        KeyframeList?.Items.Refresh();
        OnPropertyChanged(nameof(SelectedKeyframe));
        KeyframeList.SelectedItem = SelectedKeyframe;
        if (SelectedKeyframe is not null)
        {
            KeyframeList.ScrollIntoView(SelectedKeyframe);
        }
    }

    private void SnapCameraToKey_Click(object sender, RoutedEventArgs e)
    {
        StopPlayback();
        if (SelectedKeyframe is not { } keyframe)
        {
            return;
        }

        SnapCameraToKey(keyframe);
    }

    private void SnapCameraToKey(CurveEditor3DKeyframe keyframe)
    {
        const float degreesToRadians = 0.017453292519943295f;
        RenderContext.Camera.Position = keyframe.Location;
        RenderContext.Camera.Roll = keyframe.Roll * degreesToRadians;
        RenderContext.Camera.Pitch = keyframe.Pitch * degreesToRadians;
        RenderContext.Camera.Yaw = keyframe.Yaw * degreesToRadians;
        RenderContext.Camera.FocusDepth = 0f;
        UpdateCameraPositionText();
        UpdateCameraRotationText();
        SceneViewer.MarkRenderDirty();
        SceneViewer.Focus();
    }

    private void PlayMoveButton_Loaded(object sender, RoutedEventArgs e)
    {
        playMoveButton = (Button)sender;
        UpdatePlaybackButton();
    }

    private void PlayMove_Click(object sender, RoutedEventArgs e)
    {
        if (isPlayingMove)
        {
            StopPlayback();
            return;
        }

        if (model.Keyframes.Count == 0)
        {
            return;
        }

        playbackStartTime = model.Keyframes[0].Time;
        playbackEndTime = model.Keyframes[^1].Time;
        if (playbackEndTime <= playbackStartTime)
        {
            ApplyCameraAtTime(playbackStartTime);
            return;
        }

        if (playMoveButton is not null)
        {
            playMoveButton.Content = "Stop";
        }
        RenderContext.TransformWidget.Attach = null;
        playbackElapsed = 0f;
        isPlayingMove = true;
        RenderContext.ForceContinuousRendering = true;
        ApplyCameraAtTime(playbackStartTime);
        SceneViewer.Focus();
    }

    private void UpdatePlayback(object sender, float deltaTime)
    {
        if (!isPlayingMove)
        {
            return;
        }

        playbackElapsed += deltaTime;
        float time = playbackStartTime + playbackElapsed;
        if (time >= playbackEndTime)
        {
            ApplyCameraAtTime(playbackEndTime);
            StopPlayback();
            return;
        }

        ApplyCameraAtTime(time);
    }

    private void ApplyCameraAtTime(float time)
    {
        Vector3 location = model.PositionTrack?.Eval(time, Vector3.Zero) ?? Vector3.Zero;
        Vector3 rotation = model.RotationTrack?.Eval(time, Vector3.Zero) ?? Vector3.Zero;
        const float degreesToRadians = 0.017453292519943295f;
        RenderContext.Camera.Position = location;
        RenderContext.Camera.Roll = rotation.X * degreesToRadians;
        RenderContext.Camera.Pitch = rotation.Y * degreesToRadians;
        RenderContext.Camera.Yaw = rotation.Z * degreesToRadians;
        RenderContext.Camera.FocusDepth = 0f;
        PlaybackKeyframeStatus = GetPlaybackKeyframeStatus(time);
        SceneStatus = $"Playing camera at InVal {time:0.###} / {playbackEndTime:0.###}; {levelPaths.Count} level backdrop file(s).";
        UpdateCameraPositionText();
        UpdateCameraRotationText();
        SceneViewer.MarkRenderDirty();
    }

    private string GetPlaybackKeyframeStatus(float time)
    {
        int keyframeCount = model.Keyframes.Count;
        if (keyframeCount == 0)
        {
            return "Not playing";
        }

        int currentIndex = 0;
        for (int i = 1; i < keyframeCount; i++)
        {
            if (model.Keyframes[i].Time > time)
            {
                break;
            }

            currentIndex = i;
        }

        CurveEditor3DKeyframe currentKeyframe = model.Keyframes[currentIndex];
        return $"Keyframe {currentIndex + 1} of {keyframeCount} (InVal {currentKeyframe.Time:0.###})";
    }

    private void StopPlayback(bool restoreStatus = true)
    {
        if (!isPlayingMove)
        {
            return;
        }

        isPlayingMove = false;
        playbackElapsed = 0f;
        PlaybackKeyframeStatus = "Not playing";
        RenderContext.ForceContinuousRendering = false;
        if (playMoveButton is not null)
        {
            playMoveButton.Content = "Play";
        }
        RenderContext.TransformWidget.Attach = SelectedKeyframe;
        if (restoreStatus)
        {
            SceneStatus = $"{model.Keyframes.Count} trajectory keyframe(s); {levelPaths.Count} level backdrop file(s).";
        }
        SceneViewer?.MarkRenderDirty();
    }

    private void UpdatePlaybackButton()
    {
        if (playMoveButton is null)
        {
            return;
        }

        playMoveButton.IsEnabled = model.Keyframes.Count > 0;
        if (!isPlayingMove)
        {
            playMoveButton.Content = "Play";
        }
    }

    private void Model_Changed()
    {
        StopPlayback();
        UpdatePlaybackButton();
        trajectorySamplesDirty = true;
        RefreshKeyframePanel();
        SceneViewer?.MarkRenderDirty();
    }

    private void RenderScene(object sender, EventArgs e)
    {
        foreach (RenderPass pass in RenderPasses)
        {
            foreach (ActorProxy actor in RenderContext.DrawList_3D)
            {
                if (actor.IsVolume && !ShowVolumes) continue;
                if (actor.IsVolumetricMesh && !ShowVolumetrics) continue;
                int hitId = actor.HitID;
                RenderContext.CurrentHitTestId = new Vector3((hitId & 0xFF) / 255f, ((hitId >> 8) & 0xFF) / 255f, ((hitId >> 16) & 0xFF) / 255f);
                actor.Render(RenderContext, pass);
            }
        }

        if (!isPlayingMove)
        {
            DrawTrajectory();
        }
        RenderContext.DrawUI();
    }

    private void DrawTrajectory()
    {
        IReadOnlyList<Vector3> samples = GetTrajectorySamples();
        Vector4 pathColor = new(1f, 0.65f, 0.05f, 1f);
        for (int i = 1; i < samples.Count; i++)
        {
            RenderContext.Primitives.AddLine(samples[i - 1], samples[i], pathColor, 0);
        }

        Vector4 connectorColor = new(1f, 0.85f, 0.2f, 1f);
        for (int i = 1; i < model.Keyframes.Count; i++)
        {
            RenderContext.Primitives.AddLine(model.Keyframes[i - 1].Location, model.Keyframes[i].Location, connectorColor, 0);
        }

        foreach (CurveEditor3DKeyframe keyframe in model.Keyframes)
        {
            DrawKeyframe(keyframe);
        }
    }

    private IReadOnlyList<Vector3> GetTrajectorySamples()
    {
        if (trajectorySamplesDirty)
        {
            trajectorySamples = model.SampleTrajectory();
            trajectorySamplesDirty = false;
        }

        return trajectorySamples;
    }

    private void DrawKeyframe(CurveEditor3DKeyframe keyframe)
    {
        const float cubeHalfSize = 22f;
        const float axisLength = 55f;
        Vector3 position = keyframe.Location;
        Vector4 markerColor = keyframe == SelectedKeyframe ? new Vector4(1f, 1f, 1f, 1f) : new Vector4(1f, 0.8f, 0.1f, 1f);
        Quaternion orientation = Rotator.FromDegreesVector(keyframe.Rotation).ToQuaternion();
        Matrix4x4 cubeTransform = Matrix4x4.CreateFromQuaternion(orientation) * Matrix4x4.CreateTranslation(position);
        var cube = RenderContext.Primitives.BuildMesh(markerColor, keyframe.HitID, cubeTransform);
        cube.AddVertex(-cubeHalfSize, -cubeHalfSize, -cubeHalfSize);
        cube.AddVertex(cubeHalfSize, -cubeHalfSize, -cubeHalfSize);
        cube.AddVertex(cubeHalfSize, cubeHalfSize, -cubeHalfSize);
        cube.AddVertex(-cubeHalfSize, cubeHalfSize, -cubeHalfSize);
        cube.AddVertex(-cubeHalfSize, -cubeHalfSize, cubeHalfSize);
        cube.AddVertex(cubeHalfSize, -cubeHalfSize, cubeHalfSize);
        cube.AddVertex(cubeHalfSize, cubeHalfSize, cubeHalfSize);
        cube.AddVertex(-cubeHalfSize, cubeHalfSize, cubeHalfSize);
        cube.AddTriangle(0, 2, 1);
        cube.AddTriangle(0, 3, 2);
        cube.AddTriangle(4, 5, 6);
        cube.AddTriangle(4, 6, 7);
        cube.AddTriangle(0, 1, 5);
        cube.AddTriangle(0, 5, 4);
        cube.AddTriangle(1, 2, 6);
        cube.AddTriangle(1, 6, 5);
        cube.AddTriangle(2, 3, 7);
        cube.AddTriangle(2, 7, 6);
        cube.AddTriangle(3, 0, 4);
        cube.AddTriangle(3, 4, 7);

        Vector3 forward = Vector3.Transform(Vector3.UnitX, orientation);
        Vector3 right = Vector3.Transform(Vector3.UnitY, orientation);
        Vector3 up = Vector3.Transform(Vector3.UnitZ, orientation);
        RenderContext.Primitives.AddLine(position, position + forward * axisLength, new Vector4(1f, 0.15f, 0.15f, 1f), keyframe.HitID);
        RenderContext.Primitives.AddLine(position, position + right * axisLength, new Vector4(0.15f, 1f, 0.15f, 1f), keyframe.HitID);
        RenderContext.Primitives.AddLine(position, position + up * axisLength, new Vector4(0.2f, 0.45f, 1f, 1f), keyframe.HitID);
    }

    private void RegisterKeyframes()
    {
        foreach (CurveEditor3DKeyframe keyframe in model.Keyframes)
        {
            RenderContext.AddHitProxy(keyframe);
        }
    }

    private void UnregisterKeyframes()
    {
        foreach (CurveEditor3DKeyframe keyframe in model.Keyframes)
        {
            RenderContext.RemoveHitProxy(keyframe);
        }
    }

    private async void OpenLevel_Click(object sender, RoutedEventArgs e)
    {
        var dialog = AppDirectories.GetOpenPackageDialog();
        if (dialog.ShowDialog() == true)
        {
            await LoadLevelAsync(dialog.FileName, replace: true).ConfigureAwait(true);
        }
    }

    private async void AddLevel_Click(object sender, RoutedEventArgs e)
    {
        var dialog = AppDirectories.GetOpenPackageDialog();
        if (dialog.ShowDialog() == true)
        {
            await LoadLevelAsync(dialog.FileName, replace: false).ConfigureAwait(true);
        }
    }

    private void UnloadLevel_Click(object sender, RoutedEventArgs e)
    {
        CloseLevels();
        SceneStatus = $"{model.Keyframes.Count} trajectory keyframe(s); {levelPaths.Count} level backdrop file(s).";
    }

    private void RecentLevelsButton_Click(object sender, RoutedEventArgs e)
    {
        RecentLevelsMenu.PlacementTarget = RecentLevelsButton;
        RecentLevelsMenu.IsOpen = true;
    }

    private void RecentLevelsMenu_Opened(object sender, RoutedEventArgs e)
    {
        RecentLevelsMenu.Items.Clear();
        List<RecentFileSet> recentSets = LoadRecentSets();
        if (recentSets.Count == 0)
        {
            RecentLevelsMenu.Items.Add(new MenuItem { Header = "No recent levels", IsEnabled = false });
            return;
        }

        foreach (RecentFileSet set in recentSets)
        {
            var item = new MenuItem { Header = set.DisplayName.Replace("_", "__"), ToolTip = set.TooltipText, Tag = set };
            item.Click += RecentLevel_Click;
            RecentLevelsMenu.Items.Add(item);
        }
    }

    private async void RecentLevel_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: RecentFileSet set })
        {
            return;
        }

        List<string> existingPaths = set.FilePaths.Where(File.Exists).ToList();
        if (existingPaths.Count == 0)
        {
            MessageBox.Show("None of the recent level files exist anymore.");
            return;
        }

        for (int i = 0; i < existingPaths.Count; i++)
        {
            await LoadLevelAsync(existingPaths[i], replace: i == 0).ConfigureAwait(true);
        }
    }

    private async Task LoadLevelAsync(string path, bool replace)
    {
        try
        {
            if (replace)
            {
                CloseLevels();
            }

            path = Path.GetFullPath(path);
            if (levelPaths.Contains(path, StringComparer.OrdinalIgnoreCase))
            {
                return;
            }

            SceneStatus = $"Loading {Path.GetFileName(path)}...";
            await Task.Delay(1).ConfigureAwait(true);
            IMEPackage package = MEPackageHandler.OpenMEPackage(path);
            ExportEntry levelExport = package.Exports.FirstOrDefault(export => export.ClassName == "Level");
            if (levelExport is null)
            {
                package.Dispose();
                MessageBox.Show($"{Path.GetFileName(path)} is not a level file.");
                return;
            }

            Level level = levelExport.GetBinaryData<Level>();
            List<ActorProxy> actors = LoadActors(level);
            levelPackages.Add(package);
            levelPaths.Add(path);
            levelActors.AddRange(actors);
            RenderContext.LoadActors(actors);
            RecordRecentSet();
            SceneStatus = $"{model.Keyframes.Count} trajectory keyframe(s); {levelPaths.Count} level backdrop file(s).";
            SceneViewer.SetShouldRender(true);
            SceneViewer.MarkRenderDirty();
        }
        catch (Exception exception)
        {
            SceneStatus = $"Failed to load {Path.GetFileName(path)}.";
            MessageBox.Show($"Unable to open level file:\n{exception.Message}");
        }
    }

    private List<ActorProxy> LoadActors(Level level)
    {
        var actors = new List<ActorProxy>();
        IEnumerable<ExportEntry> actorExports = level.Actors.Where(level.Export.FileRef.IsUExport).Select(level.Export.FileRef.GetUExport);
        foreach (ExportEntry actorExport in actorExports)
        {
            if (actorExport.ClassName == "StaticMeshCollectionActor")
            {
                var collection = actorExport.GetBinaryData<StaticMeshCollectionActor>();
                for (int index = 0; index < collection.Components.Count; index++)
                {
                    if (level.Export.FileRef.TryGetUExport(collection.Components[index], out ExportEntry component))
                    {
                        actors.Add(new StaticMeshComponentActorProxy(this, component, collection, index));
                    }
                }
            }
            else if (actorExport.ClassName == "StaticLightCollectionActor")
            {
                // Light-component actor proxies are not ported into this branch yet.
                // The level backdrop renders static meshes only; light actors are skipped.
                continue;
            }
            else if (ActorProxy.Create(this, actorExport) is { } actor)
            {
                actors.Add(actor);
            }
        }

        foreach (ActorProxy actor in actors)
        {
            actor.ResolveAttachment(actors);
        }

        return actors.OrderBy(actor => actor.Export.UIndex).ToList();
    }

    private void CloseLevels()
    {
        RenderContext.UnloadLevel();
        RenderContext.EnableTransformWidget();
        levelActors.Clear();
        foreach (IMEPackage package in levelPackages)
        {
            package.Dispose();
        }
        levelPackages.Clear();
        levelPaths.Clear();

        foreach (CurveEditor3DKeyframe keyframe in model.Keyframes)
        {
            keyframe.HitID = 0;
        }
        RegisterKeyframes();
        SceneViewer?.MarkRenderDirty();
    }

    private static string RecentSetsFile => Path.Combine(
        Directory.CreateDirectory(Path.Combine(AppDirectories.AppDataFolder, "LevelEditor")).FullName,
        "RECENTSETS");

    private static List<RecentFileSet> LoadRecentSets()
    {
        if (!File.Exists(RecentSetsFile))
        {
            return [];
        }

        try
        {
            List<RecentFileSet> sets = JsonConvert.DeserializeObject<List<RecentFileSet>>(File.ReadAllText(RecentSetsFile)) ?? [];
            foreach (RecentFileSet set in sets)
            {
                set.FilePaths.RemoveAll(path => !File.Exists(path));
                set.ReadOnlyFilePaths.RemoveAll(path => !File.Exists(path));
            }
            return sets.Where(set => set.FilePaths.Count > 0).ToList();
        }
        catch
        {
            return [];
        }
    }

    private void RecordRecentSet()
    {
        if (levelPaths.Count == 0)
        {
            return;
        }

        List<RecentFileSet> sets = LoadRecentSets();
        sets.RemoveAll(set => set.FilePaths.Count > 0 && set.FilePaths[0].Equals(levelPaths[0], StringComparison.OrdinalIgnoreCase));
        sets.Insert(0, new RecentFileSet
        {
            Game = levelPackages[0].Game,
            FilePaths = [.. levelPaths],
            ReadOnlyFilePaths = []
        });
        if (sets.Count > 10)
        {
            sets.RemoveRange(10, sets.Count - 10);
        }
        File.WriteAllText(RecentSetsFile, JsonConvert.SerializeObject(sets, Formatting.Indented));
    }
}
