using LegendaryExplorer.Dialogs;
using LegendaryExplorer.Misc;
using LegendaryExplorer.SharedUI;
using LegendaryExplorer.SharedUI.Bases;
using LegendaryExplorer.Tools.LevelEditor.Scene3D;
using LegendaryExplorerCore.GameFilesystem;
using LegendaryExplorerCore.Helpers;
using LegendaryExplorerCore.Misc;
using LegendaryExplorerCore.Packages;
using LegendaryExplorerCore.SharpDX;
using LegendaryExplorerCore.Unreal.BinaryConverters;
using Microsoft.Win32;
using LegendaryExplorer.Tools.PackageEditor;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Threading;

namespace LegendaryExplorer.Tools.LevelEditor;

/// <summary>
/// Interaction logic for LevelEditor.xaml
/// </summary>
public class RecentFileSet
{
    public MEGame Game { get; set; }
    public List<string> FilePaths { get; set; } = [];
    public List<string> ReadOnlyFilePaths { get; set; } = [];
    public RecentViewState ViewState { get; set; }

    [JsonIgnore]
    public string DisplayName => FilePaths.Count switch
    {
        0 => "(empty)",
        1 => Path.GetFileName(FilePaths[0]),
        _ => $"{Path.GetFileName(FilePaths[0])} (+{FilePaths.Count - 1} more)"
    };

    [JsonIgnore]
    public string TooltipText => string.Join("\n", FilePaths.Select(Path.GetFileName));
}

public class RecentViewState
{
    public float CameraX { get; set; }
    public float CameraY { get; set; }
    public float CameraZ { get; set; }
    public float CameraYaw { get; set; }
    public float CameraPitch { get; set; }
    public float CameraOrthoWidth { get; set; }
    public bool IsOrthographicView { get; set; }

    public ObjectRenderMode ObjectRenderMode { get; set; } = ObjectRenderMode.Full;
    public bool UseVisibleSetOnly { get; set; }
    public bool HasUserEditedVisibleSets { get; set; }
    public int VisibleSetDistance { get; set; } = 5000;
    public List<string> VisibleActorKeys { get; set; } = [];
    public List<string> HiddenActorClasses { get; set; }

    public bool ShowLights { get; set; } = true;
    public int LightRenderDistance { get; set; } = 1000;
    public bool ShowVolumes { get; set; }
    public bool ShowVolumetrics { get; set; }
    public bool ShowEmitters { get; set; }
    public bool ShowLocationActors { get; set; }
    public bool ShowSoundPositions { get; set; }
    public bool ShowCinematicActors { get; set; }
    public bool ShowDecalActors { get; set; }
    public bool ShowStageNodes { get; set; } = true;
    public bool ShowStageCameras { get; set; } = true;
    public bool ShowCollision { get; set; }
}

public enum ObjectRenderMode
{
    Full,
    Wireframe,
    Hidden,
    VisibleSetOnly
}

public sealed class ActorTransformGroup
{
    public string Name { get; }
    public ActorProxy LeadActor { get; }
    public IReadOnlyList<ActorProxy> Members { get; }

    public ActorTransformGroup(string name, ActorProxy leadActor, IReadOnlyList<ActorProxy> members)
    {
        Name = name;
        LeadActor = leadActor;
        Members = members;
    }
}

public partial class LevelEditor : NotifyPropertyChangedWindowBase, IActorEditorContext
{
    private static readonly Regex CoordinatePasteRegex = new(@"([XYZ])\s*=\s*(-?\d+(?:[\.,]\d+)?)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static Vector3? copiedCoordinates;
    private static Rotator? copiedRotation;

    public LevelEditorRenderContext RenderContext { get; }

    public ObservableCollectionExtended<OpenLevelFile> OpenFiles { get; } = [];
    public ObservableCollectionExtended<ActorProxy> Actors { get; } = [];
    public ICollectionView ActorsView { get; }
    private string _actorFilterText = "";
    private int _nextFileLoadOrder;

    private bool _hasAnyFileOpen;
    public bool HasAnyFileOpen
    {
        get => _hasAnyFileOpen;
        private set
        {
            if (SetProperty(ref _hasAnyFileOpen, value))
            {
                OnPropertyChanged(nameof(CanGroupSelectedActors));
                OnPropertyChanged(nameof(CanReselectGroup));
            }
        }
    }

    private MEGame _game = MEGame.Unknown;
    public MEGame Game
    {
        get => _game;
        private set => SetProperty(ref _game, value);
    }

    private ActorProxy selectedActor;
    private bool _suppressSelectionFocus;
    private int _selectionFocusSuppressionDepth;
    private bool _isApplyingGroupMove;
    private bool _isUpdatingGroupSelection;
    private int _groupableSelectionCount;
    private ActorTransformGroup _activeTransformGroup;
    private BioStageOverlayMarker _selectedBioStageMarker;
    private bool _isBioStageMarkersExpanded;

    public ActorTransformGroup ActiveTransformGroup
    {
        get => _activeTransformGroup;
        private set
        {
            if (SetProperty(ref _activeTransformGroup, value))
            {
                OnPropertyChanged(nameof(HasActiveTransformGroup));
                OnPropertyChanged(nameof(ActiveTransformGroupSummary));
                OnPropertyChanged(nameof(CanReselectGroup));
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public bool HasActiveTransformGroup => ActiveTransformGroup is not null;
    public bool CanReselectGroup => PackageIsLoaded() && HasActiveTransformGroup;
    public bool CanGroupSelectedActors => PackageIsLoaded() && _groupableSelectionCount >= 2;

    public string ActiveTransformGroupSummary
    {
        get
        {
            if (ActiveTransformGroup is null)
            {
                return "No active group";
            }

            string header = $"{ActiveTransformGroup.Name}: {ActiveTransformGroup.Members.Count} actors (Lead: [{ActiveTransformGroup.LeadActor.Export.UIndex}] {ActiveTransformGroup.LeadActor.Export.ObjectName.Instanced})";
            string members = string.Join("\n", ActiveTransformGroup.Members
                .Where(actor => actor is not null)
                .Where(actor => !ReferenceEquals(actor, ActiveTransformGroup.LeadActor))
                .Select(actor => $"  [{actor.Export.UIndex}] {actor.Export.ObjectName.Instanced}"));

            return string.IsNullOrEmpty(members) ? header : $"{header}\n{members}";
        }
    }

    public ActorProxy SelectedActor
    {
        get => selectedActor;
        set
        {
            bool shouldFocus = false;
            _suppressSelectionFocus = false;
            SelectActor(value, shouldFocus);
        }
    }

    public bool IsBioStageActorSelected => selectedActor is BioStageActorProxy;
    public IReadOnlyList<BioStageOverlayMarker> BioStageMarkers => (selectedActor as BioStageActorProxy)?.StageMarkers ?? Array.Empty<BioStageOverlayMarker>();
    public bool HasBioStageMarkers => BioStageMarkers.Count > 0;

    public BioStageOverlayMarker SelectedBioStageMarker
    {
        get => _selectedBioStageMarker;
        set
        {
            if (value is not null && !ReferenceEquals(selectedActor, value.Owner))
            {
                SelectActor(value.Owner, false);
            }

            if (SetProperty(ref _selectedBioStageMarker, value))
            {
                OnPropertyChanged(nameof(HasSelectedBioStageMarker));
                if (value is not null)
                {
                    IsBioStageMarkersExpanded = true;
                }
            }
        }
    }

    public bool HasSelectedBioStageMarker => SelectedBioStageMarker is not null;

    public bool IsBioStageMarkersExpanded
    {
        get => _isBioStageMarkersExpanded;
        set => SetProperty(ref _isBioStageMarkersExpanded, value);
    }

    private void RunWithoutSelectionFocus(Action action)
    {
        _selectionFocusSuppressionDepth++;
        try
        {
            action();
        }
        finally
        {
            _selectionFocusSuppressionDepth--;
        }
    }

    private bool isDirty;
    public bool IsDirty
    {
        get => isDirty;
        set => SetProperty(ref isDirty, value);
    }

    private bool _showCollision;
    public bool ShowCollision
    {
        get => _showCollision;
        set => SetProperty(ref _showCollision, value);
    }

    private bool _showLights = true;
    public bool ShowLights
    {
        get => _showLights;
        set
        {
            if (SetProperty(ref _showLights, value))
            {
                SyncCategoryWithVisibleSetWhenActive(value, actor => actor.IsLight);
            }
        }
    }

    private bool _showStageNodes = true;
    public bool ShowStageNodes
    {
        get => _showStageNodes;
        set => SetProperty(ref _showStageNodes, value);
    }

    private bool _showStageCameras = true;
    public bool ShowStageCameras
    {
        get => _showStageCameras;
        set => SetProperty(ref _showStageCameras, value);
    }

    private int _lightRenderDistance = 1000;
    public int LightRenderDistance
    {
        get => _lightRenderDistance;
        set => SetProperty(ref _lightRenderDistance, Math.Max(0, value));
    }

    private bool _showVolumes = false;
    public bool ShowVolumes
    {
        get => _showVolumes;
        set
        {
            if (SetProperty(ref _showVolumes, value))
            {
                SyncCategoryWithVisibleSetWhenActive(value, actor => actor.IsVolume);
            }
        }
    }

    private bool _showVolumetrics = false;
    public bool ShowVolumetrics
    {
        get => _showVolumetrics;
        set
        {
            if (SetProperty(ref _showVolumetrics, value))
            {
                SyncCategoryWithVisibleSetWhenActive(value, actor => actor.IsVolumetricMesh);
            }
        }
    }

    private bool _showEmitters;
    public bool ShowEmitters
    {
        get => _showEmitters;
        set
        {
            if (SetProperty(ref _showEmitters, value))
            {
                SyncCategoryWithVisibleSetWhenActive(value, actor => actor.IsEmitter);
            }
        }
    }

    private bool _showLocationActors;
    public bool ShowLocationActors
    {
        get => _showLocationActors;
        set
        {
            if (SetProperty(ref _showLocationActors, value))
            {
                SyncCategoryWithVisibleSetWhenActive(value, actor => actor.IsLocationActor);
            }
        }
    }

    private bool _showSoundPositions;
    public bool ShowSoundPositions
    {
        get => _showSoundPositions;
        set
        {
            if (SetProperty(ref _showSoundPositions, value))
            {
                SyncCategoryWithVisibleSetWhenActive(value, actor => actor.IsAmbientSound);
            }
        }
    }

    private bool _showCinematicActors;
    public bool ShowCinematicActors
    {
        get => _showCinematicActors;
        set
        {
            if (SetProperty(ref _showCinematicActors, value))
            {
                SyncCategoryWithVisibleSetWhenActive(value, actor => actor.IsCinematicActor);
            }
        }
    }

    private bool _showDecalActors;
    public bool ShowDecalActors
    {
        get => _showDecalActors;
        set
        {
            if (SetProperty(ref _showDecalActors, value))
            {
                SyncCategoryWithVisibleSetWhenActive(value, actor => actor.IsDecalActor);
            }
        }
    }

    private bool _showCameraCoordinates;
    public bool ShowCameraCoordinates
    {
        get => _showCameraCoordinates;
        set
        {
            if (SetProperty(ref _showCameraCoordinates, value))
            {
                RenderContext.ShowDebugStatsOverlay = value;
                if (value)
                {
                    UpdateCameraCoordinatesText();
                }
            }
            OnPropertyChanged(nameof(CameraCoordinatesDisplayText));
        }
    }

    private string _cameraCoordinates = "Camera X=0.0 | Y=0.0 | Z=0.0";
    public string CameraCoordinates
    {
        get => _cameraCoordinates;
        set
        {
            if (SetProperty(ref _cameraCoordinates, value))
            {
                OnPropertyChanged(nameof(CameraCoordinatesDisplayText));
            }
        }
    }

    public string CameraCoordinatesDisplayText => ShowCameraCoordinates ? CameraCoordinates : string.Empty;

    private ObjectRenderMode _objectRenderMode = ObjectRenderMode.Full;
    public ObjectRenderMode ObjectRenderMode
    {
        get => _objectRenderMode;
        set
        {
            if (SetProperty(ref _objectRenderMode, value))
            {
                UseVisibleSetOnly = value is ObjectRenderMode.VisibleSetOnly;
                if (value is ObjectRenderMode.VisibleSetOnly && _visibleActorSet.Count is 0 && !_hasUserEditedVisibleSets)
                {
                    InitializeVisibleSetToAll();
                }
            }
        }
    }

    private readonly HashSet<string> _visibleActorSet = [];
    private readonly HashSet<string> _explicitlyHiddenVisibleSetClasses = [];
    private bool _hasUserEditedVisibleSets;
    private bool _suppressDisplayFilterVisibleSetSync;

    private bool _useVisibleSetOnly;
    public bool UseVisibleSetOnly
    {
        get => _useVisibleSetOnly;
        set
        {
            if (!SetProperty(ref _useVisibleSetOnly, value))
            {
                return;
            }

            if (value && _visibleActorSet.Count is 0 && !_hasUserEditedVisibleSets)
            {
                InitializeVisibleSetToAll();
            }

            if (ObjectRenderMode is ObjectRenderMode.VisibleSetOnly && !value)
            {
                ObjectRenderMode = ObjectRenderMode.Full;
            }
        }
    }

    private int _visibleSetDistance = 5000;
    public int VisibleSetDistance
    {
        get => _visibleSetDistance;
        set => SetProperty(ref _visibleSetDistance, Math.Max(0, value));
    }

    public bool UseLocalCoordsForWidget
    {
        get => RenderContext.TransformWidget.UseLocalCoords;
        set => SetProperty(ref RenderContext.TransformWidget.UseLocalCoords, value);
    }

    public bool TranslateSnapEnabled
    {
        get => RenderContext.TransformWidget.TranslateSnapEnabled;
        set => SetProperty(ref RenderContext.TransformWidget.TranslateSnapEnabled, value);
    }
    public float TranslateSnapValue
    {
        get => RenderContext.TransformWidget.TranslateSnapValue;
        set => SetProperty(ref RenderContext.TransformWidget.TranslateSnapValue, value);
    }
    public bool RotateSnapEnabled
    {
        get => RenderContext.TransformWidget.RotateSnapEnabled;
        set => SetProperty(ref RenderContext.TransformWidget.RotateSnapEnabled, value);
    }
    public float RotateSnapValue
    {
        get => RenderContext.TransformWidget.RotateSnapValue;
        set => SetProperty(ref RenderContext.TransformWidget.RotateSnapValue, value);
    }
    public bool ScaleSnapEnabled
    {
        get => RenderContext.TransformWidget.ScaleSnapEnabled;
        set => SetProperty(ref RenderContext.TransformWidget.ScaleSnapEnabled, value);
    }
    public float ScaleSnapValue
    {
        get => RenderContext.TransformWidget.ScaleSnapValue;
        set => SetProperty(ref RenderContext.TransformWidget.ScaleSnapValue, value);
    }

    public ObservableCollectionExtended<RecentFileSet> RecentSets { get; } = [];

    private static string RecentSetsFile => Path.Combine(
        Directory.CreateDirectory(Path.Combine(AppDirectories.AppDataFolder, "LevelEditor")).FullName,
        "RECENTSETS");

    public LevelEditor()
    {
        RenderContext = new LevelEditorRenderContext();
        RenderContext.TransformWidget.OnDragComplete = OnWidgetDragComplete;
        ActorsView = CollectionViewSource.GetDefaultView(Actors);
        ActorsView.Filter = ActorFilter;
        ActorsView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(ActorProxy.OwningFile)));
        ActorsView.SortDescriptions.Add(new SortDescription(nameof(ActorProxy.OwningFileSortOrder), ListSortDirection.Ascending));
        ActorsView.SortDescriptions.Add(new SortDescription(nameof(ActorProxy.ActorUIndex), ListSortDirection.Ascending));

        LoadCommands();
        InitializeComponent();
        LoadRecentSets();

        SceneViewer.Context = RenderContext;
        RenderContext.ShowDebugStatsOverlay = ShowCameraCoordinates;
        UndoHistory.PropertyChanged += UndoHistory_PropertyChanged;
    }

    private string FileQueuedForLoad;
    private int ExportQueuedForFocusing;

    public LevelEditor(ExportEntry exportToLoad) : this()
    {
        FileQueuedForLoad = exportToLoad.FileRef.FilePath;
        ExportQueuedForFocusing = exportToLoad.UIndex;
    }

    private void UpdateScene(object sender, float e)
    {
        if (!ShowCameraCoordinates)
        {
            return;
        }

        UpdateCameraCoordinatesText();
    }

    private void UpdateCameraCoordinatesText()
    {
        Vector3 position = RenderContext.Camera.Position;
        float yawDegrees = RenderContext.Camera.Yaw * (180f / MathF.PI);
        float pitchDegrees = RenderContext.Camera.Pitch * (180f / MathF.PI);
        CameraCoordinates = $"Camera X={position.X:F1} | Y={position.Y:F1} | Z={position.Z:F1} | Yaw={yawDegrees:F1}° | Pitch={pitchDegrees:F1}°";
    }

    private void RenderScene(object sender, EventArgs e)
    {
        RenderContext.ShowLights = ShowLights;
        RenderContext.ShowVolumes = ShowVolumes;
        RenderContext.ShowVolumetrics = ShowVolumetrics;
        RenderContext.ShowEmitters = ShowEmitters;
        RenderContext.ShowLocationActors = ShowLocationActors;
        RenderContext.ShowSoundPositions = ShowSoundPositions;
        RenderContext.ShowCinematicActors = ShowCinematicActors;
        RenderContext.ShowDecalActors = ShowDecalActors;
        RenderContext.ShowStageNodes = ShowStageNodes;
        RenderContext.ShowStageCameras = ShowStageCameras;
        Span<RenderPass> passes = ShowCollision
            ? [RenderPass.Base, RenderPass.Hair, RenderPass.Collision]
            : [RenderPass.Base, RenderPass.Hair];

        foreach (RenderPass pass in passes)
        {
            DoRenderPass(pass);
        }

        RenderContext.DrawUI();
    }
    void DoRenderPass(RenderPass pass)
    {
        float lightRenderDistanceSq = LightRenderDistance * LightRenderDistance;
        float visibleSetDistanceSq = (float)VisibleSetDistance * VisibleSetDistance;
        Vector3 cameraPosition = RenderContext.Camera.Position;
        bool isOrthographicCamera = RenderContext.Camera.IsOrthographic;
        bool baseWireframe = RenderContext.Wireframe;
        for (int i = 0; i < RenderContext.DrawList_3D.Count; i++)
        {
            ActorProxy actor = RenderContext.DrawList_3D[i];
            Vector3 actorDeltaFromCamera = actor.Location - cameraPosition;
            float actorVisibleSetDistanceSq = isOrthographicCamera
                ? (actorDeltaFromCamera.X * actorDeltaFromCamera.X) + (actorDeltaFromCamera.Y * actorDeltaFromCamera.Y)
                : Vector3.Dot(actorDeltaFromCamera, actorDeltaFromCamera);
            if (actor.IsLight && !ShowLights) continue;
            if (actor.IsLight && Vector3.DistanceSquared(actor.Location, cameraPosition) > lightRenderDistanceSq) continue;
            if (actor.IsVolume && !ShowVolumes) continue;
            if (actor.IsVolumetricMesh && !ShowVolumetrics) continue;
            if (actor.IsEmitter && !ShowEmitters) continue;
            if (actor.IsLocationActor && !ShowLocationActors) continue;
            if (actor.IsAmbientSound && !ShowSoundPositions) continue;
            if (actor.IsCinematicActor && !ShowCinematicActors) continue;
            if (actor.IsDecalActor && !ShowDecalActors) continue;
            if ((UseVisibleSetOnly || ObjectRenderMode is ObjectRenderMode.VisibleSetOnly)
                && pass is RenderPass.Base or RenderPass.Hair
                && !actor.IsVolumetricMesh
                && (!_visibleActorSet.Contains(GetActorVisibilityKey(actor))
                    || actorVisibleSetDistanceSq > visibleSetDistanceSq))
            {
                continue;
            }
            if ((UseVisibleSetOnly || ObjectRenderMode is ObjectRenderMode.VisibleSetOnly)
                && pass is RenderPass.Base or RenderPass.Hair
                && actor.IsVolumetricMesh
                && (!_visibleActorSet.Contains(GetActorVisibilityKey(actor))
                    || actorVisibleSetDistanceSq > visibleSetDistanceSq))
            {
                continue;
            }
            if ((UseVisibleSetOnly || ObjectRenderMode is ObjectRenderMode.VisibleSetOnly)
                && pass is RenderPass.Collision
                && !actor.IsVolumetricMesh
                && actorVisibleSetDistanceSq > visibleSetDistanceSq)
            {
                continue;
            }
            if (ObjectRenderMode is ObjectRenderMode.Hidden
                && pass is RenderPass.Base or RenderPass.Hair
                && !actor.IsVolume
                && !actor.IsVolumetricMesh
                && !actor.IsLight
                && !actor.IsEmitter
                && !actor.IsLocationActor
                && !actor.IsAmbientSound
                && !actor.IsCinematicActor
                && !actor.IsDecalActor)
            {
                continue;
            }
            if ((UseVisibleSetOnly || ObjectRenderMode is ObjectRenderMode.VisibleSetOnly)
                && pass is RenderPass.Base or RenderPass.Hair
                && actor.IsAmbientSound
                && actorVisibleSetDistanceSq > visibleSetDistanceSq)
            {
                continue;
            }

            bool forceWireframeForActor = ObjectRenderMode is ObjectRenderMode.Wireframe
                                          && pass is RenderPass.Base or RenderPass.Hair
                                          && !actor.IsVolume
                                          && !actor.IsVolumetricMesh;
            bool targetWireframeState = baseWireframe || forceWireframeForActor;
            if (RenderContext.Wireframe != targetWireframeState)
            {
                RenderContext.Wireframe = targetWireframeState;
            }

            int hitID = actor.HitID;
            RenderContext.CurrentHitTestId = new Vector3((hitID & 0xFF) / 255f, ((hitID >> 8) & 0xFF) / 255f, ((hitID >> 16) & 0xFF) / 255f);
            if (actor == selectedActor)
            {
                RenderContext.RenderFlags |= LevelEditorRenderContext.ShaderFlags.Selected;
            }
            actor.Render(RenderContext, pass);
            RenderContext.RenderFlags &= ~LevelEditorRenderContext.ShaderFlags.Selected;
        }

        if (RenderContext.Wireframe != baseWireframe)
        {
            RenderContext.Wireframe = baseWireframe;
        }
    }

    private void ViewportActorSelect(ActorProxy actor)
    {
        if (actor is null)
        {
            return;
        }

        bool addToSelection = RenderContext.LastActorSelectionWasAdditive;

        if (MeshExportsList is null)
        {
            SelectActor(actor, false);
            return;
        }

        RunWithoutSelectionFocus(() =>
        {
            List<ActorProxy> selectedActors = MeshExportsList.SelectedItems.OfType<ActorProxy>().ToList();

            if (addToSelection)
            {
                if (selectedActors.Contains(actor))
                {
                    selectedActors.Remove(actor);
                }
                else
                {
                    selectedActors.Add(actor);
                }
            }
            else
            {
                selectedActors.Clear();
                selectedActors.Add(actor);
            }

            ActorProxy nextSelectedActor;
            if (!addToSelection)
            {
                nextSelectedActor = actor;
            }
            else if (selectedActors.Count is 0)
            {
                nextSelectedActor = null;
            }
            else if (selectedActors.Contains(actor))
            {
                nextSelectedActor = actor;
            }
            else
            {
                nextSelectedActor = selectedActors.Last();
            }

            MeshExportsList.SelectedItems.Clear();
            foreach (ActorProxy selected in selectedActors)
            {
                MeshExportsList.SelectedItems.Add(selected);
            }

            SelectedActor = nextSelectedActor;
            if (nextSelectedActor is not null)
            {
                MeshExportsList.ScrollIntoView(nextSelectedActor);
            }

            UpdateGroupableSelectionCount();
            InvalidateSelectionCommands();
        });
    }

    private void ViewportActorFocus(ActorProxy actor)
    {
        if (actor is null)
        {
            return;
        }

        ViewportActorSelect(actor);
        FocusOnBounds(actor.GetBounds());
    }

    private void ViewportBioStageMarkerSelect(BioStageOverlayMarker marker)
    {
        if (marker is null)
        {
            return;
        }

        if (!ReferenceEquals(selectedActor, marker.Owner))
        {
            SelectActor(marker.Owner, false);
        }

        SelectedBioStageMarker = marker;
    }

    private void SelectActor(ActorProxy actor, bool focus)
    {
        var prev = selectedActor;
        if (SetProperty(ref selectedActor, actor, nameof(SelectedActor)))
        {
            if (prev is not null)
            {
                prev.PropertyChanged -= OnActorPropertyChanged;
            }
            if (selectedActor is not null)
            {
                RenderContext.TransformWidget.Attach = selectedActor;
                if (focus)
                {
                    FocusOnBounds(selectedActor.GetBounds());
                }
                selectedActor.PropertyChanged += OnActorPropertyChanged;
                _preEditSnapshot = selectedActor.SnapshotTransform();
            }
            else
            {
                RenderContext.TransformWidget.Attach = null;
                _preEditSnapshot = null;
            }

            RenderContext.SelectedActor = selectedActor;
            if (selectedActor is not BioStageActorProxy)
            {
                SelectedBioStageMarker = null;
            }
            else if (SelectedBioStageMarker is not null && !ReferenceEquals(SelectedBioStageMarker.Owner, selectedActor))
            {
                SelectedBioStageMarker = null;
            }

            OnPropertyChanged(nameof(BioStageMarkers));
            OnPropertyChanged(nameof(HasBioStageMarkers));
            OnPropertyChanged(nameof(IsBioStageActorSelected));
        }
    }

    private void CenterView()
    {
        if (Actors.Count > 0)
        {
            BoxSphereBounds fullBounds = Actors[0].GetBounds();
            for (int i = 1; i < Actors.Count; i++)
            {
                fullBounds = fullBounds.Union(Actors[i].GetBounds());
            }
            FocusOnBounds(fullBounds);
        }
        else
        {
            RenderContext.Camera.Position = Vector3.Zero;
            RenderContext.Camera.Pitch = -MathF.PI / 5.0f;
            RenderContext.Camera.Yaw = MathF.PI / 4.0f;
        }
    }

    private void FocusOnBounds(BoxSphereBounds fullBounds)
    {
        Vector3 origin = fullBounds.Origin;
        if (RenderContext.Camera.IsOrthographic)
        {
            RenderContext.Camera.Position = new Vector3(origin.X, origin.Y, RenderContext.Camera.ZFar * 0.4f);
            RenderContext.Camera.OrthoWidth = fullBounds.SphereRadius.Clamp(10, float.MaxValue) * 3f;
        }
        else
        {
            float hyp = fullBounds.SphereRadius.Clamp(10, float.MaxValue) * 2;
            (float sin, float cos) = MathF.SinCos(MathF.PI / 2.5f);
            RenderContext.Camera.Position = new Vector3(origin.X, origin.Y + sin * hyp, origin.Z + cos * hyp);
            RenderContext.Camera.OrientTowards(origin);
        }
    }

    #region File Management

    public async Task LoadFileAsync(string s)
    {
        try
        {
            string filePath = Path.GetFullPath(s);
            RecentViewState matchingViewState = RecentSets
                .FirstOrDefault(set => set.FilePaths.Count > 0
                                       && set.FilePaths[0].Equals(filePath, StringComparison.OrdinalIgnoreCase))
                ?.ViewState;

            PersistCurrentRecentViewState();
            CloseAllFiles();
            Dispatcher.Invoke(new Action(() => { }), DispatcherPriority.ContextIdle, null);


            using var guard = new RenderGuard(this);

            await AddLevelFile(filePath).ConfigureAwait(true);
            ApplyViewState(matchingViewState);
        }
        catch (Exception e)
        {
            StatusBar_LeftMostText.Text = "Failed to load " + Path.GetFileName(s);
            MessageBox.Show($"Error loading {Path.GetFileName(s)}:\n{e.Message}");
            IsBusy = false;
            IsBusyTaskbar = false;
        }
    }

    private async Task AddLevelFile(string path)
    {
        path = Path.GetFullPath(path);

        if (OpenFiles.Any(f => f.FilePath.Equals(path, StringComparison.OrdinalIgnoreCase)))
        {
            MessageBox.Show(this, $"{Path.GetFileName(path)} is already open.");
            return;
        }

        using IMEPackage pcc = MEPackageHandler.OpenMEPackage(path);
        if (OpenFiles.Count > 0 && pcc.Game != Game)
        {
            MessageBox.Show(this, $"Cannot mix games. The open files are {Game}, but {Path.GetFileName(path)} is {pcc.Game}.");
            return;
        }
        Game = pcc.Game;
        ExportEntry levelExport = pcc.Exports.FirstOrDefault(exp => exp.ClassName == "Level");
        if (levelExport is null)
        {
            MessageBox.Show(this, $"{Path.GetFileName(path)} is not a level file!");
            return;
        }

        var openFile = new OpenLevelFile(this, pcc, levelExport, _nextFileLoadOrder++);
        // Register the OpenLevelFile as a user of the package for update notifications
        pcc.RegisterTool(openFile);
        OpenFiles.Add(openFile);
        HasAnyFileOpen = true;

        RecordCurrentFilesAsRecent();

        Level levelBin = levelExport.GetBinaryData<Level>();
        bool isFirstFile = OpenFiles.Count == 1;

        IsBusy = true;
        BusyText = $"Loading {Path.GetFileName(path)}...";

        var (actors, ignoredClasses) = await Task.Run(() => LoadActors(levelBin, openFile)).ConfigureAwait(true);
        var sorted = actors.OrderBy(actor => actor.Export.UIndex).ToList();
        openFile.Actors.AddRange(sorted);
        Actors.AddRange(sorted);
        RenderContext.LoadActors(sorted);

        if (isFirstFile)
        {
            CenterView();
        }

        if (ignoredClasses.Count > 0)
        {
            string existing = string.IsNullOrEmpty(TextBelowActors) ? "" : TextBelowActors + "\n";
            TextBelowActors = existing + $"{Path.GetFileName(path)} unrendered: {string.Join(", ", ignoredClasses)}";
        }

        if (ExportQueuedForFocusing > 0)
        {
            if (sorted.FirstOrDefault(a => a.Export.UIndex == ExportQueuedForFocusing) is { } proxy)
            {
                SelectedActor = proxy;
                ExportQueuedForFocusing = 0;
            }
        }

        UpdateTitle();
    }

    private void CloseAllFiles()
    {
        Game = MEGame.Unknown;
        if (selectedActor is not null)
        {
            selectedActor.PropertyChanged -= OnActorPropertyChanged;
            selectedActor = null;
        }
        SceneViewer.SetShouldRender(false);
        RenderContext.UnloadLevel();
        Actors.Clear();
        _visibleActorSet.Clear();
        _explicitlyHiddenVisibleSetClasses.Clear();
        UseVisibleSetOnly = false;
        foreach (var file in OpenFiles)
        {
            file.Dispose();
        }
        OpenFiles.Clear();
        HasAnyFileOpen = false;
        TextBelowActors = "";
        IsDirty = false;
        UndoHistory.Clear();
        _preEditSnapshot = null;
    }

    public void CloseFile(OpenLevelFile file)
    {
        if (file is null) return;

        if (file.IsDirty)
        {
            var result = MessageBox.Show(this,
                $"{file.FileName} has uncommitted changes. Close anyway?",
                "Unsaved Changes", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes) return;
        }
        else if (file.Package.IsModified && file.Package.Users.Count <= 1)
        {
            var result = MessageBox.Show(this,
                $"{file.FileName} has unsaved changes. Close anyway?",
                "Unsaved Changes", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes) return;
        }

        if (SelectedActor is not null && file.Actors.Contains(SelectedActor))
        {
            SelectedActor = null;
        }

        foreach (var actor in file.Actors)
        {
            Actors.Remove(actor);
            RenderContext.RemoveActor(actor);
            actor.Dispose();
        }
        ReevaluateActiveGroup();

        file.Dispose();
        OpenFiles.Remove(file);
        HasAnyFileOpen = OpenFiles.Count > 0;
        UpdateGlobalDirtyState();
        UpdateTitle();

        if (OpenFiles.Count == 0)
        {
            SceneViewer.SetShouldRender(false);
            TextBelowActors = "";
            UndoHistory.Clear();
            _preEditSnapshot = null;
        }
    }

    public void CloseFileByName(string fileName)
    {
        var file = OpenFiles.FirstOrDefault(f => f.FileName == fileName);
        if (file is not null)
        {
            CloseFile(file);
        }
    }

    private void UpdateTitle()
    {
        if (OpenFiles.Count == 0)
            Title = "Level Editor";
        else if (OpenFiles.Count == 1)
            Title = $"Level Editor - {OpenFiles[0].FilePath}";
        else
            Title = $"Level Editor - {OpenFiles.Count} files";

        StatusBar_LeftMostText.Text = OpenFiles.Count switch
        {
            0 => "Select package file to load",
            1 => OpenFiles[0].FileName,
            _ => $"{OpenFiles.Count} files loaded"
        };
    }

    #endregion

    #region Actor Loading

    private readonly record struct LoadActorsResult(List<ActorProxy> actors, HashSet<string> ignoredActorClasses);

    private LoadActorsResult LoadActors(Level level, OpenLevelFile owningFile)
    {
        var actorExports = level.Actors.Where(level.Export.FileRef.IsUExport).Select(level.Export.FileRef.GetUExport);
        var actors = new List<ActorProxy>();
        HashSet<string> ignoredActorClasses = [];
        foreach (var actorExport in actorExports)
        {
            var className = actorExport.ClassName;
            if (className is "StaticMeshCollectionActor")
            {
                var smca = actorExport.GetBinaryData<StaticMeshCollectionActor>();
                for (int i = 0; i < smca.Components.Count; i++)
                {
                    if (level.Export.FileRef.TryGetUExport(smca.Components[i], out ExportEntry smcExport))
                    {
                        var smcActor = new StaticMeshComponentActorProxy(this, smcExport, smca, i);
                        smcActor.OwningFile = owningFile;
                        actors.Add(smcActor);
                    }
                }
            }
            else if (className is "StaticLightCollectionActor")
            {
                var slca = actorExport.GetBinaryData<StaticLightCollectionActor>();
                for (int i = 0; i < slca.Components.Count; i++)
                {
                    if (level.Export.FileRef.TryGetUExport(slca.Components[i], out ExportEntry lightComponentExport))
                    {
                        var lightActor = new StaticLightComponentActorProxy(this, lightComponentExport, slca, i);
                        lightActor.OwningFile = owningFile;
                        actors.Add(lightActor);
                    }
                }
            }
            else if (ActorProxy.Create(this, actorExport) is { } actorProxy)
            {
                actorProxy.OwningFile = owningFile;
                actors.Add(actorProxy);
            }
            else if (className is not "BioWorldInfo")
            {
                ignoredActorClasses.Add(className);
            }
        }
        foreach (var actor in actors)
        {
            actor.ResolveAttachment(actors);
        }
        return new(actors, ignoredActorClasses);
    }

    public void RemoveActor(ActorProxy actor)
    {
        if (Actors.Remove(actor))
        {
            _visibleActorSet.Remove(GetActorVisibilityKey(actor));
            actor.Detach();
            actor.OwningFile?.Actors.Remove(actor);
            RenderContext.RemoveActor(actor);
            actor.Dispose();
        }
    }

    public void AddActor(ActorProxy actor, bool sort = true)
    {
        if (!Actors.Contains(actor))
        {
            Actors.Add(actor);
            actor.ResolveAttachment(Actors);
            actor.OwningFile?.Actors.Add(actor);
            RenderContext.AddActor(actor);
            if (sort)
            {
                SortActorsByFileOrderThenUIndex();
            }
        }
    }

    private void SortActorsByFileOrderThenUIndex()
    {
        Actors.Sort(actor => (actor.OwningFileSortOrder, actor.Export.UIndex));
    }

    #endregion

    #region Commands

    public ICommand OpenFileCommand { get; set; }
    public ICommand AddFileCommand { get; set; }
    public ICommand SaveAllCommand { get; set; }
    public ICommand SaveAsCommand { get; set; }
    public ICommand SaveSingleFileCommand { get; set; }
    public ICommand CloseFileCommand { get; set; }
    public ICommand ToggleTranslateCommand { get; set; }
    public ICommand ToggleRotateCommand { get; set; }
    public ICommand ToggleScaleCommand { get; set; }
    public ICommand ToggleUniformScaleCommand { get; set; }
    public ICommand CommitChangesCommand { get; set; }
    public ICommand CommitSingleFileCommand { get; set; }
    public ICommand LoadRelatedLevelsCommand { get; set; }
    public ICommand FocusSelectedCommand { get; set; }
    public ICommand ToggleLocalCoordsCommand { get; set; }
    public ICommand OpenInPackageEditorCommand { get; set; }
    public ICommand OpenRecentSetCommand { get; set; }
    public ICommand UndoCommand { get; set; }
    public ICommand RedoCommand { get; set; }
    public ICommand ToggleOrthoViewCommand { get; set; }
    public ICommand ToggleVisibleSetOnlyCommand { get; set; }
    public ICommand ToggleSelectedActorVisibilityCommand { get; set; }
    public ICommand AddSelectedToVisibleSetCommand { get; set; }
    public ICommand RemoveSelectedFromVisibleSetCommand { get; set; }
    public ICommand ShowOnlySelectedCommand { get; set; }
    public ICommand ClearVisibleSetCommand { get; set; }
    public ICommand ShowAllMeshesCommand { get; set; }
    public ICommand AddSelectedClassToVisibleSetCommand { get; set; }
    public ICommand RemoveSelectedClassFromVisibleSetCommand { get; set; }
    public ICommand ShowOnlySelectedClassCommand { get; set; }
    public ICommand AddNearbyToVisibleSetCommand { get; set; }
    public ICommand ShowOnlyNearbyCommand { get; set; }
    public ICommand OpenVisibleSetsManagerCommand { get; set; }
    public ICommand GroupSelectedActorsCommand { get; set; }
    public ICommand UngroupActorsCommand { get; set; }
    public ICommand ReselectGroupCommand { get; set; }
    private void LoadCommands()
    {
        OpenFileCommand = new GenericCommand(OpenFile);
        AddFileCommand = new GenericCommand(AddFile);
        SaveAllCommand = new GenericCommand(SaveAllFiles, PackageIsLoaded);
        SaveAsCommand = new GenericCommand(SaveFileAs, PackageIsLoaded);
        SaveSingleFileCommand = new RelayCommand(SaveSingleFileExecute, _ => PackageIsLoaded());
        CloseFileCommand = new RelayCommand(CloseFileExecute);
        ToggleTranslateCommand = new GenericCommand(() => { RenderContext.TransformWidget.Mode = EWidgetMode.Translate; CurrentModeName = "Translate"; }, PackageIsLoaded);
        ToggleRotateCommand = new GenericCommand(() => { RenderContext.TransformWidget.Mode = EWidgetMode.Rotate; CurrentModeName = "Rotate"; }, PackageIsLoaded);
        ToggleScaleCommand = new GenericCommand(() => { RenderContext.TransformWidget.Mode = EWidgetMode.Scale; CurrentModeName = "Scale"; }, PackageIsLoaded);
        ToggleUniformScaleCommand = new GenericCommand(() => { RenderContext.TransformWidget.Mode = EWidgetMode.UniformScale; CurrentModeName = "Uniform Scale"; }, PackageIsLoaded);
        CommitChangesCommand = new GenericCommand(CommitChanges, PackageIsLoaded);
        CommitSingleFileCommand = new RelayCommand(CommitSingleFileExecute, _ => PackageIsLoaded());
        LoadRelatedLevelsCommand = new GenericCommand(LoadRelatedLevels, PackageIsLoaded);
        FocusSelectedCommand = new GenericCommand(() =>
        {
            if (SelectedActor is not null)
            {
                FocusOnBounds(SelectedActor.GetBounds());
            }
        }, () => PackageIsLoaded() && SelectedActor is not null && CanUseSingleKeyShortcut());
        ToggleLocalCoordsCommand = new GenericCommand(() => UseLocalCoordsForWidget = !UseLocalCoordsForWidget,
            () => PackageIsLoaded() && CanUseSingleKeyShortcut());
        OpenInPackageEditorCommand = new GenericCommand(() =>
        {
            if (SelectedActor is not null)
            {
                var p = new PackageEditorWindow();
                p.Show();

                IMEPackage actorPackage = SelectedActor.Export.FileRef;
                int uIndex = SelectedActor.Export.UIndex;
                p.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
                {
                    p.LoadPackage(actorPackage, uIndex);
                    p.Activate();
                }));
            }
        }, () => PackageIsLoaded() && SelectedActor is not null);
        OpenRecentSetCommand = new RelayCommand(obj => { if (obj is RecentFileSet set) OpenRecentFileSet(set); });
        UndoCommand = new GenericCommand(Undo, () => UndoHistory.CanUndo);
        RedoCommand = new GenericCommand(Redo, () => UndoHistory.CanRedo);
        ToggleOrthoViewCommand = new GenericCommand(() => IsOrthographicView = !IsOrthographicView, CanUseSingleKeyShortcut);
        ToggleVisibleSetOnlyCommand = new GenericCommand(() => UseVisibleSetOnly = !UseVisibleSetOnly, PackageIsLoaded);
        ToggleSelectedActorVisibilityCommand = new GenericCommand(ToggleSelectedActorVisibility, () => PackageIsLoaded() && SelectedActor is not null && CanUseSingleKeyShortcut());
        AddSelectedToVisibleSetCommand = new GenericCommand(AddSelectedToVisibleSet, () => PackageIsLoaded() && SelectedActor is not null);
        RemoveSelectedFromVisibleSetCommand = new GenericCommand(RemoveSelectedFromVisibleSet, () => PackageIsLoaded() && SelectedActor is not null);
        ShowOnlySelectedCommand = new GenericCommand(ShowOnlySelected, () => PackageIsLoaded() && SelectedActor is not null);
        ClearVisibleSetCommand = new GenericCommand(ClearVisibleSet, PackageIsLoaded);
        ShowAllMeshesCommand = new GenericCommand(ShowAllMeshes, PackageIsLoaded);
        AddSelectedClassToVisibleSetCommand = new GenericCommand(AddSelectedClassToVisibleSet, () => PackageIsLoaded() && SelectedActor is not null);
        RemoveSelectedClassFromVisibleSetCommand = new GenericCommand(RemoveSelectedClassFromVisibleSet, () => PackageIsLoaded() && SelectedActor is not null);
        ShowOnlySelectedClassCommand = new GenericCommand(ShowOnlySelectedClass, () => PackageIsLoaded() && SelectedActor is not null);
        AddNearbyToVisibleSetCommand = new GenericCommand(AddNearbyToVisibleSet, PackageIsLoaded);
        ShowOnlyNearbyCommand = new GenericCommand(ShowOnlyNearby, PackageIsLoaded);
        OpenVisibleSetsManagerCommand = new GenericCommand(OpenVisibleSetsManager, PackageIsLoaded);
        GroupSelectedActorsCommand = new GenericCommand(CreateActorGroupFromSelection, () => PackageIsLoaded() && GetGroupableSelectedActors().Count >= 2);
        UngroupActorsCommand = new GenericCommand(UngroupActors, () => PackageIsLoaded() && HasActiveTransformGroup);
        ReselectGroupCommand = new GenericCommand(ReselectActiveGroup, () => PackageIsLoaded() && HasActiveTransformGroup);
    }

    #endregion

    private void LevelEditor_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!CanUseSingleKeyShortcut())
        {
            return;
        }

        if (e.Key is Key.F)
        {
            if (FocusSelectedCommand?.CanExecute(null) == true)
            {
                FocusSelectedCommand.Execute(null);
                e.Handled = true;
            }
            return;
        }

        if (e.Key is Key.L)
        {
            if (ToggleLocalCoordsCommand?.CanExecute(null) == true)
            {
                ToggleLocalCoordsCommand.Execute(null);
                e.Handled = true;
            }
            return;
        }

        if (e.Key is Key.NumPad5)
        {
            if (ToggleOrthoViewCommand?.CanExecute(null) == true)
            {
                ToggleOrthoViewCommand.Execute(null);
                e.Handled = true;
            }
        }
    }

    private static bool CanUseSingleKeyShortcut()
    {
        if (Keyboard.FocusedElement is not DependencyObject focusedElement)
        {
            return true;
        }

        if (GetAncestor<TextBoxBase>(focusedElement) is not null)
        {
            return false;
        }

        if (GetAncestor<ComboBox>(focusedElement) is { IsEditable: true })
        {
            return false;
        }

        return true;
    }

    private static T GetAncestor<T>(DependencyObject element) where T : DependencyObject
    {
        DependencyObject current = element;
        while (current is not null)
        {
            if (current is T match)
            {
                return match;
            }

            current = current switch
            {
                Visual visual => VisualTreeHelper.GetParent(visual),
                Visual3D visual3D => VisualTreeHelper.GetParent(visual3D),
                FrameworkContentElement frameworkContentElement => frameworkContentElement.Parent,
                _ => LogicalTreeHelper.GetParent(current)
            };
        }

        return null;
    }

    private List<ActorProxy> GetGroupableSelectedActors()
    {
        if (MeshExportsList is null)
        {
            return [];
        }

        return MeshExportsList.SelectedItems
            .OfType<ActorProxy>()
            .Where(actor => !actor.IsReadOnly)
            .Distinct()
            .ToList();
    }

    private void UpdateGroupableSelectionCount()
    {
        _groupableSelectionCount = GetGroupableSelectedActors().Count;
        OnPropertyChanged(nameof(CanGroupSelectedActors));
    }

    private void InvalidateSelectionCommands()
    {
        if (Dispatcher.CheckAccess())
        {
            CommandManager.InvalidateRequerySuggested();
            return;
        }

        Dispatcher.BeginInvoke((Action)CommandManager.InvalidateRequerySuggested);
    }

    private void CreateActorGroupFromSelection()
    {
        List<ActorProxy> members = GetGroupableSelectedActors();
        if (members.Count < 2)
        {
            MessageBox.Show(this, "Select at least two editable actors to create a transform group.", "Create Group", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        ActorProxy lead = SelectedActor is not null && members.Contains(SelectedActor)
            ? SelectedActor
            : members[0];

        ActiveTransformGroup = new ActorTransformGroup($"Group {DateTime.Now:HHmmss}", lead, members);
        _suppressSelectionFocus = true;
        SelectedActor = lead;
        RenderContext.TransformWidget.Attach = lead;
    }

    private void UngroupActors()
    {
        ActiveTransformGroup = null;
    }

    private void ReselectActiveGroup()
    {
        ReevaluateActiveGroup();
        if (ActiveTransformGroup is null || MeshExportsList is null)
        {
            return;
        }

        try
        {
            _isUpdatingGroupSelection = true;
            MeshExportsList.SelectedItems.Clear();
            foreach (var member in ActiveTransformGroup.Members.Where(actor => actor is not null && Actors.Contains(actor)))
            {
                MeshExportsList.SelectedItems.Add(member);
            }
        }
        finally
        {
            _isUpdatingGroupSelection = false;
        }

        _suppressSelectionFocus = true;
        SelectedActor = ActiveTransformGroup.LeadActor;
        MeshExportsList.ScrollIntoView(ActiveTransformGroup.LeadActor);
        RenderContext.TransformWidget.Attach = ActiveTransformGroup.LeadActor;
        CommandManager.InvalidateRequerySuggested();
    }

    private void ReevaluateActiveGroup()
    {
        if (ActiveTransformGroup is null)
        {
            return;
        }

        List<ActorProxy> validMembers = ActiveTransformGroup.Members
            .Where(actor => actor is not null && !actor.IsReadOnly && Actors.Contains(actor))
            .Distinct()
            .ToList();

        if (validMembers.Count < 2)
        {
            UngroupActors();
            return;
        }

        ActorProxy lead = ActiveTransformGroup.LeadActor;
        if (!validMembers.Contains(lead))
        {
            lead = SelectedActor is not null && validMembers.Contains(SelectedActor)
                ? SelectedActor
                : validMembers[0];
        }

        if (!ReferenceEquals(lead, ActiveTransformGroup.LeadActor)
            || validMembers.Count != ActiveTransformGroup.Members.Count
            || validMembers.Any(actor => !ActiveTransformGroup.Members.Contains(actor)))
        {
            ActiveTransformGroup = new ActorTransformGroup(ActiveTransformGroup.Name, lead, validMembers);
        }
        else
        {
            OnPropertyChanged(nameof(ActiveTransformGroupSummary));
        }
    }

    private static float ApplyScaleDelta(float memberValue, float leadBefore, float leadAfter)
    {
        if (leadBefore != 0f)
        {
            float factor = leadAfter / leadBefore;
            if (!float.IsNaN(factor) && !float.IsInfinity(factor))
            {
                return memberValue * factor;
            }
        }

        return memberValue + (leadAfter - leadBefore);
    }

    private static Vector3 ApplyScaleDelta(Vector3 memberValue, Vector3 leadBefore, Vector3 leadAfter)
    {
        return new Vector3(
            ApplyScaleDelta(memberValue.X, leadBefore.X, leadAfter.X),
            ApplyScaleDelta(memberValue.Y, leadBefore.Y, leadAfter.Y),
            ApplyScaleDelta(memberValue.Z, leadBefore.Z, leadAfter.Z));
    }

    private bool TryApplyGroupedLeadTransformEdit(ActorProxy actor, TransformSnapshot before, TransformSnapshot after, string description)
    {
        if (ActiveTransformGroup is null
            || !ReferenceEquals(actor, ActiveTransformGroup.LeadActor)
            || before.Equals(after))
        {
            return false;
        }

        bool locationChanged = before.Location != after.Location;
        bool rotationChanged = before.Rotation != after.Rotation;
        bool drawScaleChanged = before.DrawScale != after.DrawScale;
        bool drawScale3DChanged = before.DrawScale3D != after.DrawScale3D;

        Vector3 locationDelta = after.Location - before.Location;
        Matrix4x4 rotationDeltaMatrix = Matrix4x4.Identity;
        if (rotationChanged)
        {
            Matrix4x4 beforeRotationMatrix = before.Rotation.ToRotationMatrix();
            Matrix4x4 afterRotationMatrix = after.Rotation.ToRotationMatrix();
            if (Matrix4x4.Invert(beforeRotationMatrix, out Matrix4x4 beforeRotationInverse))
            {
                rotationDeltaMatrix = beforeRotationInverse * afterRotationMatrix;
            }
        }

        var entries = new List<(ActorProxy Actor, TransformSnapshot Before, TransformSnapshot After)>
        {
            (actor, before, after)
        };

        try
        {
            _isApplyingGroupMove = true;
            foreach (var member in ActiveTransformGroup.Members)
            {
                if (ReferenceEquals(member, actor)
                    || member is null
                    || member.IsReadOnly
                    || !Actors.Contains(member))
                {
                    continue;
                }

                TransformSnapshot memberBefore = member.SnapshotTransform();
                if (locationChanged || rotationChanged)
                {
                    Vector3 memberLocation = memberBefore.Location;
                    if (rotationChanged)
                    {
                        Vector3 relativeToLead = memberBefore.Location - before.Location;
                        memberLocation = before.Location + Vector3.Transform(relativeToLead, rotationDeltaMatrix);
                    }

                    if (locationChanged)
                    {
                        memberLocation += locationDelta;
                    }

                    member.Location = memberLocation;
                }

                if (rotationChanged)
                {
                    Matrix4x4 memberRotationMatrix = memberBefore.Rotation.ToRotationMatrix();
                    member.Rotation = (memberRotationMatrix * rotationDeltaMatrix).GetRotator();
                }

                if (drawScaleChanged)
                {
                    member.DrawScale = ApplyScaleDelta(memberBefore.DrawScale, before.DrawScale, after.DrawScale);
                }

                if (drawScale3DChanged)
                {
                    member.DrawScale3D = ApplyScaleDelta(memberBefore.DrawScale3D, before.DrawScale3D, after.DrawScale3D);
                }

                TransformSnapshot memberAfter = member.SnapshotTransform();
                if (!memberBefore.Equals(memberAfter))
                {
                    entries.Add((member, memberBefore, memberAfter));
                }
            }
        }
        finally
        {
            _isApplyingGroupMove = false;
        }

        UndoHistory.Push(new TransformBatchAction(entries, description));
        _preEditSnapshot = after;
        return true;
    }

    #region Selective Visibility

    private static string GetActorVisibilityKey(ActorProxy actor)
    {
        return $"{actor.Export.FileRef.FilePath}|{actor.Export.UIndex}";
    }

    private static bool IsMeshFilterCandidate(ActorProxy actor)
    {
        return !actor.IsVolume && !actor.IsVolumetricMesh && !actor.IsLight;
    }

    private static bool IsVisibleSetCandidate(ActorProxy actor)
    {
        return true;
    }

    private void SyncCategoryWithVisibleSetWhenActive(bool isEnabled, Func<ActorProxy, bool> predicate)
    {
        if (_suppressDisplayFilterVisibleSetSync)
        {
            return;
        }

        if (!(UseVisibleSetOnly || ObjectRenderMode is ObjectRenderMode.VisibleSetOnly))
        {
            return;
        }

        _hasUserEditedVisibleSets = true;
        if (isEnabled)
        {
            AddActorsToVisibleSet(Actors.Where(actor => IsVisibleSetCandidate(actor) && predicate(actor)));
            return;
        }

        foreach (var actor in Actors.Where(actor => IsVisibleSetCandidate(actor) && predicate(actor)))
        {
            _visibleActorSet.Remove(GetActorVisibilityKey(actor));
        }
    }

    private void AddActorsToVisibleSet(IEnumerable<ActorProxy> actors)
    {
        foreach (var actor in actors)
        {
            _visibleActorSet.Add(GetActorVisibilityKey(actor));
        }
    }

    private void RefreshVisibleSetDisplay(bool ensureVisibleSetOnly)
    {
        if (ensureVisibleSetOnly)
        {
            if (ObjectRenderMode is not ObjectRenderMode.VisibleSetOnly)
            {
                ObjectRenderMode = ObjectRenderMode.VisibleSetOnly;
            }
            else
            {
                UseVisibleSetOnly = true;
            }
        }

        if (UseVisibleSetOnly || ObjectRenderMode is ObjectRenderMode.VisibleSetOnly)
        {
            SceneViewer.SetShouldRender(false);
            SceneViewer.SetShouldRender(true);
        }
    }

    private void SyncDisplayFiltersWithVisibleSet()
    {
        bool IsVisible(ActorProxy actor) => _visibleActorSet.Contains(GetActorVisibilityKey(actor));

        _suppressDisplayFilterVisibleSetSync = true;
        try
        {
            ShowLights = Actors.Any(a => a.IsLight && IsVisible(a));
            ShowVolumes = Actors.Any(a => a.IsVolume && IsVisible(a));
            ShowVolumetrics = Actors.Any(a => a.IsVolumetricMesh && IsVisible(a));
            ShowEmitters = Actors.Any(a => a.IsEmitter && IsVisible(a));
            ShowLocationActors = Actors.Any(a => a.IsLocationActor && IsVisible(a));
            ShowSoundPositions = Actors.Any(a => a.IsAmbientSound && IsVisible(a));
            ShowCinematicActors = Actors.Any(a => a.IsCinematicActor && IsVisible(a));
            ShowDecalActors = Actors.Any(a => a.IsDecalActor && IsVisible(a));
        }
        finally
        {
            _suppressDisplayFilterVisibleSetSync = false;
        }
    }

    private void EnableDisplayFilterForActorType(ActorProxy actor)
    {
        if (actor is null)
        {
            return;
        }

        if (actor.IsLight) ShowLights = true;
        if (actor.IsVolume) ShowVolumes = true;
        if (actor.IsVolumetricMesh) ShowVolumetrics = true;
        if (actor.IsEmitter) ShowEmitters = true;
        if (actor.IsLocationActor) ShowLocationActors = true;
        if (actor.IsAmbientSound) ShowSoundPositions = true;
        if (actor.IsCinematicActor) ShowCinematicActors = true;
        if (actor.IsDecalActor) ShowDecalActors = true;
    }

    private void AddSelectedToVisibleSet()
    {
        if (SelectedActor is null) return;
        _hasUserEditedVisibleSets = true;
        _visibleActorSet.Add(GetActorVisibilityKey(SelectedActor));
        EnableDisplayFilterForActorType(SelectedActor);
        RefreshVisibleSetDisplay(ensureVisibleSetOnly: true);
    }

    private void ToggleSelectedActorVisibility()
    {
        if (SelectedActor is null)
        {
            return;
        }

        string selectedActorVisibilityKey = GetActorVisibilityKey(SelectedActor);
        if (_visibleActorSet.Contains(selectedActorVisibilityKey))
        {
            RemoveSelectedFromVisibleSet();
        }
        else
        {
            AddSelectedToVisibleSet();
        }
    }

    private void RemoveSelectedFromVisibleSet()
    {
        if (SelectedActor is null) return;
        EnsureVisibleSetInitializedForPreModeRemoval();
        _hasUserEditedVisibleSets = true;
        _visibleActorSet.Remove(GetActorVisibilityKey(SelectedActor));
        RefreshVisibleSetDisplay(ensureVisibleSetOnly: false);
    }

    private void ShowOnlySelected()
    {
        if (SelectedActor is null) return;
        _hasUserEditedVisibleSets = true;
        _visibleActorSet.Clear();
        _visibleActorSet.Add(GetActorVisibilityKey(SelectedActor));
        if (SelectedActor.IsLight)
        {
            ShowLights = true;
        }
        if (SelectedActor.IsVolume)
        {
            ShowVolumes = true;
        }
        if (SelectedActor.IsVolumetricMesh)
        {
            ShowVolumetrics = true;
        }
        RefreshVisibleSetDisplay(ensureVisibleSetOnly: true);
    }

    private void AddSelectedClassToVisibleSet()
    {
        if (SelectedActor is null) return;
        _hasUserEditedVisibleSets = true;
        string selectedClass = SelectedActor.Export.ClassName;
        _explicitlyHiddenVisibleSetClasses.Remove(selectedClass);
        AddActorsToVisibleSet(Actors.Where(actor => IsVisibleSetCandidate(actor) && actor.Export.ClassName == selectedClass));
        if (SelectedActor.IsLight)
        {
            ShowLights = true;
        }
        if (SelectedActor.IsVolume)
        {
            ShowVolumes = true;
        }
        if (SelectedActor.IsVolumetricMesh)
        {
            ShowVolumetrics = true;
        }
        RefreshVisibleSetDisplay(ensureVisibleSetOnly: true);
    }

    private void RemoveSelectedClassFromVisibleSet()
    {
        if (SelectedActor is null) return;
        EnsureVisibleSetInitializedForPreModeRemoval();
        _hasUserEditedVisibleSets = true;
        string selectedClass = SelectedActor.Export.ClassName;
        _explicitlyHiddenVisibleSetClasses.Add(selectedClass);
        foreach (var actor in Actors.Where(actor => IsVisibleSetCandidate(actor) && actor.Export.ClassName == selectedClass))
        {
            _visibleActorSet.Remove(GetActorVisibilityKey(actor));
        }
        RefreshVisibleSetDisplay(ensureVisibleSetOnly: false);
    }

    private void EnsureVisibleSetInitializedForPreModeRemoval()
    {
        if (_visibleActorSet.Count is 0 && !UseVisibleSetOnly && ObjectRenderMode is not ObjectRenderMode.VisibleSetOnly)
        {
            InitializeVisibleSetToAll();
        }
    }

    private void ShowOnlySelectedClass()
    {
        if (SelectedActor is null) return;
        _visibleActorSet.Clear();
        AddSelectedClassToVisibleSet();
    }

    private void AddNearbyToVisibleSet()
    {
        _hasUserEditedVisibleSets = true;
        Vector3 cameraPosition = RenderContext.Camera.Position;
        float maxDistanceSq = VisibleSetDistance * VisibleSetDistance;
        AddActorsToVisibleSet(Actors.Where(actor => IsMeshFilterCandidate(actor)
                                                     && Vector3.DistanceSquared(actor.Location, cameraPosition) <= maxDistanceSq));
        RefreshVisibleSetDisplay(ensureVisibleSetOnly: true);
    }

    private void ShowOnlyNearby()
    {
        RebuildVisibleSetByDistance();
    }

    private void RebuildVisibleSetByDistance()
    {
        _hasUserEditedVisibleSets = true;
        _visibleActorSet.Clear();
        Vector3 cameraPosition = RenderContext.Camera.Position;
        float maxDistanceSq = VisibleSetDistance * VisibleSetDistance;
        AddActorsToVisibleSet(Actors.Where(actor => IsMeshFilterCandidate(actor)
                                                     && Vector3.DistanceSquared(actor.Location, cameraPosition) <= maxDistanceSq));
        RefreshVisibleSetDisplay(ensureVisibleSetOnly: true);
    }

    private void InitializeVisibleSetToAll()
    {
        _visibleActorSet.Clear();
        _explicitlyHiddenVisibleSetClasses.Clear();
        AddActorsToVisibleSet(Actors.Where(IsVisibleSetCandidate));
    }

    private void OpenVisibleSetsManager()
    {
        List<string> allClasses = Actors.Select(a => a.Export.ClassName)
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Distinct()
            .OrderBy(c => c)
            .ToList();
        if (allClasses.Count is 0)
        {
            return;
        }

        if (_visibleActorSet.Count is 0 && !_hasUserEditedVisibleSets)
        {
            InitializeVisibleSetToAll();
        }

        HashSet<string> currentlyVisibleClasses = Actors
            .Where(actor => _visibleActorSet.Contains(GetActorVisibilityKey(actor)))
            .Select(actor => actor.Export.ClassName)
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .ToHashSet();

        HashSet<string> explicitlyHiddenActorKeys = Actors
            .Where(actor => currentlyVisibleClasses.Contains(actor.Export.ClassName)
                            && !_visibleActorSet.Contains(GetActorVisibilityKey(actor)))
            .Select(GetActorVisibilityKey)
            .ToHashSet();

        HashSet<string> explicitlyVisibleActorKeys = Actors
            .Where(actor => !currentlyVisibleClasses.Contains(actor.Export.ClassName)
                            && _visibleActorSet.Contains(GetActorVisibilityKey(actor)))
            .Select(GetActorVisibilityKey)
            .ToHashSet();

        var dialog = new VisibleSetsManagerDialog(allClasses, currentlyVisibleClasses, this);
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        HashSet<string> desiredVisibleClasses = dialog.GetVisibleClasses();
        _hasUserEditedVisibleSets = true;
        _explicitlyHiddenVisibleSetClasses.Clear();
        _explicitlyHiddenVisibleSetClasses.UnionWith(allClasses.Where(c => !desiredVisibleClasses.Contains(c)));
        foreach (ActorProxy actor in Actors)
        {
            string key = GetActorVisibilityKey(actor);
            if (desiredVisibleClasses.Contains(actor.Export.ClassName))
            {
                _visibleActorSet.Add(key);
            }
            else
            {
                _visibleActorSet.Remove(key);
            }
        }

        if (!dialog.ClearActorLevelSettingsRequested)
        {
            foreach (ActorProxy actor in Actors)
            {
                string key = GetActorVisibilityKey(actor);
                bool classIsVisible = desiredVisibleClasses.Contains(actor.Export.ClassName);
                if (classIsVisible && explicitlyHiddenActorKeys.Contains(key))
                {
                    _visibleActorSet.Remove(key);
                }
                else if (!classIsVisible && explicitlyVisibleActorKeys.Contains(key))
                {
                    _visibleActorSet.Add(key);
                }
            }
        }

        SyncDisplayFiltersWithVisibleSet();
        RefreshVisibleSetDisplay(ensureVisibleSetOnly: true);
    }

    private void ClearVisibleSet()
    {
        _hasUserEditedVisibleSets = true;
        _visibleActorSet.Clear();
        SyncDisplayFiltersWithVisibleSet();
        RefreshVisibleSetDisplay(ensureVisibleSetOnly: false);
    }

    private void ShowAllMeshes()
    {
        _hasUserEditedVisibleSets = true;
        _visibleActorSet.Clear();
        _explicitlyHiddenVisibleSetClasses.Clear();
        AddActorsToVisibleSet(Actors.Where(IsVisibleSetCandidate));
        SyncDisplayFiltersWithVisibleSet();
        RefreshVisibleSetDisplay(ensureVisibleSetOnly: false);
    }

    #endregion

    #region Undo/Redo
    public readonly UndoHistory UndoHistory = new();
    private TransformSnapshot? _preEditSnapshot;
    private bool _isApplyingUndoRedo;
    public bool IsApplyingUndoRedo => _isApplyingUndoRedo;

    public bool CanUndo => UndoHistory.CanUndo;
    public bool CanRedo => UndoHistory.CanRedo;

    private void UndoHistory_PropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
    }

    public void Undo_Clicked(object sender, RoutedEventArgs e) => Undo();
    public void Undo()
    {
        if (UndoHistory.CanUndo)
        {
            _isApplyingUndoRedo = true;
            UndoHistory.Undo();
            _isApplyingUndoRedo = false;
            if (SelectedActor is not null)
            {
                _preEditSnapshot = SelectedActor.SnapshotTransform();
            }
        }
    }

    public void Redo_Clicked(object sender, RoutedEventArgs e) => Redo();
    void Redo()
    {
        if (UndoHistory.CanRedo)
        {
            _isApplyingUndoRedo = true;
            UndoHistory.Redo();
            _isApplyingUndoRedo = false;
            if (SelectedActor is not null)
            {
                _preEditSnapshot = SelectedActor.SnapshotTransform();
            }
        }
    }
    #endregion

    #region Load Related Levels

    private async void LoadRelatedLevels()
    {
        if (OpenFiles.Count == 0) return;

        var firstFile = OpenFiles[0];
        string rootFilename = firstFile.FileName;
        MEGame game = Game;

        if (rootFilename.StartsWith("Bio") && rootFilename.Length > 3
            && rootFilename[3] is 'P' or 'D' or 'A' or 'S'
            && rootFilename.Split('_') is [_, string levelIdent, ..]
            && levelIdent.Split('.') is [string realLevelIdent, ..])
        {
            List<(string filename, string path)> candidates = [];
            var regex = new Regex($"^Bio[PDA]_{realLevelIdent}", RegexOptions.IgnoreCase | RegexOptions.Compiled);
            var openFilePaths = OpenFiles.Select(f => Path.GetFileName(f.FilePath)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach ((string filename, string path) in MELoadedFiles.GetFilesLoadedInGame(game))
            {
                if (regex.IsMatch(filename) && !filename.Contains("_LOC_", StringComparison.OrdinalIgnoreCase) && !openFilePaths.Contains(filename))
                {
                    candidates.Add((filename, path));
                }
            }
            if (candidates.Count is 0) return;

            var dialogItems = candidates.Select(c => new CheckedListItem
            {
                DisplayName = c.filename,
                IsSelected = true,
                Tag = c.path
            }).ToList();

            var dialog = new CheckedListDialog(dialogItems, "Load Related Levels",
                $"Select levels related to {realLevelIdent} to load:", this);

            if (dialog.ShowDialog() != true) return;

            var selectedPaths = dialog.GetSelectedItems().Select(i => (string)i.Tag).ToList();
            if (selectedPaths.Count is 0) return;

            using var guard = new RenderGuard(this);

            foreach (string path in selectedPaths)
            {
                await AddLevelFile(path).ConfigureAwait(true);
            }
        }
    }

    #endregion

    #region Commit & Save

    private void CommitChanges()
    {
        if (!PackageIsLoaded() || Actors.Count is 0) return;

        foreach (var file in OpenFiles)
        {
            if (!file.IsReadOnly)
                CommitChangesForFile(file);
        }
        IsDirty = false;
    }

    private void CommitChangesForFile(OpenLevelFile file)
    {
        Dictionary<int, StaticCollectionActor> collectionActorMap = [];

        foreach (ActorProxy actor in file.Actors)
        {
            if (!actor.IsDirty)
            {
                continue;
            }
            if (actor is CollectionActorComponentProxy cacp)
            {
                if (!collectionActorMap.TryGetValue(cacp.CollectionActorExport.UIndex, out var collectionActor))
                {
                    collectionActor = (StaticCollectionActor)ObjectBinary.From(cacp.CollectionActorExport);
                    collectionActorMap.Add(cacp.CollectionActorExport.UIndex, collectionActor);
                }
                cacp.CommitChanges(collectionActor);
            }
            else
            {
                actor.CommitChanges();
            }
            actor.MarkClean();
        }

        foreach (var collectionActor in collectionActorMap.Values)
        {
            collectionActor.Export.WriteBinary(collectionActor);
        }
        file.IsDirty = false;
    }

    private void CommitSingleFileExecute(object parameter)
    {
        OpenLevelFile file = ResolveFileParameter(parameter);
        if (file is not null && !file.IsReadOnly)
        {
            CommitChangesForFile(file);
        }
    }

    private async void SaveAllFiles()
    {
        if (IsDirty)
        {
            switch (MessageBox.Show("Do you want to commit your Level Editor changes before saving all files?", "Uncommitted changes", MessageBoxButton.YesNoCancel))
            {
                case MessageBoxResult.Yes:
                    CommitChanges();
                    break;
                case MessageBoxResult.No:
                    break;
                case MessageBoxResult.Cancel:
                default:
                    return;
            }
        }
        foreach (var file in OpenFiles)
        {
            if (!file.IsReadOnly && file.Package.IsModified)
            {
                await file.Package.SaveAsync();
            }
        }
    }

    private async void SaveSingleFileExecute(object parameter)
    {
        OpenLevelFile file = ResolveFileParameter(parameter);
        if (file is null || file.IsReadOnly) return;

        if (file.IsDirty)
        {
            switch (MessageBox.Show($"Do you want to commit changes to {file.FileName} before saving?", "Uncommitted changes", MessageBoxButton.YesNoCancel))
            {
                case MessageBoxResult.Yes:
                    CommitChangesForFile(file);
                    break;
                case MessageBoxResult.No:
                    break;
                case MessageBoxResult.Cancel:
                default:
                    return;
            }
        }
        await file.Package.SaveAsync();
    }

    private void CloseFileExecute(object parameter)
    {
        OpenLevelFile file = ResolveFileParameter(parameter);
        if (file is not null)
        {
            CloseFile(file);
        }
    }

    private OpenLevelFile ResolveFileParameter(object parameter)
    {
        if (parameter is OpenLevelFile file) return file;
        if (parameter is string fileName)
        {
            return OpenFiles.FirstOrDefault(f => f.FileName == fileName);
        }
        return null;
    }

    private async void SaveFileAs()
    {
        if (OpenFiles.Count == 0) return;

        // Save As applies to the first file when only one is open,
        // otherwise prompt which file to save
        OpenLevelFile fileToSave;
        if (OpenFiles.Count == 1)
        {
            fileToSave = OpenFiles[0];
        }
        else
        {
            // For multi-file, Save As saves all files to a chosen directory
            // For simplicity, just save the selected actor's file, or the first file
            fileToSave = SelectedActor?.OwningFile ?? OpenFiles[0];
        }

        if (fileToSave.IsDirty)
        {
            switch (MessageBox.Show($"Do you want to commit changes to {fileToSave.FileName} before saving?", "Uncommitted changes", MessageBoxButton.YesNoCancel))
            {
                case MessageBoxResult.Yes:
                    CommitChangesForFile(fileToSave);
                    break;
                case MessageBoxResult.No:
                    break;
                case MessageBoxResult.Cancel:
                default:
                    return;
            }
        }

        string fileFilter;
        switch (fileToSave.Package.Game)
        {
            case MEGame.ME1:
                fileFilter = GameFileFilters.ME1SaveFileFilter;
                break;
            case MEGame.ME2:
            case MEGame.ME3:
                fileFilter = GameFileFilters.ME3ME2SaveFileFilter;
                break;
            default:
                string extension = Path.GetExtension(fileToSave.FilePath);
                fileFilter = $"*{extension}|*{extension}";
                break;
        }
        var d = new SaveFileDialog { Filter = fileFilter };
        if (d.ShowDialog() == true)
        {
            IsBusy = true;
            BusyText = "Saving...";
            await fileToSave.Package.SaveAsync(d.FileName);
            IsBusy = false;
        }
    }

    #endregion

    #region HandleUpdate

    public void HandleFileUpdate(OpenLevelFile file, List<PackageUpdate> updates)
    {
        if (file.LevelExport is null) return;

        IEnumerable<PackageUpdate> relevantUpdates = updates.Where(x => x.Change.Has(PackageChange.Export));
        HashSet<int> updatedExports = relevantUpdates.Select(x => x.Index).ToHashSet();
        HashSet<string> preservedVisibleActorKeys = [];
        if (updatedExports.Contains(file.LevelExport.UIndex))
        {
            ReloadFile(file);
        }
        else
        {
            bool updated = false;
            int reselectUIndex = 0;
            (Vector3, float, float) savedCamPOV = default;
            Vector3 savedActorPos = default;
            HashSet<int> collectionActorUIndexesToUpdate = [];
            for (int i = file.Actors.Count - 1; i >= 0; i--)
            {
                ActorProxy alteredActor = file.Actors[i];
                if (alteredActor.TestUIndexes(updatedExports))
                {
                    updated = true;
                    string actorVisibilityKey = GetActorVisibilityKey(alteredActor);
                    if (_visibleActorSet.Contains(actorVisibilityKey))
                    {
                        preservedVisibleActorKeys.Add(actorVisibilityKey);
                    }
                    if (alteredActor == SelectedActor)
                    {
                        reselectUIndex = alteredActor.Export.UIndex;
                        savedCamPOV = (RenderContext.Camera.Position, RenderContext.Camera.Pitch, RenderContext.Camera.Yaw);
                        savedActorPos = SelectedActor.Location;
                    }
                    if (alteredActor is CollectionActorComponentProxy cacp)
                    {
                        collectionActorUIndexesToUpdate.Add(cacp.CollectionActorExport.UIndex);
                        continue;
                    }
                    RemoveActor(alteredActor);
                    if (file.Package.GetEntry(alteredActor.Export.UIndex) is ExportEntry actorExport
                        && ActorProxy.Create(this, actorExport) is { } actorProxy)
                    {
                        actorProxy.OwningFile = file;
                        AddActor(actorProxy);
                        if (preservedVisibleActorKeys.Contains(actorVisibilityKey))
                        {
                            _visibleActorSet.Add(actorVisibilityKey);
                        }
                    }
                }
            }
            foreach (int collectionActorUIndex in collectionActorUIndexesToUpdate)
            {
                for (int i = file.Actors.Count - 1; i >= 0; i--)
                {
                    if (file.Actors[i] is CollectionActorComponentProxy cacp
                        && cacp.CollectionActorExport.UIndex == collectionActorUIndex)
                    {
                        string actorVisibilityKey = GetActorVisibilityKey(file.Actors[i]);
                        if (_visibleActorSet.Contains(actorVisibilityKey))
                        {
                            preservedVisibleActorKeys.Add(actorVisibilityKey);
                        }
                        RemoveActor(file.Actors[i]);
                    }
                }
                if (file.Package.GetEntry(collectionActorUIndex) is ExportEntry newCollectionActor)
                {
                    string className = newCollectionActor.ClassName;
                    if (className is "StaticMeshCollectionActor")
                    {
                        var smca = newCollectionActor.GetBinaryData<StaticMeshCollectionActor>();
                        for (int i = 0; i < smca.Components.Count; i++)
                        {
                            if (file.Package.TryGetUExport(smca.Components[i], out ExportEntry smcExport))
                            {
                                var smcActor = new StaticMeshComponentActorProxy(this, smcExport, smca, i);
                                smcActor.OwningFile = file;
                                AddActor(smcActor, false);
                                string actorVisibilityKey = GetActorVisibilityKey(smcActor);
                                if (preservedVisibleActorKeys.Contains(actorVisibilityKey))
                                {
                                    _visibleActorSet.Add(actorVisibilityKey);
                                }
                            }
                        }
                    }
                    else if (className is "StaticLightCollectionActor")
                    {
                        var slca = newCollectionActor.GetBinaryData<StaticLightCollectionActor>();
                        for (int i = 0; i < slca.Components.Count; i++)
                        {
                            if (file.Package.TryGetUExport(slca.Components[i], out ExportEntry lightComponentExport))
                            {
                                var lightActor = new StaticLightComponentActorProxy(this, lightComponentExport, slca, i);
                                lightActor.OwningFile = file;
                                AddActor(lightActor, false);
                                string actorVisibilityKey = GetActorVisibilityKey(lightActor);
                                if (preservedVisibleActorKeys.Contains(actorVisibilityKey))
                                {
                                    _visibleActorSet.Add(actorVisibilityKey);
                                }
                            }
                        }
                    }
                }
            }
            if (updated)
            {
                SortActorsByFileOrderThenUIndex();
                UpdateGlobalDirtyState();
            }
            if (reselectUIndex is not 0)
            {
                SelectedActor = Actors.FirstOrDefault(a => a.Export.UIndex == reselectUIndex && a.Export.FileRef == file.Package);
                if (SelectedActor is not null)
                {
                    (RenderContext.Camera.Position, RenderContext.Camera.Pitch, RenderContext.Camera.Yaw)
                    = (savedCamPOV.Item1 + SelectedActor.Location - savedActorPos, savedCamPOV.Item2, savedCamPOV.Item3);
                }
            }
        }
    }

    private void ReloadFile(OpenLevelFile file)
    {
        // Remove all actors for this file, then re-load
        (Vector3, float, float) savedCamPOV = default;
        Vector3 savedActorPos = default;
        int reselectUIndex = 0;
        HashSet<string> existingFileActorKeys = file.Actors.Select(GetActorVisibilityKey).ToHashSet();
        HashSet<string> preservedVisibleActorKeys = existingFileActorKeys
            .Where(_visibleActorSet.Contains)
            .ToHashSet();
        if (SelectedActor is not null && file.Actors.Contains(SelectedActor))
        {
            savedCamPOV = (RenderContext.Camera.Position, RenderContext.Camera.Pitch, RenderContext.Camera.Yaw);
            savedActorPos = SelectedActor.Location;
            reselectUIndex = SelectedActor.Export.UIndex;
            SelectedActor = null;
        }

        foreach (var actor in file.Actors.ToList())
        {
            Actors.Remove(actor);
            RenderContext.RemoveActor(actor);
            actor.Dispose();
        }
        ReevaluateActiveGroup();
        file.Actors.Clear();

        Level levelBin = file.LevelExport.GetBinaryData<Level>();
        var (actors, _) = LoadActors(levelBin, file);
        var sorted = actors.OrderBy(a => a.Export.UIndex).ToList();
        file.Actors.AddRange(sorted);
        Actors.AddRange(sorted);
        RenderContext.LoadActors(sorted);

        _visibleActorSet.ExceptWith(existingFileActorKeys);
        foreach (var actor in sorted)
        {
            string actorVisibilityKey = GetActorVisibilityKey(actor);
            if (preservedVisibleActorKeys.Contains(actorVisibilityKey))
            {
                _visibleActorSet.Add(actorVisibilityKey);
            }
        }

        if (reselectUIndex is not 0)
        {
            var reselect = Actors.FirstOrDefault(a => a.Export.UIndex == reselectUIndex && a.Export.FileRef == file.Package);
            if (reselect is not null)
            {
                SelectedActor = reselect;
                (RenderContext.Camera.Position, RenderContext.Camera.Pitch, RenderContext.Camera.Yaw)
                = (savedCamPOV.Item1 + reselect.Location - savedActorPos, savedCamPOV.Item2, savedCamPOV.Item3);
            }
        }

        file.IsDirty = false;

        if (UseVisibleSetOnly || ObjectRenderMode is ObjectRenderMode.VisibleSetOnly)
        {
            SyncDisplayFiltersWithVisibleSet();
        }
    }

    #endregion

    public void UpdateGlobalDirtyState()
    {
        IsDirty = OpenFiles.Any(f => f.IsDirty);
    }

    private bool PackageIsLoaded() => OpenFiles.Count > 0;

    private void OnActorPropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        if (_isApplyingUndoRedo || _isApplyingGroupMove || RenderContext.TransformWidget.IsDragging) return;
        if (e.PropertyName is not (nameof(ActorProxy.Location) or nameof(ActorProxy.Rotation) or nameof(ActorProxy.DrawScale) or nameof(ActorProxy.DrawScale3D))) return;

        if (sender is ActorProxy actor && _preEditSnapshot is { } before)
        {
            var after = actor.SnapshotTransform();
            if (!before.Equals(after))
            {
                if (TryApplyGroupedLeadTransformEdit(actor, before, after, $"Edit group ({ActiveTransformGroup?.Name ?? "Lead"})"))
                {
                    return;
                }

                UndoHistory.Push(new TransformAction(actor, before, after, $"Edit {actor.Export.ObjectName.Instanced}"));
                _preEditSnapshot = after;
            }
        }
    }

    private void OnWidgetDragComplete(ActorProxy actor, TransformSnapshot before, TransformSnapshot after)
    {
        if (before.Equals(after)) return;

        if (TryApplyGroupedLeadTransformEdit(actor, before, after, $"Drag group ({ActiveTransformGroup?.Name ?? "Lead"})"))
        {
            return;
        }

        UndoHistory.Push(new TransformAction(actor, before, after, $"Drag {actor.Export.ObjectName.Instanced}"));
        _preEditSnapshot = after;
    }

    private bool ActorFilter(object obj)
    {
        if (string.IsNullOrEmpty(_actorFilterText)) return true;
        return obj is ActorProxy actor &&
               actor.DisplayText.Contains(_actorFilterText, StringComparison.OrdinalIgnoreCase);
    }

    private void ActorFilter_TextBox_KeyUp(object sender, KeyEventArgs e)
    {
        _actorFilterText = ActorFilter_TextBox.Text;
        ActorsView.Refresh();
    }

    private void Goto_TextBox_KeyUp(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Return && !e.IsRepeat)
        {
            GotoButton_Clicked(null, null);
        }
    }

    private void GotoButton_Clicked(object sender, RoutedEventArgs e)
    {
        if (int.TryParse(Goto_TextBox.Text, out int uIdx)
            && Actors.FirstOrDefault(a => a.Export.UIndex == uIdx) is ActorProxy actor)
        {
            SelectedActor = actor;
        }
    }

    private void DisplayFilters_ComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox comboBox && comboBox.SelectedIndex != 0)
        {
            comboBox.SelectedIndex = 0;
        }
    }

    private void DisplayFilters_ComboBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject source)
        {
            return;
        }

        var checkbox = GetAncestor<CheckBox>(source);
        if (checkbox is null)
        {
            return;
        }

        checkbox.IsChecked = !(checkbox.IsChecked ?? false);
        e.Handled = true;
    }

    private void MeshExportsList_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not ListBox listBox || e.OriginalSource is not DependencyObject source)
        {
            return;
        }

        if (ItemsControl.ContainerFromElement(listBox, source) is ListBoxItem { DataContext: ActorProxy actor } listBoxItem)
        {
            if (!listBoxItem.IsSelected)
            {
                _suppressSelectionFocus = true;
                listBox.SelectedItem = actor;
            }
        }
    }

    private void MeshExportsList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not ListBox listBox || e.OriginalSource is not DependencyObject source)
        {
            return;
        }

        if ((Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) != ModifierKeys.None)
        {
            return;
        }

        if (ItemsControl.ContainerFromElement(listBox, source) is ListBoxItem { DataContext: ActorProxy actor } listBoxItem)
        {
            _suppressSelectionFocus = true;
            if (!listBoxItem.IsSelected || listBox.SelectedItems.Count > 1)
            {
                listBox.SelectedItem = actor;
            }
        }
    }

    private void MeshExportsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ListBox listBox)
        {
            List<ActorProxy> selectedActors = listBox.SelectedItems.OfType<ActorProxy>().ToList();
            ActorProxy primarySelection = selectedActors.LastOrDefault();
            if (primarySelection is null && listBox.SelectedItem is ActorProxy fallbackSelection)
            {
                primarySelection = fallbackSelection;
            }

            if (!ReferenceEquals(SelectedActor, primarySelection))
            {
                _suppressSelectionFocus = true;
                SelectedActor = primarySelection;
            }
        }

        UpdateGroupableSelectionCount();

        if (_isUpdatingGroupSelection)
        {
            return;
        }

        if (ActiveTransformGroup is not null)
        {
            List<ActorProxy> selectedGroupableActors = GetGroupableSelectedActors();
            if (selectedGroupableActors.Count >= 2)
            {
                ActorProxy leadActor = SelectedActor is not null && selectedGroupableActors.Contains(SelectedActor)
                    ? SelectedActor
                    : selectedGroupableActors[0];
                ActiveTransformGroup = new ActorTransformGroup(ActiveTransformGroup.Name, leadActor, selectedGroupableActors);
                RenderContext.TransformWidget.Attach = leadActor;
            }
        }

        ReevaluateActiveGroup();
        if (ActiveTransformGroup is not null
            && SelectedActor is not null
            && ActiveTransformGroup.Members.Contains(SelectedActor)
            && !ReferenceEquals(ActiveTransformGroup.LeadActor, SelectedActor))
        {
            ActiveTransformGroup = new ActorTransformGroup(ActiveTransformGroup.Name, SelectedActor, ActiveTransformGroup.Members.ToList());
            RenderContext.TransformWidget.Attach = SelectedActor;
        }

        InvalidateSelectionCommands();
    }

    private void MeshExportsList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not ListBox listBox || e.OriginalSource is not DependencyObject source)
        {
            return;
        }

        if (ItemsControl.ContainerFromElement(listBox, source) is not ListBoxItem { DataContext: ActorProxy actor })
        {
            return;
        }

        _suppressSelectionFocus = true;
        listBox.SelectedItem = actor;

        if (FocusSelectedCommand?.CanExecute(null) == true)
        {
            FocusSelectedCommand.Execute(null);
        }
    }

    private void CameraCoordinatesMenuGlyph_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { ContextMenu: { } contextMenu } element)
        {
            contextMenu.PlacementTarget = element;
            contextMenu.Placement = PlacementMode.Bottom;
            contextMenu.IsOpen = true;
            e.Handled = true;
        }
    }

    private void CopyCameraCoordinatesMenuItem_Click(object sender, RoutedEventArgs e)
    {
        Vector3 position = RenderContext.Camera.Position;
        copiedCoordinates = position;
        string text = $"X={position.X:F1} | Y={position.Y:F1} | Z={position.Z:F1}";
        Clipboard.SetText(text);
    }

    private void CopyLocationCoordinatesButton_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedActor is null)
        {
            MessageBox.Show(this, "Select an actor first.", "Copy Location Coordinates", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        copiedCoordinates = SelectedActor.Location;
    }

    private void PasteLocationCoordinatesButton_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedActor is null)
        {
            MessageBox.Show(this, "Select an actor first.", "Paste Location Coordinates", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (SelectedActor.IsReadOnly)
        {
            MessageBox.Show(this, "The selected actor is read-only and cannot be edited.", "Paste Location Coordinates", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!copiedCoordinates.HasValue)
        {
            MessageBox.Show(this, "No copied coordinates are available yet.", "Paste Location Coordinates", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        SelectedActor.Location = copiedCoordinates.Value;
    }

    private void CopyRotationCoordinatesButton_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedActor is null)
        {
            MessageBox.Show(this, "Select an actor first.", "Copy Rotation Coordinates", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        copiedRotation = SelectedActor.Rotation;
    }

    private void PasteRotationCoordinatesButton_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedActor is null)
        {
            MessageBox.Show(this, "Select an actor first.", "Paste Rotation Coordinates", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (SelectedActor.IsReadOnly)
        {
            MessageBox.Show(this, "The selected actor is read-only and cannot be edited.", "Paste Rotation Coordinates", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!copiedRotation.HasValue)
        {
            MessageBox.Show(this, "No copied rotation is available yet.", "Paste Rotation Coordinates", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        SelectedActor.Rotation = copiedRotation.Value;
    }

    private void PasteCameraCoordinatesMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedActor is null)
        {
            MessageBox.Show(this, "Select an actor first.", "Paste Camera Coordinates", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (SelectedActor.IsReadOnly)
        {
            MessageBox.Show(this, "The selected actor is read-only and cannot be edited.", "Paste Camera Coordinates", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        string clipboardText = Clipboard.ContainsText() ? Clipboard.GetText() : string.Empty;
        if (!TryParseCoordinatesFromText(clipboardText, out Vector3 coordinates))
        {
            MessageBox.Show(this, "Clipboard does not contain valid X/Y/Z coordinates.", "Paste Camera Coordinates", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        MessageBoxResult result = MessageBox.Show(this,
            $"Paste coordinates to selected actor '{SelectedActor.Export.ObjectName.Instanced}'?",
            "Confirm Paste Coordinates",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (result is not MessageBoxResult.Yes)
        {
            return;
        }

        SelectedActor.Location = coordinates;
    }

    private static bool TryParseCoordinatesFromText(string text, out Vector3 coordinates)
    {
        coordinates = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        float? x = null;
        float? y = null;
        float? z = null;
        MatchCollection matches = CoordinatePasteRegex.Matches(text);
        foreach (Match match in matches)
        {
            if (!match.Success || match.Groups.Count < 3)
            {
                continue;
            }

            string axisText = match.Groups[1].Value;
            string valueText = match.Groups[2].Value.Replace(',', '.');
            if (!float.TryParse(valueText, NumberStyles.Float, CultureInfo.InvariantCulture, out float value))
            {
                continue;
            }

            switch (axisText.ToUpperInvariant())
            {
                case "X":
                    x = value;
                    break;
                case "Y":
                    y = value;
                    break;
                case "Z":
                    z = value;
                    break;
            }
        }

        if (x is null || y is null || z is null)
        {
            return false;
        }

        coordinates = new Vector3(x.Value, y.Value, z.Value);
        return true;
    }

    #region Open / Drag-Drop

    private async void OpenFile()
    {
        var d = AppDirectories.GetOpenPackageDialog();
        if (d.ShowDialog() == true)
        {
#if !DEBUG
            try
            {
#endif
            await LoadFileAsync(d.FileName);
#if !DEBUG
            }
            catch (Exception ex)
            {
                MessageBox.Show("Unable to open file:\n" + ex.Message);
            }
#endif
        }
    }

    private async void AddFile()
    {
        var d = AppDirectories.GetOpenPackageDialog();
        if (d.ShowDialog() == true)
        {
#if !DEBUG
            try
            {
#endif

            using var guard = new RenderGuard(this);
            await AddLevelFile(d.FileName).ConfigureAwait(true);
#if !DEBUG
            }
            catch (Exception ex)
            {
                MessageBox.Show("Unable to open file:\n" + ex.Message);
            }
#endif
        }
    }

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var files = (string[])e.Data.GetData(DataFormats.FileDrop);
            string ext = Path.GetExtension(files[0]).ToLower();
            if (ext != ".upk" && ext != ".pcc" && ext != ".sfm")
            {
                e.Effects = DragDropEffects.None;
                e.Handled = true;
            }
        }
        else
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
        }
    }

    private async void Window_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            bool isFirst = true;
            var files = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (files.Length is 0) return;
            if (PackageIsLoaded())
            {
                string q = files.Length is 1 ? "these files" : "";
                var result = MessageBox.Show("Do you want to add" + q + "to the existing level view? Select no to unload all open files first.", "Add to files?", MessageBoxButton.YesNoCancel);
                if (result == MessageBoxResult.Cancel) return;
                isFirst = result == MessageBoxResult.No;
            }
            foreach (string file in files)
            {
                string ext = Path.GetExtension(file).ToLower();
                if (ext is not (".upk" or ".pcc" or ".sfm")) continue;

                if (isFirst && OpenFiles.Count == 0)
                {
                    await LoadFileAsync(file);
                    isFirst = false;
                }
                else
                {
                    using var guard = new RenderGuard(this);

                    await AddLevelFile(file).ConfigureAwait(true);
                    isFirst = false;
                }
            }
        }
    }

    #endregion

    #region Window Lifecycle

    private void LevelEditor_Closing(object sender, CancelEventArgs e)
    {
        if (e.Cancel) return;

        var dirtyFiles = OpenFiles.Where(f => f.IsDirty || f.Package.IsModified).ToList();
        if (dirtyFiles.Count > 0)
        {
            string fileNames = string.Join(",\n", dirtyFiles.Select(f => f.FileName));
            var result = MessageBox.Show(this,
                $"The following files have unsaved changes:\n{fileNames}\n\nClose anyway?",
                "Unsaved Changes", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result == MessageBoxResult.No)
            {
                e.Cancel = true;
                return;
            }
        }

        PersistCurrentRecentViewState();

        CloseAllFiles();

        RenderContext.UpdateScene -= UpdateScene;
        RenderContext.RenderScene -= RenderScene;
        RenderContext.SelectActor -= ViewportActorSelect;
        RenderContext.FocusActor -= ViewportActorFocus;
        RenderContext.SelectBioStageMarker -= ViewportBioStageMarkerSelect;

        UndoHistory.PropertyChanged -= UndoHistory_PropertyChanged;
        UndoHistory.Clear();

        SceneViewer.Dispose();
    }

    private void LevelEditor_Loaded(object sender, RoutedEventArgs e)
    {
        RenderContext.UpdateScene += UpdateScene;
        RenderContext.RenderScene += RenderScene;
        RenderContext.SelectActor += ViewportActorSelect;
        RenderContext.FocusActor += ViewportActorFocus;
        RenderContext.SelectBioStageMarker += ViewportBioStageMarkerSelect;

        if (!string.IsNullOrEmpty(FileQueuedForLoad))
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
            {
                LoadFileAsync(FileQueuedForLoad);
                FileQueuedForLoad = null;

                Activate();
            }));
        }
    }

    #endregion

    #region Recent File Sets

    private void LoadRecentSets()
    {
        if (!File.Exists(RecentSetsFile)) return;
        try
        {
            var json = File.ReadAllText(RecentSetsFile);
            var sets = JsonConvert.DeserializeObject<List<RecentFileSet>>(json);
            if (sets is null) return;
            foreach (var set in sets)
            {
                set.FilePaths.RemoveAll(p => !File.Exists(p));
                set.ReadOnlyFilePaths ??= [];
                if (set.ViewState is not null)
                {
                    set.ViewState.VisibleActorKeys ??= [];
                }
                if (set.FilePaths.Count > 0)
                    RecentSets.Add(set);
            }
        }
        catch { /* corrupt file, ignore */ }
        RefreshRecentsMenu();
    }

    private void SaveRecentSets()
    {
        var json = JsonConvert.SerializeObject(RecentSets.ToList(), Formatting.Indented);
        File.WriteAllText(RecentSetsFile, json);
        RefreshRecentsMenu();
    }

    private void RecordCurrentFilesAsRecent()
    {
        if (OpenFiles.Count == 0) return;
        var currentPaths = OpenFiles.Select(f => f.FilePath).ToList();

        for (int i = 0; i < RecentSets.Count; i++)
        {
            var existing = RecentSets[i].FilePaths;
            if (existing.Count > 0 && existing[0] == currentPaths[0])
            {
                RecentSets.RemoveAt(i);
            }
        }

        RecentSets.Insert(0, new RecentFileSet
        {
            Game = Game,
            FilePaths = currentPaths,
            ReadOnlyFilePaths = OpenFiles.Where(f => f.IsReadOnly).Select(f => f.FilePath).ToList(),
            ViewState = CaptureCurrentViewState()
        });

        while (RecentSets.Count > 10)
            RecentSets.RemoveAt(RecentSets.Count - 1);

        SaveRecentSets();
    }

    private async void OpenRecentFileSet(RecentFileSet set)
    {
        PersistCurrentRecentViewState();
        CloseAllFiles();

        using var guard = new RenderGuard(this);

        foreach (string path in set.FilePaths)
        {
            if (File.Exists(path))
            {
                await AddLevelFile(path).ConfigureAwait(true);
                var openFile = OpenFiles.LastOrDefault(f => f.FilePath == path);
                if (openFile is not null && set.ReadOnlyFilePaths.Contains(path))
                    openFile.IsReadOnly = true;
            }
        }

        ApplyViewState(set.ViewState);
        RecordCurrentFilesAsRecent();
    }

    private void PersistCurrentRecentViewState()
    {
        if (OpenFiles.Count == 0)
        {
            return;
        }

        var currentPaths = OpenFiles.Select(f => f.FilePath).ToList();
        var existing = RecentSets.FirstOrDefault(set => set.FilePaths.Count > 0 && set.FilePaths[0] == currentPaths[0]);
        if (existing is null)
        {
            RecordCurrentFilesAsRecent();
            return;
        }

        existing.Game = Game;
        existing.FilePaths = currentPaths;
        existing.ReadOnlyFilePaths = OpenFiles.Where(f => f.IsReadOnly).Select(f => f.FilePath).ToList();
        existing.ViewState = CaptureCurrentViewState();
        SaveRecentSets();
    }

    private RecentViewState CaptureCurrentViewState()
    {
        Vector3 cameraPosition = RenderContext.Camera.Position;
        return new RecentViewState
        {
            CameraX = cameraPosition.X,
            CameraY = cameraPosition.Y,
            CameraZ = cameraPosition.Z,
            CameraYaw = RenderContext.Camera.Yaw,
            CameraPitch = RenderContext.Camera.Pitch,
            CameraOrthoWidth = RenderContext.Camera.OrthoWidth,
            IsOrthographicView = IsOrthographicView,
            ObjectRenderMode = ObjectRenderMode,
            UseVisibleSetOnly = UseVisibleSetOnly,
            HasUserEditedVisibleSets = _hasUserEditedVisibleSets,
            VisibleSetDistance = VisibleSetDistance,
            VisibleActorKeys = _visibleActorSet.ToList(),
            HiddenActorClasses = _explicitlyHiddenVisibleSetClasses.ToList(),
            ShowLights = ShowLights,
            LightRenderDistance = LightRenderDistance,
            ShowVolumes = ShowVolumes,
            ShowVolumetrics = ShowVolumetrics,
            ShowEmitters = ShowEmitters,
            ShowLocationActors = ShowLocationActors,
            ShowSoundPositions = ShowSoundPositions,
            ShowCinematicActors = ShowCinematicActors,
            ShowDecalActors = ShowDecalActors,
            ShowStageNodes = ShowStageNodes,
            ShowStageCameras = ShowStageCameras,
            ShowCollision = ShowCollision
        };
    }

    private void ApplyViewState(RecentViewState viewState)
    {
        if (viewState is null)
        {
            return;
        }

        ShowLights = viewState.ShowLights;
        LightRenderDistance = viewState.LightRenderDistance;
        ShowVolumes = viewState.ShowVolumes;
        ShowVolumetrics = viewState.ShowVolumetrics;
        ShowEmitters = viewState.ShowEmitters;
        ShowLocationActors = viewState.ShowLocationActors;
        ShowSoundPositions = viewState.ShowSoundPositions;
        ShowCinematicActors = viewState.ShowCinematicActors;
        ShowDecalActors = viewState.ShowDecalActors;
        ShowStageNodes = viewState.ShowStageNodes;
        ShowStageCameras = viewState.ShowStageCameras;
        ShowCollision = viewState.ShowCollision;

        VisibleSetDistance = viewState.VisibleSetDistance;
        _hasUserEditedVisibleSets = viewState.HasUserEditedVisibleSets;
        _explicitlyHiddenVisibleSetClasses.Clear();
        if (viewState.HiddenActorClasses is { Count: > 0 })
        {
            _explicitlyHiddenVisibleSetClasses.UnionWith(viewState.HiddenActorClasses);
        }
        _visibleActorSet.Clear();
        if (viewState.VisibleActorKeys.Count > 0)
        {
            _visibleActorSet.UnionWith(viewState.VisibleActorKeys);
        }

        ObjectRenderMode = viewState.ObjectRenderMode;
        UseVisibleSetOnly = viewState.UseVisibleSetOnly;

        IsOrthographicView = viewState.IsOrthographicView;
        RenderContext.Camera.Position = new Vector3(viewState.CameraX, viewState.CameraY, viewState.CameraZ);
        RenderContext.Camera.Yaw = viewState.CameraYaw;
        RenderContext.Camera.Pitch = viewState.CameraPitch;
        if (viewState.IsOrthographicView)
        {
            RenderContext.Camera.OrthoWidth = viewState.CameraOrthoWidth;
        }
    }

    private void RefreshRecentsMenu()
    {
        Recents_MenuItem.Items.Clear();
        Recents_MenuItem.IsEnabled = RecentSets.Count > 0;
        foreach (var set in RecentSets)
        {
            var mi = new MenuItem
            {
                Header = set.DisplayName.Replace("_", "__"),
                ToolTip = set.TooltipText,
                Tag = set
            };
            mi.Click += (_, _) => OpenRecentFileSet((RecentFileSet)mi.Tag);
            Recents_MenuItem.Items.Add(mi);
        }
    }

    #endregion

    #region UI Properties

    private float _posIncrement = 10f;
    public float PosIncrement
    {
        get => _posIncrement;
        set => SetProperty(ref _posIncrement, value);
    }

    private float _rotIncrement = 5f;
    public float RotIncrement
    {
        get => _rotIncrement;
        set => SetProperty(ref _rotIncrement, value);
    }

    private float _scaleIncrement = 0.1f;
    public float ScaleIncrement
    {
        get => _scaleIncrement;
        set => SetProperty(ref _scaleIncrement, value);
    }

    private string textBelowActors;
    public string TextBelowActors { get => textBelowActors; set => SetProperty(ref textBelowActors, value); }

    private string _currentModeName = "Translate";
    public string CurrentModeName { get => _currentModeName; set => SetProperty(ref _currentModeName, value); }

    private bool _isOrthographicView;
    public bool IsOrthographicView
    {
        get => _isOrthographicView;
        set
        {
            if (SetProperty(ref _isOrthographicView, value))
            {
                if (value)
                {
                    RenderContext.Camera.SavePerspectiveState();
                    RenderContext.Camera.IsOrthographic = true;
                    var pos = RenderContext.Camera.Position;
                    RenderContext.Camera.Position = new Vector3(pos.X, pos.Y, RenderContext.Camera.ZFar * 0.4f);
                    float focusDepth = RenderContext.Camera.FocusDepth;
                    RenderContext.Camera.OrthoWidth = MathF.Max(focusDepth * 4f, 500f);
                }
                else
                {
                    RenderContext.Camera.IsOrthographic = false;
                    RenderContext.Camera.RestorePerspectiveState();
                }
            }
        }
    }

    private void ResetUncommittedChanges_Click(object sender, RoutedEventArgs e)
    {
        if (IsDirty && MessageBox.Show("Are you sure you want to reset uncommitted changes?", "Reset confirmation", MessageBoxButton.YesNo) is MessageBoxResult.Yes)
        {
            foreach (var file in OpenFiles)
            {
                ReloadFile(file);
            }
        }
    }

    #endregion



    #region Busy variables

    private bool _isBusy;

    public bool IsBusy
    {
        get => _isBusy;
        set => SetProperty(ref _isBusy, value);
    }

    private bool _isBusyTaskbar;

    public bool IsBusyTaskbar
    {
        get => _isBusyTaskbar;
        set => SetProperty(ref _isBusyTaskbar, value);
    }

    private string _busyText;

    public string BusyText
    {
        get => _busyText;
        set => SetProperty(ref _busyText, value);
    }

    public virtual void SetBusy(string text = null)
    {
        BusyText = text;
        IsBusy = true;
    }
    public virtual void EndBusy()
    {
        IsBusy = false;
    }

    public void HandleSaveStateChange(bool isSaving)
    {
        if (isSaving)
        {
            SetBusy("Saving");
        }
        else
        {
            EndBusy();
        }
    }

    #endregion

    private readonly struct RenderGuard : IDisposable
    {
        private readonly LevelEditor levelEditor;

        public RenderGuard(LevelEditor levEd)
        {
            levelEditor = levEd;
            levelEditor.SceneViewer.SetShouldRender(false);
            levelEditor.SetBusy();
        }

        public readonly void Dispose()
        {
            levelEditor.SceneViewer.SetShouldRender(true);
            levelEditor.EndBusy();
        }
    }
}
