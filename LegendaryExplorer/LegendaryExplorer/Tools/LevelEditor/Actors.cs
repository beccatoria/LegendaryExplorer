using LegendaryExplorer.Misc;
using LegendaryExplorer.Tools.LevelEditor.Scene3D;
using LegendaryExplorerCore.Helpers;
using LegendaryExplorerCore.Packages;
using LegendaryExplorerCore.Unreal;
using LegendaryExplorerCore.Unreal.BinaryConverters;
using LegendaryExplorerCore.Unreal.Classes;
using LegendaryExplorerCore.Unreal.ObjectInfo;
using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using MediaColor = System.Windows.Media.Color;
using MediaColors = System.Windows.Media.Colors;

namespace LegendaryExplorer.Tools.LevelEditor;

public class ActorProxy : NotifyPropertyChangedBase, IDisposable, IHitProxy
{
    public IActorEditorContext Editor;
    private OpenLevelFile _owningFile;
    public OpenLevelFile OwningFile
    {
        get => _owningFile;
        set
        {
            if (SetProperty(ref _owningFile, value))
            {
                OnPropertyChanged(nameof(OwningFileSortOrder));
            }
        }
    }

    public int OwningFileSortOrder => OwningFile?.LoadOrder ?? int.MaxValue;

    public Matrix4x4 LocalToWorld;

    public List<PrimitiveComponentProxy> Components = [];

    #region UProp mirrors. DO NOT CHANGE NAMES!
    public ActorProxy Base;
    public SkeletalMeshComponentProxy BaseSkelComponent;
    public NameReference BaseBoneName;
    public List<ActorProxy> Attached = [];
    public bool bHardAttach;
    public NameReference Tag;
    #endregion

    protected PropertyCollection Properties;

    public ExportEntry Export { get; }
    protected IMEPackage Pcc => Export.FileRef;

    public string OwningFileName => System.IO.Path.GetFileName(Pcc.FilePath);
    public int ActorUIndex => Export.UIndex;

    public string DisplayText { get; }

    private bool isDirty;
    public bool IsDirty
    {
        get => isDirty;
        protected set
        {
            if (SetProperty(ref isDirty, value))
            {
                if (OwningFile is not null)
                {
                    if (isDirty)
                        OwningFile.IsDirty = true;
                    else
                        OwningFile.RecalculateDirty();
                }
            }
        }
    }

    protected TransformSnapshot _cleanSnapshot;

    public void MarkClean()
    {
        _cleanSnapshot = SnapshotTransform();
        IsDirty = false;
    }

    private bool _isBeingAnimated;
    public bool IsBeingAnimated
    {
        get => _isBeingAnimated;
        private set => SetProperty(ref _isBeingAnimated, value);
    }

    private TransformSnapshot? _prematineeSnapshot;

    public void BeginMatineeControl()
    {
        if (!_isBeingAnimated)
            _prematineeSnapshot = SnapshotTransform();
        IsBeingAnimated = true;
    }

    public void EndMatineeControl()
    {
        IsBeingAnimated = false;
        if (_prematineeSnapshot is TransformSnapshot snap)
            RestoreTransform(snap);
        _prematineeSnapshot = null;
    }

    protected Rotator rotation;
    protected Vector3 location;
    protected Vector3 drawScale3D;
    protected float drawScale;
    protected Vector3 prePivot;
    public Rotator Rotation
    {
        get => rotation;
        set
        {
            if (IsReadOnly) return;
            var oldValue = rotation;
            if (rotation != value)
            {
                rotation = value;
                OnPropertyChanged(nameof(Rotation));
                if (value.Pitch != oldValue.Pitch) OnPropertyChanged(nameof(PitchDegrees));
                if (value.Yaw != oldValue.Yaw) OnPropertyChanged(nameof(YawDegrees));
                if (value.Roll != oldValue.Roll) OnPropertyChanged(nameof(RollDegrees));
                UpdateLocalToWorld();
                if (!IsBeingAnimated)
                    IsDirty = !SnapshotTransform().Equals(_cleanSnapshot);
            }
        }
    }
    public float PitchDegrees { get => rotation.Pitch.UnrealRotationUnitsToDegrees(); set => Rotation = new Rotator(value.DegreesToUnrealRotationUnits(), rotation.Yaw, rotation.Roll); }
    public float YawDegrees { get => rotation.Yaw.UnrealRotationUnitsToDegrees(); set => Rotation = new Rotator(rotation.Pitch, value.DegreesToUnrealRotationUnits(), rotation.Roll); }
    public float RollDegrees { get => rotation.Roll.UnrealRotationUnitsToDegrees(); set => Rotation = new Rotator(rotation.Pitch, rotation.Yaw, value.DegreesToUnrealRotationUnits()); }
    public Vector3 Location
    {
        get => location;
        set
        {
            if (IsReadOnly) return;
            var oldValue = location;
            if (location != value)
            {
                location = value;
                OnPropertyChanged(nameof(Location));
                if (value.X != oldValue.X) OnPropertyChanged(nameof(XPos));
                if (value.Y != oldValue.Y) OnPropertyChanged(nameof(YPos));
                if (value.Z != oldValue.Z) OnPropertyChanged(nameof(ZPos));
                UpdateLocalToWorld();
                if (!IsBeingAnimated)
                    IsDirty = !SnapshotTransform().Equals(_cleanSnapshot);
            }
        }
    }
    public float XPos { get => location.X; set => Location = location with { X = value }; }
    public float YPos { get => location.Y; set => Location = location with { Y = value }; }
    public float ZPos { get => location.Z; set => Location = location with { Z = value }; }

    public Vector3 DrawScale3D
    {
        get => drawScale3D;
        set
        {
            if (IsReadOnly) return;
            var oldValue = drawScale3D;
            if (drawScale3D != value)
            {
                drawScale3D = value;
                OnPropertyChanged(nameof(DrawScale3D));
                if (value.X != oldValue.X) OnPropertyChanged(nameof(XScale));
                if (value.Y != oldValue.Y) OnPropertyChanged(nameof(YScale));
                if (value.Z != oldValue.Z) OnPropertyChanged(nameof(ZScale));
                UpdateLocalToWorld();
                if (!IsBeingAnimated)
                    IsDirty = !SnapshotTransform().Equals(_cleanSnapshot);
            }
        }
    }
    public float XScale { get => drawScale3D.X; set => DrawScale3D = drawScale3D with { X = value }; }
    public float YScale { get => drawScale3D.Y; set => DrawScale3D = drawScale3D with { Y = value }; }
    public float ZScale { get => drawScale3D.Z; set => DrawScale3D = drawScale3D with { Z = value }; }
    public float DrawScale
    {
        get => drawScale;
        set
        {
            if (IsReadOnly) return;
            if (SetProperty(ref drawScale, value))
            {
                UpdateLocalToWorld();
                if (!IsBeingAnimated)
                    IsDirty = !SnapshotTransform().Equals(_cleanSnapshot);
            }
        }
    }
    public Vector3 PrePivot
    {
        get => prePivot;
        set
        {
            if (IsReadOnly) return;
            if (SetProperty(ref prePivot, value))
            {
                UpdateLocalToWorld();
                if (!IsBeingAnimated)
                    IsDirty = !SnapshotTransform().Equals(_cleanSnapshot);
            }
        }
    }

    public bool IsReadOnly => (OwningFile is null || OwningFile.IsReadOnly)
                           && !(Editor?.IsApplyingUndoRedo ?? false);

    public bool IsLight { get; protected set; }
    protected LightComponentProxy LightEditorComponent { get; set; }
    public bool SupportsLightProperties => LightEditorComponent is not null;
    public virtual bool IsVolume => false;
    public bool IsVolumetricMesh { get; protected set; }
    public bool IsEmitter { get; protected set; }
    public bool IsStartPoint { get; protected set; }
    public bool IsTargetPoint { get; protected set; }
    public bool IsPointOfInterest { get; protected set; }
    public bool IsLocationActor { get; protected set; }
    public bool IsAmbientSound { get; protected set; }
    public bool IsCameraActor { get; protected set; }
    public bool IsCinematicActor { get; protected set; }
    public bool IsDecalActor { get; protected set; }

    public float LightBrightness
    {
        get => LightEditorComponent?.Brightness ?? 0f;
        set
        {
            if (LightEditorComponent is null || IsReadOnly || LightEditorComponent.Brightness == value) return;
            LightEditorComponent.Brightness = value;
            OnPropertyChanged(nameof(LightBrightness));
        }
    }

    public float LightRadius
    {
        get => LightEditorComponent?.Radius ?? 0f;
        set
        {
            if (LightEditorComponent is null || IsReadOnly || LightEditorComponent.Radius == value) return;
            LightEditorComponent.Radius = value;
            OnPropertyChanged(nameof(LightRadius));
        }
    }

    public float LightSourceRadius
    {
        get => LightEditorComponent?.SourceRadius ?? 0f;
        set
        {
            if (LightEditorComponent is null || IsReadOnly || LightEditorComponent.SourceRadius == value) return;
            LightEditorComponent.SourceRadius = value;
            OnPropertyChanged(nameof(LightSourceRadius));
        }
    }

    public MediaColor? LightColor
    {
        get => LightEditorComponent?.LightColor ?? MediaColors.White;
        set
        {
            if (LightEditorComponent is null || IsReadOnly || value is null || LightEditorComponent.LightColor == value.Value) return;
            LightEditorComponent.LightColor = value.Value;
            OnPropertyChanged(nameof(LightColor));
        }
    }

    public bool LightChannelStatic
    {
        get => LightEditorComponent?.LightingChannelStatic ?? false;
        set
        {
            if (LightEditorComponent is null || IsReadOnly || LightEditorComponent.LightingChannelStatic == value) return;
            LightEditorComponent.LightingChannelStatic = value;
            OnPropertyChanged(nameof(LightChannelStatic));
        }
    }

    public bool LightChannelDynamic
    {
        get => LightEditorComponent?.LightingChannelDynamic ?? false;
        set
        {
            if (LightEditorComponent is null || IsReadOnly || LightEditorComponent.LightingChannelDynamic == value) return;
            LightEditorComponent.LightingChannelDynamic = value;
            OnPropertyChanged(nameof(LightChannelDynamic));
        }
    }

    public bool LightChannelCompositeDynamic
    {
        get => LightEditorComponent?.LightingChannelCompositeDynamic ?? false;
        set
        {
            if (LightEditorComponent is null || IsReadOnly || LightEditorComponent.LightingChannelCompositeDynamic == value) return;
            LightEditorComponent.LightingChannelCompositeDynamic = value;
            OnPropertyChanged(nameof(LightChannelCompositeDynamic));
        }
    }

    public TransformSnapshot SnapshotTransform() => new(location, rotation, drawScale, drawScale3D);

    public void RestoreTransform(TransformSnapshot snapshot)
    {
        Location = snapshot.Location;
        Rotation = snapshot.Rotation;
        DrawScale = snapshot.DrawScale;
        DrawScale3D = snapshot.DrawScale3D;
    }

    internal void MarkDirty()
    {
        if (!IsBeingAnimated)
        {
            IsDirty = true;
        }
    }

    protected ActorProxy(IActorEditorContext context, ExportEntry actorExport)
    {
        Editor = context;
        Export = actorExport;
        Properties = actorExport.GetCondensedProperties();
        PropertyCollection props = Properties;

        props.ReadProp(ref Tag);

        DisplayText = Export.ObjectName.Instanced;
        if (!Tag.Name.CaseInsensitiveEquals(Export.ClassName))
        {
            DisplayText += $" ({Tag})";
        }

        var rotationProp = props.GetProp<StructProperty>("Rotation");
        var locationsProp = props.GetProp<StructProperty>("location");
        var drawScale3DProp = props.GetProp<StructProperty>("DrawScale3D");
        var prePivotProp = props.GetProp<StructProperty>("PrePivot");

        drawScale = props.GetProp<FloatProperty>("DrawScale")?.Value ?? 1;
        location = locationsProp != null ? CommonStructs.GetVector3(locationsProp) : Vector3.Zero;
        drawScale3D = drawScale3DProp != null ? CommonStructs.GetVector3(drawScale3DProp) : Vector3.One;
        prePivot = prePivotProp != null ? CommonStructs.GetVector3(prePivotProp) : Vector3.Zero;
        rotation = rotationProp != null ? CommonStructs.GetRotator(rotationProp) : new Rotator(0, 0, 0);
        UpdateLocalToWorld();
        _cleanSnapshot = SnapshotTransform();
    }

    //only for use by the faux actors that are children of the CollectionActors
    protected ActorProxy(ExportEntry actorExport)
    {
        Export = actorExport;
        DisplayText = Export.ObjectName.Instanced;
        drawScale = 1;
        location =  Vector3.Zero;
        drawScale3D =  Vector3.One;
        prePivot =  Vector3.Zero;
        rotation = new Rotator(0, 0, 0);
        Properties = [];
    }

    public void ResolveAttachment(IEnumerable<ActorProxy> actorProxies)
    {
        if (Properties.TryResolveObjectProp(Pcc, "Base", out ExportEntry baseExport)
            && actorProxies.FirstOrDefault(ap => ap.Export == baseExport) is ActorProxy baseActor)
        {
            Base = baseActor;
            baseActor.Attached.Add(this);
            if (Properties.TryResolveObjectProp(Pcc, nameof(BaseSkelComponent), out ExportEntry baseSkelComponentExport))
            {
                BaseSkelComponent = baseActor.Components.OfType<SkeletalMeshComponentProxy>().FirstOrDefault(cmp => cmp.Export == baseSkelComponentExport);
            }
            Properties.ReadProp(ref BaseBoneName);
            Properties.ReadProp(ref bHardAttach);
        }
    }

    public void Detach()
    {
        Base?.Attached.Remove(this);
        Base = null;
        foreach (var attached in Attached)
        {
            attached.Base = null;
        }
        Attached.Clear();
    }

    protected virtual void UpdateLocalToWorld()
    {
        LocalToWorld = ActorUtils.ComposeLocalToWorld(location, rotation, drawScale * drawScale3D, prePivot);
        foreach (var cmp in Components)
        {
            cmp.UpdateLocalToWorld();
        }
    }

    private static readonly FrozenSet<string> SupportedClasses = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "StaticMeshActor",
        "SkeletalMeshActor",
        "SFXSkeletalMeshActor",
        "DynamicSMActor",
        "Brush",
        "SFXStuntActor",
        "BioArtPlaceable",
        "BioPawn",
        "Pawn",
        "PrefabInstance",
        "SFXDroppedGrenade",
        "SFXDroppedAmmo",
        "SFXDroppedPickup",
        "Emitter",
        "BioEmitter",
        "PlayerStart",
        "BioStartLocation",
        "Location",
        "TargetPoint",
        "SFXPointOfInterest",
        "WwiseAmbientSound",
        "AmbientSound",
        "WwiseMicPosOrient",
        "CameraActor",
        "SceneCapture2DActor",
        "SceneCaptureReflectActor",
        "ScreenCaptureReflectActor",
        "DecalActor",
        "MaterialInstanceActor",
        "LensFlareSource",
        "BioStage"
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    public static bool CanCreate(ExportEntry actorExport)
    {
        return actorExport.IsA(SupportedClasses)
               || actorExport.IsA("Light")
               || actorExport.IsA("SceneCaptureReflectActor")
               || actorExport.IsA("ScreenCaptureReflectActor")
               || actorExport.ClassName.Contains("CaptureReflectActor", StringComparison.OrdinalIgnoreCase);
    }

    //KEEP IN SYNC WITH CanCreate!
    public static ActorProxy Create(IActorEditorContext context, ExportEntry actorExport)
    {
        string className = actorExport.ClassName;
        if (GlobalUnrealObjectInfo.IsA(className, "StaticMeshActor", actorExport.Game))
        {
            return new StaticMeshActorProxy(context, actorExport);
        }
        if (GlobalUnrealObjectInfo.IsA(className, "SFXSkeletalMeshActor", actorExport.Game))
        {
            return new SFXSkeletalMeshActorProxy(context, actorExport);
        }
        if (GlobalUnrealObjectInfo.IsA(className, "SkeletalMeshActor", actorExport.Game))
        {
            return new SkeletalMeshActorProxy(context, actorExport);
        }
        if (GlobalUnrealObjectInfo.IsA(className, "DynamicSMActor", actorExport.Game))
        {
            return new DynamicSMActorProxy(context, actorExport);
        }
        if (GlobalUnrealObjectInfo.IsA(className, "Brush", actorExport.Game))
        {
            return new BrushProxy(context, actorExport);
        }
        if (GlobalUnrealObjectInfo.IsA(className, "SFXStuntActor", actorExport.Game))
        {
            return new SFXStuntActorProxy(context, actorExport);
        }
        if (GlobalUnrealObjectInfo.IsA(className, "BioArtPlaceable", actorExport.Game))
        {
            return new BioArtPlaceableProxy(context, actorExport);
        }
        if (GlobalUnrealObjectInfo.IsA(className, "BioPawn", actorExport.Game))
        {
            return new BioPawnProxy(context, actorExport);
        }
        if (GlobalUnrealObjectInfo.IsA(className, "Pawn", actorExport.Game))
        {
            return new PawnProxy(context, actorExport);
        }
        if (GlobalUnrealObjectInfo.IsA(className, "PrefabInstance", actorExport.Game))
        {
            return new PrefabInstanceProxy(context, actorExport);
        }
        if (GlobalUnrealObjectInfo.IsA(className, "SFXDroppedGrenade", actorExport.Game))
        {
            return new SFXDroppedGrenadeProxy(context, actorExport);
        }
        if (GlobalUnrealObjectInfo.IsA(className, "SFXDroppedAmmo", actorExport.Game))
        {
            return new SFXDroppedAmmoProxy(context, actorExport);
        }
        if (GlobalUnrealObjectInfo.IsA(className, "SFXDroppedPickup", actorExport.Game))
        {
            return new SFXDroppedPickupProxy(context, actorExport);
        }
        if (GlobalUnrealObjectInfo.IsA(className, "Light", actorExport.Game))
        {
            return new LightActorProxy(context, actorExport);
        }
        if (GlobalUnrealObjectInfo.IsA(className, "Emitter", actorExport.Game)
            || GlobalUnrealObjectInfo.IsA(className, "BioEmitter", actorExport.Game))
        {
            return new IconActorProxy(context, actorExport, IconActorCategory.Emitter);
        }
        if (GlobalUnrealObjectInfo.IsA(className, "PlayerStart", actorExport.Game))
        {
            return new IconActorProxy(context, actorExport, IconActorCategory.StartPoint);
        }
        if (GlobalUnrealObjectInfo.IsA(className, "Location", actorExport.Game)
            || GlobalUnrealObjectInfo.IsA(className, "BioStartLocation", actorExport.Game))
        {
            return new IconActorProxy(context, actorExport, IconActorCategory.StartPoint);
        }
        if (GlobalUnrealObjectInfo.IsA(className, "TargetPoint", actorExport.Game))
        {
            return new IconActorProxy(context, actorExport, IconActorCategory.TargetPoint);
        }
        if (GlobalUnrealObjectInfo.IsA(className, "SFXPointOfInterest", actorExport.Game))
        {
            return new IconActorProxy(context, actorExport, IconActorCategory.PointOfInterest);
        }
        if (GlobalUnrealObjectInfo.IsA(className, "WwiseAmbientSound", actorExport.Game))
        {
            return new IconActorProxy(context, actorExport, IconActorCategory.WwiseAmbientSound);
        }
        if (GlobalUnrealObjectInfo.IsA(className, "AmbientSound", actorExport.Game))
        {
            return new IconActorProxy(context, actorExport, IconActorCategory.AmbientSound);
        }
        if (GlobalUnrealObjectInfo.IsA(className, "WwiseMicPosOrient", actorExport.Game))
        {
            return new IconActorProxy(context, actorExport, IconActorCategory.WwiseMic);
        }
        if (GlobalUnrealObjectInfo.IsA(className, "CameraActor", actorExport.Game))
        {
            return new IconActorProxy(context, actorExport, IconActorCategory.Camera);
        }
        if (actorExport.IsA("SceneCapture2DActor")
            || className.Contains("SceneCapture2DActor", StringComparison.OrdinalIgnoreCase))
        {
            return new IconActorProxy(context, actorExport, IconActorCategory.SceneCapture2D);
        }
        if (actorExport.IsA("SceneCaptureReflectActor")
            || actorExport.IsA("ScreenCaptureReflectActor")
            || className.Contains("CaptureReflectActor", StringComparison.OrdinalIgnoreCase))
        {
            return new IconActorProxy(context, actorExport, IconActorCategory.ScreenCaptureReflect);
        }
        if (GlobalUnrealObjectInfo.IsA(className, "BioStage", actorExport.Game))
        {
            return new BioStageActorProxy(context, actorExport);
        }
        if (GlobalUnrealObjectInfo.IsA(className, "DecalActor", actorExport.Game))
        {
            return new IconActorProxy(context, actorExport, IconActorCategory.Decal);
        }
        if (GlobalUnrealObjectInfo.IsA(className, "MaterialInstanceActor", actorExport.Game))
        {
            return new IconActorProxy(context, actorExport, IconActorCategory.MaterialInstance);
        }
        if (GlobalUnrealObjectInfo.IsA(className, "LensFlareSource", actorExport.Game))
        {
            return new IconActorProxy(context, actorExport, IconActorCategory.LensFlareLight);
        }
        return null;
        //return new ActorProxy(context, actorExport);
    }

    protected void AddComponentArray<T>(MeshRenderContext context, ref List<T> components, [CallerArgumentExpression(nameof(components))] string propName = null) where T : PrimitiveComponentProxy
    {
        if (Properties.GetProp<ArrayProperty<ObjectProperty>>(propName) is { } componentArray)
        {
            foreach (IEntry entry in componentArray.ResolveToEntries(Pcc))
            {
                if (entry is ExportEntry cmpExport && PrimitiveComponentProxy.Create(context, cmpExport, this) is T cmpProxy)
                {
                    components.Add(cmpProxy);
                    Components.Add(cmpProxy);
                }
            }
        }
    }

    protected void AddComponent<T>(MeshRenderContext context, ref T component, [CallerArgumentExpression(nameof(component))] string propName = null) where T : PrimitiveComponentProxy
    {
        if (Properties.GetProp<ObjectProperty>(propName)?.ResolveToEntry(Pcc) is ExportEntry componentExport)
        {
            if (PrimitiveComponentProxy.Create(context, componentExport, this) is T cmpProxy)
            {
                component = cmpProxy;
                Components.Add(cmpProxy);
            }
        }
    }

    public virtual void UpdateScene(LevelEditorRenderContext context, float deltaTime)
    {
        foreach (var component in Components)
        {
            component.UpdateScene(context, deltaTime);
        }
    }

    public virtual void Render(LevelEditorRenderContext context, RenderPass pass)
    {
        foreach (var component in Components)
        {
            component.Render(context, pass);
        }
    }

    public virtual BoxSphereBounds GetBounds()
    {
        if (Components.Count is 0)
        {
            return new BoxSphereBounds
            {
                Origin = LocalToWorld.Translation
            };
        }
        var bounds = Components[0].GetBounds();
        for (int i = 1; i < Components.Count; i++)
        {
            bounds = bounds.Union(Components[i].GetBounds());
        }
        return bounds;
    }

    public int HitID { get; set; }

    public virtual int HitPriority => IHitProxy.StandardPriority;

    public virtual void CommitChanges(PackageCache packageCache = null)
    {
        var props = Properties;

        string locationPropName = Export.Game.IsGame3() ? "location" : "Location";
        if (props.ContainsNamedProp(locationPropName) || Location != Vector3.Zero)
        {
            props.AddOrReplaceProp(CommonStructs.Vector3Prop(Location, locationPropName));
        }
        if (props.ContainsNamedProp("DrawScale") || DrawScale != 1f)
        {
            props.AddOrReplaceProp(new FloatProperty(DrawScale, "DrawScale"));
        }
        if (props.ContainsNamedProp("DrawScale3D") || DrawScale3D != Vector3.One)
        {
            props.AddOrReplaceProp(CommonStructs.Vector3Prop(DrawScale3D, "DrawScale3D"));
        }
        if (props.ContainsNamedProp("Rotation") || !Rotation.IsZero)
        {
            props.AddOrReplaceProp(CommonStructs.RotatorProp(Rotation, "Rotation"));
        }
        if (props.ContainsNamedProp("PrePivot") || PrePivot != Vector3.Zero)
        {
            props.AddOrReplaceProp(CommonStructs.Vector3Prop(PrePivot, "PrePivot"));
        }
        Export.WriteProperties(props);
        foreach (var component in Components)
        {
            component.CommitChanges();
        }
    }

    public virtual bool TestUIndexes(HashSet<int> uIndexes)
    {
        if (uIndexes.Contains(Export.UIndex))
        {
            return true;
        }
        foreach (var cmp in Components)
        {
            if (cmp.TestUIndexes(uIndexes))
            {
                return true;
            }
        }
        return false;
    }

    public virtual void SetAnimation(AnimSequence animSequence, float pos)
    {
        if (App.IsDebug && Debugger.IsAttached)
        {
            //If reached, need to add animation support for whatever kind of actor this is
            Debugger.Break();
        }
    }

    protected void ApplyMorphFace(SkeletalMeshComponentProxy skeletalMeshComponent)
    {
        if (Properties.GetProp<ObjectProperty>("MorphHead")?.ResolveToExport(Pcc, Editor?.PackageCache) is ExportEntry morphHead)
        {
            (BonePosition[] bonePositions, Vector3[][] vertexOffsets) = LegendaryExplorerCore.Unreal.Classes.BioMorphFace.GetBoneAndVertexPositions(morphHead);
            skeletalMeshComponent.ApplyMorph(bonePositions, vertexOffsets);
        }
    }

    #region IDisposable
    protected bool isDisposed;

    protected virtual void Dispose(bool disposing)
    {
        if (!isDisposed)
        {
            if (disposing)
            {
                foreach (var cmp in Components)
                {
                    cmp.Dispose();
                }
                Components.Clear();
            }
            isDisposed = true;
        }
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
    #endregion
}

public class StaticMeshActorProxy : ActorProxy
{
    public StaticMeshComponentProxy StaticMeshComponent;
    public StaticMeshActorProxy(IActorEditorContext context, ExportEntry actorExport) : base(context, actorExport)
    {
        AddComponent(context.RenderContext, ref StaticMeshComponent);
        IsVolumetricMesh = StaticMeshComponent.IsVolumetric;
    }
}

public class SkeletalMeshActorProxy : ActorProxy
{
    public SkeletalMeshComponentProxy SkeletalMeshComponent;
    public SkeletalMeshActorProxy(IActorEditorContext context, ExportEntry actorExport) : base(context, actorExport)
    {
        AddComponent(context.RenderContext, ref SkeletalMeshComponent);
    }

    public override void SetAnimation(AnimSequence animSequence, float pos)
    {
        SkeletalMeshComponent?.SetAnimation(animSequence, pos);
    }
}

public class SFXSkeletalMeshActorProxy : SkeletalMeshActorProxy
{
    public SkeletalMeshComponentProxy HeadMesh;
    public SkeletalMeshComponentProxy HairMesh;
    public SkeletalMeshComponentProxy HeadGearMesh;
    public SFXSkeletalMeshActorProxy(IActorEditorContext context, ExportEntry actorExport) : base(context, actorExport)
    {
        AddComponent(context.RenderContext, ref HeadMesh);
        ApplyMorphFace(HeadMesh);
        AddComponent(context.RenderContext, ref HairMesh);
        AddComponent(context.RenderContext, ref HeadGearMesh);
    }

    public override void SetAnimation(AnimSequence animSequence, float pos)
    {
        base.SetAnimation(animSequence, pos);
        HeadMesh?.SetAnimation(animSequence, pos);
        HairMesh?.SetAnimation(animSequence, pos);
        HeadGearMesh?.SetAnimation(animSequence, pos);
    }
}

//interpactor, placeables
public class DynamicSMActorProxy : ActorProxy
{
    public StaticMeshComponentProxy StaticMeshComponent;

    public DynamicSMActorProxy(IActorEditorContext context, ExportEntry actorExport) : base(context, actorExport)
    {
        AddComponent(context.RenderContext, ref StaticMeshComponent);
        IsVolumetricMesh = StaticMeshComponent.IsVolumetric;
    }
}

//volumes
public class BrushProxy : ActorProxy
{
    public BrushComponentProxy BrushComponent;
    public override bool IsVolume => true;

    public BrushProxy(IActorEditorContext context, ExportEntry actorExport) : base(context, actorExport)
    {
        AddComponent(context.RenderContext, ref BrushComponent);
    }
    public override int HitPriority => IHitProxy.WireFramePriority;
}

public sealed class BioStageOverlayMarker : NotifyPropertyChangedBase, IHitProxy
{
    internal BioStageActorProxy Owner { get; }
    public int BoneIndex { get; }
    public string BoneName { get; }
    public bool IsCamera { get; }
    public int SequenceNumber { get; }
    public int OverlayNumber { get; }
    public string MarkerCategoryText => IsCamera ? "Camera" : "Node";
    public string DisplayLabel => $"{MarkerCategoryText} {SequenceNumber}: {BoneName}";
    internal Matrix4x4 ComponentSpaceTransform;

    public int HitID { get; set; }
    public int HitPriority => IHitProxy.UIPriority;

    public float XPos
    {
        get => Owner.GetMarkerLocalPosition(this).X;
        set => Owner.SetMarkerLocalPosition(this, Owner.GetMarkerLocalPosition(this) with { X = value });
    }

    public float YPos
    {
        get => Owner.GetMarkerLocalPosition(this).Y;
        set => Owner.SetMarkerLocalPosition(this, Owner.GetMarkerLocalPosition(this) with { Y = value });
    }

    public float ZPos
    {
        get => Owner.GetMarkerLocalPosition(this).Z;
        set => Owner.SetMarkerLocalPosition(this, Owner.GetMarkerLocalPosition(this) with { Z = value });
    }

    public float PitchDegrees
    {
        get => Owner.GetMarkerRotator(this).Pitch.UnrealRotationUnitsToDegrees();
        set
        {
            Rotator current = Owner.GetMarkerRotator(this);
            Owner.SetMarkerRotator(this, new Rotator(value.DegreesToUnrealRotationUnits(), current.Yaw, current.Roll));
        }
    }

    public float YawDegrees
    {
        get => Owner.GetMarkerRotator(this).Yaw.UnrealRotationUnitsToDegrees();
        set
        {
            Rotator current = Owner.GetMarkerRotator(this);
            Owner.SetMarkerRotator(this, new Rotator(current.Pitch, value.DegreesToUnrealRotationUnits(), current.Roll));
        }
    }

    public float RollDegrees
    {
        get => Owner.GetMarkerRotator(this).Roll.UnrealRotationUnitsToDegrees();
        set
        {
            Rotator current = Owner.GetMarkerRotator(this);
            Owner.SetMarkerRotator(this, new Rotator(current.Pitch, current.Yaw, value.DegreesToUnrealRotationUnits()));
        }
    }

    internal BioStageOverlayMarker(BioStageActorProxy owner, int boneIndex, string boneName, bool isCamera, int sequenceNumber, int overlayNumber)
    {
        Owner = owner;
        BoneIndex = boneIndex;
        BoneName = boneName;
        IsCamera = isCamera;
        SequenceNumber = sequenceNumber;
        OverlayNumber = overlayNumber;
    }

    internal void NotifyTransformChanged()
    {
        OnPropertyChanged(nameof(XPos));
        OnPropertyChanged(nameof(YPos));
        OnPropertyChanged(nameof(ZPos));
        OnPropertyChanged(nameof(PitchDegrees));
        OnPropertyChanged(nameof(YawDegrees));
        OnPropertyChanged(nameof(RollDegrees));
    }
}

public class BioStageActorProxy : ActorProxy
{
    private static readonly int[] SevenSegmentDigitMasks =
    [
        0b1110111, // 0
        0b0100100, // 1
        0b1011101, // 2
        0b1101101, // 3
        0b0101110, // 4
        0b1101011, // 5
        0b1111011, // 6
        0b0100101, // 7
        0b1111111, // 8
        0b1101111  // 9
    ];

    public SkeletalMeshComponentProxy MeshComponent;
    public StaticMeshComponentProxy StaticMeshComponent;
    public BrushComponentProxy BrushComponent;
    private List<PrimitiveComponentProxy> StageComponents = [];
    private readonly List<BioStageOverlayMarker> _stageMarkers = [];
    private bool _stageMarkersInitialized;
    private bool _stageMarkersDirty;
    private Matrix4x4[] _componentSpaceBoneTransforms = [];

    public IReadOnlyList<BioStageOverlayMarker> StageMarkers => _stageMarkers;

    public BioStageActorProxy(IActorEditorContext context, ExportEntry actorExport) : base(context, actorExport)
    {
        IsCinematicActor = true;

        // BioStage implementations vary; try generic actor components first.
        AddComponentArray(context.RenderContext, ref StageComponents, "Components");

        // Many BioStage exports use a direct Mesh -> SkeletalMeshComponent reference.
        AddComponent(context.RenderContext, ref MeshComponent, "Mesh");
        if (MeshComponent is not null)
        {
            MeshComponent.ForceWireframeRender = true;
        }

        // Fallbacks for stage variants that expose explicit component refs.
        if (StageComponents.Count is 0)
        {
            AddComponent(context.RenderContext, ref StaticMeshComponent);
            AddComponent(context.RenderContext, ref BrushComponent);
        }

        MeshComponent ??= Components.OfType<SkeletalMeshComponentProxy>().FirstOrDefault(component => component.RefSkeleton is { Length: > 0 });
        if (MeshComponent is not null)
        {
            MeshComponent.ForceWireframeRender = true;
        }

        BuildStageMarkers();
    }

    public override void CommitChanges(PackageCache packageCache = null)
    {
        base.CommitChanges(packageCache);

        if (!_stageMarkersDirty)
        {
            return;
        }

        if (MeshComponent?.SkeletalMeshExport is null || MeshComponent.SkeletalMeshBinary is null)
        {
            return;
        }

        MeshComponent.SkeletalMeshExport.WriteBinary(MeshComponent.SkeletalMeshBinary);
        _stageMarkersDirty = false;
    }

    public override int HitPriority => IHitProxy.WireFramePriority;

    public override void Render(LevelEditorRenderContext context, RenderPass pass)
    {
        base.Render(context, pass);

        if (pass is not (RenderPass.Base or RenderPass.Hair))
        {
            return;
        }

        if (ReferenceEquals(context.SelectedActor, this))
        {
            RenderStageMarkers(context);
        }

        bool hasRenderableStageGeometry = Components.Any(c => c is BrushComponentProxy or StaticMeshComponentProxy or SkeletalMeshComponentProxy);
        if (hasRenderableStageGeometry)
        {
            return;
        }

        float size = context.Camera.IsOrthographic ? 80f : Math.Clamp(Vector3.Distance(Location, context.Camera.Position) * 0.02f, 50f, 180f);
        float half = size * 0.5f;
        Vector4 color = new(1f, 0.9f, 0.25f, 1f);

        Vector3 p000 = new(-half, -half, -half);
        Vector3 p001 = new(-half, -half, half);
        Vector3 p010 = new(-half, half, -half);
        Vector3 p011 = new(-half, half, half);
        Vector3 p100 = new(half, -half, -half);
        Vector3 p101 = new(half, -half, half);
        Vector3 p110 = new(half, half, -half);
        Vector3 p111 = new(half, half, half);

        Matrix4x4 transform = Matrix4x4.CreateTranslation(Location);
        p000 = Vector3.Transform(p000, transform);
        p001 = Vector3.Transform(p001, transform);
        p010 = Vector3.Transform(p010, transform);
        p011 = Vector3.Transform(p011, transform);
        p100 = Vector3.Transform(p100, transform);
        p101 = Vector3.Transform(p101, transform);
        p110 = Vector3.Transform(p110, transform);
        p111 = Vector3.Transform(p111, transform);

        context.Primitives.AddLine(p000, p001, color, HitID);
        context.Primitives.AddLine(p000, p010, color, HitID);
        context.Primitives.AddLine(p000, p100, color, HitID);
        context.Primitives.AddLine(p001, p011, color, HitID);
        context.Primitives.AddLine(p001, p101, color, HitID);
        context.Primitives.AddLine(p010, p011, color, HitID);
        context.Primitives.AddLine(p010, p110, color, HitID);
        context.Primitives.AddLine(p100, p101, color, HitID);
        context.Primitives.AddLine(p100, p110, color, HitID);
        context.Primitives.AddLine(p011, p111, color, HitID);
        context.Primitives.AddLine(p101, p111, color, HitID);
        context.Primitives.AddLine(p110, p111, color, HitID);
    }

    private void BuildStageMarkers()
    {
        if (_stageMarkersInitialized)
        {
            return;
        }

        _stageMarkersInitialized = true;

        if (MeshComponent?.RefSkeleton is not { Length: > 0 })
        {
            return;
        }

        MeshBone[] refSkeleton = MeshComponent.RefSkeleton;
        _componentSpaceBoneTransforms = new Matrix4x4[refSkeleton.Length];
        int nodeSequence = 0;
        int cameraSequence = 0;
        for (int i = 0; i < refSkeleton.Length; i++)
        {
            MeshBone bone = refSkeleton[i];
            string boneName = bone.Name.Instanced;
            bool isCamera = IsStageCameraBoneName(boneName);
            if (!isCamera && !IsStageNodeBoneName(boneName))
            {
                continue;
            }

            int sequenceNumber = isCamera ? ++cameraSequence : ++nodeSequence;
            int overlayNumber = isCamera ? sequenceNumber : ResolveNodeOverlayNumber(boneName, sequenceNumber);
            var marker = new BioStageOverlayMarker(this, i, boneName, isCamera, sequenceNumber, overlayNumber);
            if (Editor?.RenderContext is not null)
            {
                marker.HitID = Editor.RenderContext.RegisterHitProxy(marker);
            }
            _stageMarkers.Add(marker);
        }

        UpdateComponentSpaceTransforms();
    }

    private void RenderStageMarkers(LevelEditorRenderContext context)
    {
        if (!context.ShowStageNodes && !context.ShowStageCameras)
        {
            return;
        }

        BuildStageMarkers();
        if (_stageMarkers.Count is 0)
        {
            return;
        }

        UpdateComponentSpaceTransforms();

        Matrix4x4 meshToWorld = MeshComponent?.LocalToWorld ?? LocalToWorld;
        foreach (BioStageOverlayMarker marker in _stageMarkers)
        {
            if (marker.IsCamera && !context.ShowStageCameras)
            {
                continue;
            }

            if (!marker.IsCamera && !context.ShowStageNodes)
            {
                continue;
            }

            Matrix4x4 markerToWorld = marker.ComponentSpaceTransform * meshToWorld;
            Vector3 markerPosition = markerToWorld.Translation;
            float markerSize = context.Camera.IsOrthographic
                ? 18f
                : Math.Clamp(Vector3.Distance(markerPosition, context.Camera.Position) * 0.0065f, 8f, 52f);
            bool isSelectedMarker = ReferenceEquals(context.SelectedBioStageMarker, marker);
            float markerRenderSize = isSelectedMarker ? markerSize * 1.2f : markerSize;

            if (marker.IsCamera)
            {
                RenderCameraMarker(context, marker, markerToWorld, markerPosition, markerRenderSize);
            }
            else
            {
                RenderNodeMarker(context, marker, markerToWorld, markerPosition, markerRenderSize);
            }

            if (isSelectedMarker)
            {
                RenderSelectedMarkerHighlight(context, marker, markerPosition, markerRenderSize);
            }

            RenderMarkerNumber(context, marker, markerToWorld, markerPosition, markerRenderSize, isSelectedMarker);
        }
    }

    private void RenderMarkerNumber(LevelEditorRenderContext context, BioStageOverlayMarker marker, Matrix4x4 markerToWorld, Vector3 markerPosition, float markerSize, bool isSelectedMarker)
    {
        Vector3 upAxis = GetAxis(markerToWorld, Vector3.UnitZ);
        Vector3 rightAxis = context.Camera.CameraRight;
        Vector3 textUp = context.Camera.CameraUp;
        float textScale = markerSize * 0.6f;
        Vector3 anchor = markerPosition + upAxis * (markerSize * 0.9f) + rightAxis * (textScale * 0.15f);

        Vector4 color = isSelectedMarker
            ? new Vector4(1.0f, 0.47f, 0.10f, 1f)
            : marker.IsCamera
                ? new Vector4(1.0f, 0.93f, 0.20f, 1f)
                : new Vector4(1.0f, 1.0f, 1.0f, 1f);

        Vector3 shadowOffset = rightAxis * (textScale * 0.06f) - textUp * (textScale * 0.06f);
        DrawNumber(context, marker.OverlayNumber, anchor + shadowOffset, rightAxis, textUp, textScale * 1.08f, new Vector4(0f, 0f, 0f, 1f), marker.HitID);

        DrawNumber(context, marker.OverlayNumber, anchor, rightAxis, textUp, textScale, color, marker.HitID);
    }

    private static void RenderSelectedMarkerHighlight(LevelEditorRenderContext context, BioStageOverlayMarker marker, Vector3 markerPosition, float markerSize)
    {
        Vector3 right = context.Camera.CameraRight;
        Vector3 up = context.Camera.CameraUp;
        float half = markerSize * 0.9f;
        Vector4 highlightColor = marker.IsCamera
            ? new Vector4(1.0f, 0.78f, 0.22f, 1f)
            : new Vector4(1.0f, 0.56f, 0.15f, 1f);

        Vector3 topLeft = markerPosition + up * half - right * half;
        Vector3 topRight = markerPosition + up * half + right * half;
        Vector3 bottomLeft = markerPosition - up * half - right * half;
        Vector3 bottomRight = markerPosition - up * half + right * half;

        context.Primitives.AddLine(topLeft, topRight, highlightColor, marker.HitID);
        context.Primitives.AddLine(topRight, bottomRight, highlightColor, marker.HitID);
        context.Primitives.AddLine(bottomRight, bottomLeft, highlightColor, marker.HitID);
        context.Primitives.AddLine(bottomLeft, topLeft, highlightColor, marker.HitID);
    }

    private static void DrawNumber(LevelEditorRenderContext context, int value, Vector3 anchor, Vector3 rightAxis, Vector3 upAxis, float scale, Vector4 color, int hitId)
    {
        string text = Math.Max(0, value).ToString();
        float digitAdvance = scale * 0.85f;
        float centeredOffset = (text.Length - 1) * digitAdvance * 0.5f;
        Vector3 start = anchor - rightAxis * centeredOffset;

        for (int i = 0; i < text.Length; i++)
        {
            if (!char.IsDigit(text[i]))
            {
                continue;
            }

            DrawSevenSegmentDigit(context, text[i] - '0', start + rightAxis * (i * digitAdvance), rightAxis, upAxis, scale, color, hitId);
        }
    }

    private static void DrawSevenSegmentDigit(LevelEditorRenderContext context, int digit, Vector3 origin, Vector3 rightAxis, Vector3 upAxis, float scale, Vector4 color, int hitId)
    {
        if ((uint)digit >= (uint)SevenSegmentDigitMasks.Length)
        {
            return;
        }

        int mask = SevenSegmentDigitMasks[digit];

        Vector2 topLeft = new(-0.30f, 0.50f);
        Vector2 topRight = new(0.30f, 0.50f);
        Vector2 midLeft = new(-0.30f, 0.00f);
        Vector2 midRight = new(0.30f, 0.00f);
        Vector2 bottomLeft = new(-0.30f, -0.50f);
        Vector2 bottomRight = new(0.30f, -0.50f);

        if ((mask & (1 << 0)) != 0) DrawGlyphLine(context, origin, rightAxis, upAxis, topLeft, topRight, scale, color, hitId);      // top
        if ((mask & (1 << 1)) != 0) DrawGlyphLine(context, origin, rightAxis, upAxis, topLeft, midLeft, scale, color, hitId);       // upper-left
        if ((mask & (1 << 2)) != 0) DrawGlyphLine(context, origin, rightAxis, upAxis, topRight, midRight, scale, color, hitId);     // upper-right
        if ((mask & (1 << 3)) != 0) DrawGlyphLine(context, origin, rightAxis, upAxis, midLeft, midRight, scale, color, hitId);      // middle
        if ((mask & (1 << 4)) != 0) DrawGlyphLine(context, origin, rightAxis, upAxis, midLeft, bottomLeft, scale, color, hitId);     // lower-left
        if ((mask & (1 << 5)) != 0) DrawGlyphLine(context, origin, rightAxis, upAxis, midRight, bottomRight, scale, color, hitId);   // lower-right
        if ((mask & (1 << 6)) != 0) DrawGlyphLine(context, origin, rightAxis, upAxis, bottomLeft, bottomRight, scale, color, hitId); // bottom
    }

    private static void DrawGlyphLine(LevelEditorRenderContext context, Vector3 origin, Vector3 rightAxis, Vector3 upAxis, Vector2 a, Vector2 b, float scale, Vector4 color, int hitId)
    {
        Vector3 p1 = origin + rightAxis * (a.X * scale) + upAxis * (a.Y * scale);
        Vector3 p2 = origin + rightAxis * (b.X * scale) + upAxis * (b.Y * scale);
        context.Primitives.AddLine(p1, p2, color, hitId);
    }

    private void UpdateComponentSpaceTransforms()
    {
        if (MeshComponent?.RefSkeleton is not { Length: > 0 } refSkeleton)
        {
            return;
        }

        if (_componentSpaceBoneTransforms.Length != refSkeleton.Length)
        {
            _componentSpaceBoneTransforms = new Matrix4x4[refSkeleton.Length];
        }

        for (int i = 0; i < refSkeleton.Length; i++)
        {
            MeshBone bone = refSkeleton[i];
            Matrix4x4 localTransform = CreateBoneLocalTransform(bone);
            int parentIndex = bone.ParentIndex;
            if ((uint)parentIndex < (uint)i)
            {
                _componentSpaceBoneTransforms[i] = localTransform * _componentSpaceBoneTransforms[parentIndex];
            }
            else
            {
                _componentSpaceBoneTransforms[i] = localTransform;
            }
        }

        foreach (BioStageOverlayMarker marker in _stageMarkers)
        {
            if ((uint)marker.BoneIndex < (uint)_componentSpaceBoneTransforms.Length)
            {
                marker.ComponentSpaceTransform = _componentSpaceBoneTransforms[marker.BoneIndex];
            }
        }
    }

    internal Vector3 GetMarkerLocalPosition(BioStageOverlayMarker marker)
    {
        if (MeshComponent?.RefSkeleton is not { Length: > 0 } refSkeleton || (uint)marker.BoneIndex >= (uint)refSkeleton.Length)
        {
            return Vector3.Zero;
        }

        return refSkeleton[marker.BoneIndex].Position;
    }

    internal Rotator GetMarkerRotator(BioStageOverlayMarker marker)
    {
        if (MeshComponent?.RefSkeleton is not { Length: > 0 } refSkeleton || (uint)marker.BoneIndex >= (uint)refSkeleton.Length)
        {
            return new Rotator(0, 0, 0);
        }

        Quaternion orientation = refSkeleton[marker.BoneIndex].Orientation;
        if (orientation.LengthSquared() <= 0.0001f)
        {
            orientation = Quaternion.Identity;
        }
        else
        {
            orientation = Quaternion.Normalize(orientation);
        }

        return Rotator.FromQuaternion(orientation);
    }

    internal void SetMarkerLocalPosition(BioStageOverlayMarker marker, Vector3 newPosition)
    {
        if (IsReadOnly || MeshComponent?.RefSkeleton is not { Length: > 0 } refSkeleton || (uint)marker.BoneIndex >= (uint)refSkeleton.Length)
        {
            return;
        }

        MeshBone bone = refSkeleton[marker.BoneIndex];
        if (bone.Position == newPosition)
        {
            return;
        }

        bone.Position = newPosition;
        _stageMarkersDirty = true;
        MarkDirty();
        UpdateComponentSpaceTransforms();
        marker.NotifyTransformChanged();
    }

    internal void SetMarkerRotator(BioStageOverlayMarker marker, Rotator rotator)
    {
        if (IsReadOnly || MeshComponent?.RefSkeleton is not { Length: > 0 } refSkeleton || (uint)marker.BoneIndex >= (uint)refSkeleton.Length)
        {
            return;
        }

        MeshBone bone = refSkeleton[marker.BoneIndex];
        Quaternion newOrientation = Quaternion.Normalize(rotator.ToQuaternion());
        if (bone.Orientation == newOrientation)
        {
            return;
        }

        bone.Orientation = newOrientation;
        _stageMarkersDirty = true;
        MarkDirty();
        UpdateComponentSpaceTransforms();
        marker.NotifyTransformChanged();
    }

    private void RenderNodeMarker(LevelEditorRenderContext context, BioStageOverlayMarker marker, Matrix4x4 markerToWorld, Vector3 markerPosition, float markerSize)
    {
        Vector4 nodeColor = new(0.23f, 0.90f, 0.43f, 1f);
        float half = markerSize * 0.5f;
        Vector3 xAxis = GetAxis(markerToWorld, Vector3.UnitX);
        Vector3 yAxis = GetAxis(markerToWorld, Vector3.UnitY);
        Vector3 zAxis = GetAxis(markerToWorld, Vector3.UnitZ);

        context.Primitives.AddLine(markerPosition - xAxis * half, markerPosition + xAxis * half, nodeColor, marker.HitID);
        context.Primitives.AddLine(markerPosition - yAxis * half, markerPosition + yAxis * half, nodeColor, marker.HitID);
        context.Primitives.AddLine(markerPosition - zAxis * half, markerPosition + zAxis * half, nodeColor, marker.HitID);
    }

    private void RenderCameraMarker(LevelEditorRenderContext context, BioStageOverlayMarker marker, Matrix4x4 markerToWorld, Vector3 markerPosition, float markerSize)
    {
        Vector4 cameraColor = new(0.30f, 0.62f, 1f, 1f);
        Vector3 forward = GetAxis(markerToWorld, Vector3.UnitX);
        Vector3 right = GetAxis(markerToWorld, Vector3.UnitY);
        Vector3 up = GetAxis(markerToWorld, Vector3.UnitZ);

        float nearDistance = markerSize * 0.8f;
        float farDistance = markerSize * 2.0f;
        float nearHalfWidth = markerSize * 0.35f;
        float nearHalfHeight = markerSize * 0.26f;
        float farHalfWidth = markerSize * 0.85f;
        float farHalfHeight = markerSize * 0.62f;

        Vector3 nearCenter = markerPosition + forward * nearDistance;
        Vector3 farCenter = markerPosition + forward * farDistance;

        Vector3 nearTopLeft = nearCenter + up * nearHalfHeight - right * nearHalfWidth;
        Vector3 nearTopRight = nearCenter + up * nearHalfHeight + right * nearHalfWidth;
        Vector3 nearBottomLeft = nearCenter - up * nearHalfHeight - right * nearHalfWidth;
        Vector3 nearBottomRight = nearCenter - up * nearHalfHeight + right * nearHalfWidth;

        Vector3 farTopLeft = farCenter + up * farHalfHeight - right * farHalfWidth;
        Vector3 farTopRight = farCenter + up * farHalfHeight + right * farHalfWidth;
        Vector3 farBottomLeft = farCenter - up * farHalfHeight - right * farHalfWidth;
        Vector3 farBottomRight = farCenter - up * farHalfHeight + right * farHalfWidth;

        // Camera body
        context.Primitives.AddLine(markerPosition, nearCenter, cameraColor, marker.HitID);
        context.Primitives.AddLine(markerPosition, nearTopLeft, cameraColor, marker.HitID);
        context.Primitives.AddLine(markerPosition, nearTopRight, cameraColor, marker.HitID);
        context.Primitives.AddLine(markerPosition, nearBottomLeft, cameraColor, marker.HitID);
        context.Primitives.AddLine(markerPosition, nearBottomRight, cameraColor, marker.HitID);

        // Near plane
        context.Primitives.AddLine(nearTopLeft, nearTopRight, cameraColor, marker.HitID);
        context.Primitives.AddLine(nearTopRight, nearBottomRight, cameraColor, marker.HitID);
        context.Primitives.AddLine(nearBottomRight, nearBottomLeft, cameraColor, marker.HitID);
        context.Primitives.AddLine(nearBottomLeft, nearTopLeft, cameraColor, marker.HitID);

        // Frustum edges
        context.Primitives.AddLine(nearTopLeft, farTopLeft, cameraColor, marker.HitID);
        context.Primitives.AddLine(nearTopRight, farTopRight, cameraColor, marker.HitID);
        context.Primitives.AddLine(nearBottomLeft, farBottomLeft, cameraColor, marker.HitID);
        context.Primitives.AddLine(nearBottomRight, farBottomRight, cameraColor, marker.HitID);

        // Far plane
        context.Primitives.AddLine(farTopLeft, farTopRight, cameraColor, marker.HitID);
        context.Primitives.AddLine(farTopRight, farBottomRight, cameraColor, marker.HitID);
        context.Primitives.AddLine(farBottomRight, farBottomLeft, cameraColor, marker.HitID);
        context.Primitives.AddLine(farBottomLeft, farTopLeft, cameraColor, marker.HitID);

        // Direction and up indicator
        context.Primitives.AddLine(markerPosition, farCenter, cameraColor, marker.HitID);
        Vector3 farTopCenter = farCenter + up * farHalfHeight;
        context.Primitives.AddLine(farTopCenter, farTopCenter + up * (markerSize * 0.45f), cameraColor, marker.HitID);
    }

    private static Matrix4x4 CreateBoneLocalTransform(MeshBone bone)
    {
        Quaternion orientation = bone.Orientation;
        if (orientation.LengthSquared() <= 0.0001f)
        {
            orientation = Quaternion.Identity;
        }
        else
        {
            orientation = Quaternion.Normalize(orientation);
        }

        return Matrix4x4.CreateFromQuaternion(orientation) * Matrix4x4.CreateTranslation(bone.Position);
    }

    private static bool IsStageNodeBoneName(string name)
    {
        return name.Contains("node", StringComparison.OrdinalIgnoreCase);
    }

    private static int ResolveNodeOverlayNumber(string boneName, int fallback)
    {
        int index = 0;
        while (index < boneName.Length)
        {
            if (!char.IsDigit(boneName[index]))
            {
                index++;
                continue;
            }

            int start = index;
            while (index < boneName.Length && char.IsDigit(boneName[index]))
            {
                index++;
            }

            if (int.TryParse(boneName.AsSpan(start, index - start), out int parsed))
            {
                return parsed;
            }
        }

        return fallback;
    }

    private static bool IsStageCameraBoneName(string name)
    {
        return name.StartsWith("cam", StringComparison.OrdinalIgnoreCase)
            || name.Contains("camera", StringComparison.OrdinalIgnoreCase);
    }

    private static Vector3 GetAxis(Matrix4x4 transform, Vector3 fallback)
    {
        Vector3 axis = Vector3.TransformNormal(fallback, transform);
        if (axis.LengthSquared() <= 0.0001f)
        {
            return fallback;
        }

        return Vector3.Normalize(axis);
    }
}
public class SFXStuntActorProxy : ActorProxy
{
    public SkeletalMeshComponentProxy BodyMesh;
    public SkeletalMeshComponentProxy HeadMesh;
    public SkeletalMeshComponentProxy HairMesh;
    public SkeletalMeshComponentProxy HeadGearMesh;
    public SFXStuntActorProxy(IActorEditorContext context, ExportEntry actorExport) : base(context, actorExport)
    {
        AddComponent(context.RenderContext, ref BodyMesh);
        if (BodyMesh.Translation == Vector3.Zero)
        {
            //from the defaultproperties, which condenseproperties currently does not fetch
            BodyMesh.Translation = new Vector3(0, 0, -88);
        }
        AddComponent(context.RenderContext, ref HeadMesh);
        ApplyMorphFace(HeadMesh);
        AddComponent(context.RenderContext, ref HairMesh);
        AddComponent(context.RenderContext, ref HeadGearMesh);
    }

    public override void SetAnimation(AnimSequence animSequence, float pos)
    {
        BodyMesh?.SetAnimation(animSequence, pos);
        HeadMesh?.SetAnimation(animSequence, pos);
        HairMesh?.SetAnimation(animSequence, pos);
        HeadGearMesh?.SetAnimation(animSequence, pos);
    }
}
public class BioArtPlaceableProxy : ActorProxy
{
    public MeshComponentProxy PlaceableMesh;
    public MeshComponentProxy DestroyedMesh;
    public BioArtPlaceableProxy(IActorEditorContext context, ExportEntry actorExport) : base(context, actorExport)
    {
        AddComponent(context.RenderContext, ref PlaceableMesh);
        AddComponent(context.RenderContext, ref DestroyedMesh);
        if (PlaceableMesh is not null)
        {
            DestroyedMesh?.IsVisible = false;
        }
    }
}
public class PawnProxy : ActorProxy
{
    public SkeletalMeshComponentProxy Mesh;
    public PawnProxy(IActorEditorContext context, ExportEntry actorExport) : base(context, actorExport)
    {
        AddComponent(context.RenderContext, ref Mesh);
    }

    public override void SetAnimation(AnimSequence animSequence, float pos)
    {
        Mesh?.SetAnimation(animSequence, pos);
    }
}
public class BioPawnProxy : PawnProxy
{
    public SkeletalMeshComponentProxy HeadMesh;
    public SkeletalMeshComponentProxy m_oHairMesh;
    public SkeletalMeshComponentProxy m_oHeadGearMesh;
    public SkeletalMeshComponentProxy m_oVisorMesh;
    public SkeletalMeshComponentProxy m_oFacePlateMesh;
    public List<SkeletalMeshComponentProxy> m_aoAccessories = [];

    public BioPawnProxy(IActorEditorContext context, ExportEntry actorExport) : base(context, actorExport)
    {
        AddComponent(context.RenderContext, ref HeadMesh, actorExport.Game.IsGame1() ? "m_oHeadMesh" : nameof(HeadMesh));
        ApplyMorphFace(HeadMesh);
        AddComponent(context.RenderContext, ref m_oHairMesh);
        AddComponent(context.RenderContext, ref m_oHeadGearMesh);
        AddComponent(context.RenderContext, ref m_oVisorMesh);
        AddComponent(context.RenderContext, ref m_oFacePlateMesh);
        AddComponentArray(context.RenderContext, ref m_aoAccessories);
    }

    public override void SetAnimation(AnimSequence animSequence, float pos)
    {
        base.SetAnimation(animSequence, pos);
        HeadMesh?.SetAnimation(animSequence, pos);
        m_oHairMesh?.SetAnimation(animSequence, pos);
        m_oHeadGearMesh?.SetAnimation(animSequence, pos);
        m_oVisorMesh?.SetAnimation(animSequence, pos);
        m_oFacePlateMesh?.SetAnimation(animSequence, pos);
        foreach (var accessory in m_aoAccessories)
        {
            accessory?.SetAnimation(animSequence, pos);
        }
    }
}

public abstract class CollectionActorComponentProxy : ActorProxy
{
    public ExportEntry CollectionActorExport { get; }

    protected CollectionActorComponentProxy(IActorEditorContext context, StaticCollectionActor collectionActor, ExportEntry componentActor, int index)
        : this(
            context,
            collectionActor.Export,
            componentActor,
            collectionActor.LocalToWorldTransforms[index],
            collectionActor.GetDecomposedTransformationForIndex(index))
    {
    }

    protected CollectionActorComponentProxy(
        IActorEditorContext context,
        ExportEntry collectionActorExport,
        ExportEntry componentActor,
        Matrix4x4 localToWorld,
        (Vector3 location, Vector3 scale, Rotator rotation) transform) : base(componentActor)
    {
        Editor = context;
        CollectionActorExport = collectionActorExport;

        LocalToWorld = localToWorld;
        (location, drawScale3D, rotation) = transform;
        if (drawScale3D.X == drawScale3D.Y && drawScale3D.X == drawScale3D.Z)
        {
            drawScale = drawScale3D.X;
            drawScale3D = Vector3.One;
        }
        _cleanSnapshot = SnapshotTransform();
    }

    public override void CommitChanges(PackageCache packageCache = null)
    {
        throw new InvalidOperationException($"Cannot be called on a {nameof(CollectionActorComponentProxy)}.");
    }

    public void CommitChanges(StaticCollectionActor collectionActor)
    {
        if (!(collectionActor.Components.FindIndex(uIdx => uIdx == Export.UIndex) is int idx and >= 0))
        {
            throw new ArgumentException("Does not contain this component", nameof(collectionActor));
        }
        Matrix4x4 m = ActorUtils.ComposeLocalToWorld(Location, Rotation, DrawScale * DrawScale3D, PrePivot);
        collectionActor.LocalToWorldTransforms[idx] = m;
        foreach (var component in Components)
        {
            component.CommitChanges();
        }
    }

    public override bool TestUIndexes(HashSet<int> uIndexes)
    {
        return base.TestUIndexes(uIndexes) || uIndexes.Contains(CollectionActorExport.UIndex);
    }
}

public class StaticMeshComponentActorProxy : CollectionActorComponentProxy
{
    public StaticMeshComponentActorProxy(IActorEditorContext context, ExportEntry smcExport, StaticMeshCollectionActor smca, int smcaIndex) : base(context, smca, smcExport, smcaIndex)
    {
        InitializeComponent(context, smcExport);
    }

    internal StaticMeshComponentActorProxy(
        IActorEditorContext context,
        ExportEntry collectionExport,
        ExportEntry smcExport,
        Matrix4x4 localToWorld,
        Vector3 location,
        Vector3 scale,
        Rotator rotation) : base(context, collectionExport, smcExport, localToWorld, (location, scale, rotation))
    {
        InitializeComponent(context, smcExport);
    }

    private void InitializeComponent(IActorEditorContext context, ExportEntry smcExport)
    {
        var staticMeshComponentProxy = PrimitiveComponentProxy.Create(context.RenderContext, smcExport, this);
        Components.Add(staticMeshComponentProxy);
        IsVolumetricMesh = (staticMeshComponentProxy as StaticMeshComponentProxy)?.IsVolumetric ?? false;
    }
}

public class StaticLightComponentActorProxy : CollectionActorComponentProxy
{
    public LightComponentProxy LightComponent;

    public StaticLightComponentActorProxy(IActorEditorContext context, ExportEntry lightComponentExport, StaticLightCollectionActor slca, int slcaIndex) : base(context, slca, lightComponentExport, slcaIndex)
    {
        InitializeComponent(context, lightComponentExport);
    }

    internal StaticLightComponentActorProxy(
        IActorEditorContext context,
        ExportEntry collectionExport,
        ExportEntry lightComponentExport,
        Matrix4x4 localToWorld,
        Vector3 location,
        Vector3 scale,
        Rotator rotation) : base(context, collectionExport, lightComponentExport, localToWorld, (location, scale, rotation))
    {
        InitializeComponent(context, lightComponentExport);
    }

    private void InitializeComponent(IActorEditorContext context, ExportEntry lightComponentExport)
    {
        IsLight = true;
        if (PrimitiveComponentProxy.Create(context.RenderContext, lightComponentExport, this) is LightComponentProxy lightComponentProxy)
        {
            LightComponent = lightComponentProxy;
            LightEditorComponent = lightComponentProxy;
            Components.Add(lightComponentProxy);
        }
    }
}

public class PrefabInstanceProxy : ActorProxy
{
    private readonly List<ActorProxy> Actors = [];
    private readonly List<Matrix4x4> RelativeMatrices = [];

    public PrefabInstanceProxy(IActorEditorContext context, ExportEntry actorExport) : base(context, actorExport)
    {
        PackageCache packageCache = context.RenderContext.PackageCache;
        if (Properties.GetProp<ObjectProperty>("TemplatePrefab")?
            .ResolveToExport(Pcc, packageCache) is ExportEntry prefab
            && prefab.GetProperty<ArrayProperty<ObjectProperty>>("PrefabArchetypes") is { } prefabActors)
        {

            foreach (var objProp in prefabActors)
            {
                if (objProp.TryResolveExport(prefab.FileRef, packageCache, out ExportEntry prefabActor)
                    && Create(context, prefabActor) is ActorProxy prefabActorProxy)
                {
                    prefabActorProxy.Editor = null; // prevent IsDirty being marked

                    var actorRelative = ActorUtils.ComposeLocalToWorld(prefabActorProxy.Location, prefabActorProxy.Rotation, Vector3.One);
                    (prefabActorProxy.Location, _, prefabActorProxy.Rotation) = (actorRelative * LocalToWorld).UnrealDecompose();
                    Actors.Add(prefabActorProxy);
                    RelativeMatrices.Add(actorRelative);
                }
            }
        }
    }

    public override void UpdateScene(LevelEditorRenderContext context, float deltaTime)
    {
        foreach (var actor in Actors)
        {
            actor.UpdateScene(context, deltaTime);
        }
    }

    public override void Render(LevelEditorRenderContext context, RenderPass pass)
    {
        foreach (var actor in Actors)
        {
            actor.HitID = HitID;
            if (actor.IsLight && !context.ShowLights) continue;
            if (actor.IsVolume && !context.ShowVolumes) continue;
            if (actor.IsVolumetricMesh && !context.ShowVolumetrics) continue;
            if (actor.IsEmitter && !context.ShowEmitters) continue;
            if (actor.IsLocationActor && !context.ShowLocationActors) continue;
            if (actor.IsAmbientSound && !context.ShowSoundPositions) continue;
            if (actor.IsCinematicActor && !context.ShowCinematicActors) continue;
            if (actor.IsDecalActor && !context.ShowDecalActors) continue;
            actor.Render(context, pass);
        }
    }

    protected override void UpdateLocalToWorld()
    {
        //Unreal appears to ignore scaling on a prefab
        LocalToWorld = ActorUtils.ComposeLocalToWorld(Location, Rotation, Vector3.One);
        for (int i = 0; i < Actors.Count; i++)
        {
            ActorProxy actor = Actors[i];
            (actor.Location, _, actor.Rotation) = (RelativeMatrices[i] * LocalToWorld).UnrealDecompose();
        }
    }

    public override BoxSphereBounds GetBounds()
    {
        if (Actors.Count is 0)
        {
            return new BoxSphereBounds
            {
                Origin = LocalToWorld.Translation
            };
        }
        var bounds = Actors[0].GetBounds();
        for (int i = 1; i < Actors.Count; i++)
        {
            bounds = bounds.Union(Actors[i].GetBounds());
        }
        return bounds;
    }

    public override void CommitChanges(PackageCache packageCache = null)
    {
        var props = Properties;

        string locationPropName = Export.Game.IsGame3() ? "location" : "Location";
        if (props.ContainsNamedProp(locationPropName) || Location != Vector3.Zero)
        {
            props.AddOrReplaceProp(CommonStructs.Vector3Prop(Location, locationPropName));
        }
        if (props.ContainsNamedProp("Rotation") || !Rotation.IsZero)
        {
            props.AddOrReplaceProp(CommonStructs.RotatorProp(Rotation, "Rotation"));
        }
        Export.WriteProperties(props);
    }

    public override bool TestUIndexes(HashSet<int> uIndexes)
    {
        if (uIndexes.Contains(Export.UIndex))
        {
            return true;
        }
        foreach (var actor in Actors)
        {
            if (actor.TestUIndexes(uIndexes))
            {
                return true;
            }
        }
        return false;
    }
    protected override void Dispose(bool disposing)
    {
        if (!isDisposed)
        {
            if (disposing)
            {
                foreach (var actor in Actors)
                {
                    actor.Dispose();
                }
            }
            isDisposed = true;
        }
    }

}
public class SFXDroppedPickupProxy : ActorProxy
{
    public SkeletalMeshComponentProxy PickupMesh;
    public SFXDroppedPickupProxy(IActorEditorContext context, ExportEntry actorExport) : base(context, actorExport)
    {
        AddComponent(context.RenderContext, ref PickupMesh);
    }
}

public class LightActorProxy : ActorProxy
{
    public LightComponentProxy LightComponent;

    public LightActorProxy(IActorEditorContext context, ExportEntry actorExport) : base(context, actorExport)
    {
        IsLight = true;
        AddComponent(context.RenderContext, ref LightComponent);
        LightEditorComponent = LightComponent;
    }
}

public enum IconActorCategory
{
    Emitter,
    StartPoint,
    TargetPoint,
    PointOfInterest,
    WwiseAmbientSound,
    AmbientSound,
    WwiseMic,
    Camera,
    SceneCapture2D,
    Decal,
    MaterialInstance,
    LensFlareLight,
    ScreenCaptureReflect
}

public class IconActorProxy : ActorProxy
{
    public IconActorCategory IconCategory { get; }

    public IconActorProxy(IActorEditorContext context, ExportEntry actorExport, IconActorCategory iconCategory)
        : base(context, actorExport)
    {
        IconCategory = iconCategory;
        switch (IconCategory)
        {
            case IconActorCategory.Emitter:
                IsEmitter = true;
                break;
            case IconActorCategory.StartPoint:
                IsStartPoint = true;
                IsLocationActor = true;
                break;
            case IconActorCategory.TargetPoint:
                IsTargetPoint = true;
                IsLocationActor = true;
                break;
            case IconActorCategory.PointOfInterest:
                IsPointOfInterest = true;
                IsLocationActor = true;
                break;
            case IconActorCategory.AmbientSound:
            case IconActorCategory.WwiseMic:
                IsAmbientSound = true;
                break;
            case IconActorCategory.WwiseAmbientSound:
                IsCinematicActor = true;
                break;
            case IconActorCategory.Camera:
                IsCameraActor = true;
                IsCinematicActor = true;
                break;
            case IconActorCategory.SceneCapture2D:
                IsCameraActor = true;
                IsCinematicActor = true;
                break;
            case IconActorCategory.ScreenCaptureReflect:
                IsCameraActor = true;
                IsCinematicActor = true;
                break;
            case IconActorCategory.Decal:
            case IconActorCategory.MaterialInstance:
                IsDecalActor = true;
                break;
            case IconActorCategory.LensFlareLight:
                IsLight = true;
                break;
        }
    }

    public override void Render(LevelEditorRenderContext context, RenderPass pass)
    {
        if (pass is not (RenderPass.Base or RenderPass.Hair))
        {
            return;
        }

        Vector4 color = IconCategory switch
        {
            IconActorCategory.Emitter => new Vector4(1.0f, 0.52f, 0.12f, 1f),
            IconActorCategory.StartPoint => new Vector4(0.18f, 1.0f, 0.32f, 1f),
            IconActorCategory.TargetPoint => new Vector4(1.0f, 0.22f, 0.22f, 1f),
            IconActorCategory.PointOfInterest => new Vector4(1.0f, 0.58f, 0.18f, 1f),
            IconActorCategory.WwiseAmbientSound => new Vector4(0.72f, 0.54f, 1.0f, 1f),
            IconActorCategory.AmbientSound => new Vector4(0.25f, 0.88f, 1.0f, 1f),
            IconActorCategory.WwiseMic => new Vector4(0.36f, 0.62f, 1.0f, 1f),
            IconActorCategory.Camera => new Vector4(1.0f, 0.92f, 0.18f, 1f),
            IconActorCategory.SceneCapture2D => new Vector4(0.78f, 0.42f, 1.0f, 1f),
            IconActorCategory.ScreenCaptureReflect => new Vector4(0.32f, 0.78f, 1.0f, 1f),
            IconActorCategory.Decal => new Vector4(0.92f, 0.38f, 1.0f, 1f),
            IconActorCategory.MaterialInstance => new Vector4(0.55f, 0.30f, 1.0f, 1f),
            IconActorCategory.LensFlareLight => new Vector4(1.0f, 0.95f, 0.35f, 1f),
            _ => Vector4.One
        };

        float categoryScale = IconCategory switch
        {
            IconActorCategory.Emitter => 1.06f,
            IconActorCategory.StartPoint => 1.14f,
            IconActorCategory.TargetPoint => 1.1f,
            IconActorCategory.WwiseAmbientSound => 1.08f,
            IconActorCategory.AmbientSound => 1.08f,
            IconActorCategory.WwiseMic => 1.08f,
            IconActorCategory.Decal => 1.05f,
            IconActorCategory.MaterialInstance => 1.05f,
            IconActorCategory.Camera => 1.12f,
            IconActorCategory.SceneCapture2D => 1.16f,
            IconActorCategory.ScreenCaptureReflect => 1.14f,
            _ => 1f
        };

        float radius = 10.5f * categoryScale;
        if (!context.Camera.IsOrthographic)
        {
            float distance = Vector3.Distance(LocalToWorld.Translation, context.Camera.Position);
            radius = Math.Clamp((7f + (distance * 0.0042f)) * categoryScale, 7f, 24f);
        }

        Matrix4x4 iconTransform = IconCategory == IconActorCategory.ScreenCaptureReflect
            || IconCategory == IconActorCategory.SceneCapture2D
            ? ActorUtils.ComposeLocalToWorld(Location, Rotation, Vector3.One)
            : Matrix4x4.CreateTranslation(LocalToWorld.Translation);

        var mesh = context.Primitives.BuildMesh(color, HitID, iconTransform);
        switch (IconCategory)
        {
            case IconActorCategory.StartPoint:
                RenderStartFlag(mesh, radius);
                break;
            case IconActorCategory.TargetPoint:
            case IconActorCategory.PointOfInterest:
                RenderLocationCrosshair(mesh, radius);
                break;
            case IconActorCategory.Emitter:
                RenderEmitterConeBurst(mesh, radius);
                break;
            case IconActorCategory.AmbientSound:
            case IconActorCategory.WwiseAmbientSound:
            case IconActorCategory.WwiseMic:
                RenderSoundSpeakerWaves(mesh, radius);
                break;
            case IconActorCategory.Decal:
            case IconActorCategory.MaterialInstance:
                RenderDecalProjectionStamp(mesh, radius);
                break;
            case IconActorCategory.Camera:
                RenderFilmCameraSilhouette(mesh, radius);
                break;
            case IconActorCategory.SceneCapture2D:
                RenderFilmCameraSilhouette(mesh, radius * 0.92f);
                var captureFrustumMesh = context.Primitives.BuildMesh(new Vector4(0.70f, 0.45f, 1.0f, 0.34f), HitID, iconTransform);
                RenderSceneCapture2DFrustum(captureFrustumMesh, radius);
                break;
            case IconActorCategory.ScreenCaptureReflect:
                RenderFilmCameraSilhouette(mesh, radius);
                var coneMesh = context.Primitives.BuildMesh(new Vector4(0.24f, 0.70f, 1.0f, 0.38f), HitID, iconTransform);
                RenderCaptureDirectionConeWire(coneMesh, radius);
                break;
            default:
                RenderOrb(mesh, radius);
                break;
        }
    }

    private void RenderSceneCapture2DFrustum(BatchedPrimitives.MeshBuilder mesh, float radius)
    {
        var sourceProps = GetSceneCapture2DSourceProperties();

        float fovDegrees = ReadFloatProperty(sourceProps, "FOVAngle", 90f);
        float aspect = ReadFloatProperty(sourceProps, "AspectRatio", 1.777f);
        float nearPlane = ReadFloatProperty(sourceProps, "NearPlane", 10f);
        float farPlane = ReadFloatProperty(sourceProps, "FarPlane", 2500f);
        float orthoWidth = ReadFloatProperty(sourceProps, "OrthoWidth", 1024f);

        bool isOrtho = false;
        if (sourceProps.GetProp<EnumProperty>("ProjectionType") is { } projectionType)
        {
            isOrtho = projectionType.Value.Instanced.Contains("Ortho", StringComparison.OrdinalIgnoreCase);
        }
        else if (sourceProps.GetProp<BoolProperty>("bUseOrthoProjection") is { } useOrthoProjection)
        {
            isOrtho = useOrthoProjection.Value;
        }

        float clampedAspect = Math.Clamp(aspect, 0.55f, 2.4f);
        float clampedFov = Math.Clamp(fovDegrees, 12f, 150f);

        float depthScale = farPlane > nearPlane
            ? Math.Clamp(MathF.Log10((farPlane - nearPlane) + 10f) / 3f, 0.7f, 2.4f)
            : 1.2f;

        float nearX = radius * 0.95f;
        float farX = radius * (2.15f + (depthScale * 1.15f));
        float lineHalfWidth = radius * 0.028f;

        float nearHalfHeight;
        float nearHalfWidth;
        float farHalfHeight;
        float farHalfWidth;

        if (isOrtho)
        {
            float orthoScale = Math.Clamp(MathF.Log10(orthoWidth + 10f) / 2.7f, 0.55f, 2.2f);
            nearHalfHeight = radius * 0.36f * orthoScale;
            nearHalfWidth = nearHalfHeight * clampedAspect;
            farHalfHeight = nearHalfHeight;
            farHalfWidth = nearHalfWidth;
        }
        else
        {
            float tanHalfFov = MathF.Tan((clampedFov * (MathF.PI / 180f)) * 0.5f);
            nearHalfHeight = nearX * tanHalfFov * 0.33f;
            nearHalfWidth = nearHalfHeight * clampedAspect;
            farHalfHeight = farX * tanHalfFov * 0.33f;
            farHalfWidth = farHalfHeight * clampedAspect;
        }

        Vector3 n0 = new(nearX, -nearHalfWidth, -nearHalfHeight);
        Vector3 n1 = new(nearX, nearHalfWidth, -nearHalfHeight);
        Vector3 n2 = new(nearX, nearHalfWidth, nearHalfHeight);
        Vector3 n3 = new(nearX, -nearHalfWidth, nearHalfHeight);

        Vector3 f0 = new(farX, -farHalfWidth, -farHalfHeight);
        Vector3 f1 = new(farX, farHalfWidth, -farHalfHeight);
        Vector3 f2 = new(farX, farHalfWidth, farHalfHeight);
        Vector3 f3 = new(farX, -farHalfWidth, farHalfHeight);

        int v = 0;
        AddLinePrism(mesh, ref v, n0, n1, lineHalfWidth);
        AddLinePrism(mesh, ref v, n1, n2, lineHalfWidth);
        AddLinePrism(mesh, ref v, n2, n3, lineHalfWidth);
        AddLinePrism(mesh, ref v, n3, n0, lineHalfWidth);

        AddLinePrism(mesh, ref v, f0, f1, lineHalfWidth);
        AddLinePrism(mesh, ref v, f1, f2, lineHalfWidth);
        AddLinePrism(mesh, ref v, f2, f3, lineHalfWidth);
        AddLinePrism(mesh, ref v, f3, f0, lineHalfWidth);

        AddLinePrism(mesh, ref v, n0, f0, lineHalfWidth);
        AddLinePrism(mesh, ref v, n1, f1, lineHalfWidth);
        AddLinePrism(mesh, ref v, n2, f2, lineHalfWidth);
        AddLinePrism(mesh, ref v, n3, f3, lineHalfWidth);

        AddLinePrism(mesh, ref v, new Vector3(radius * 0.70f, 0f, 0f), new Vector3(farX, 0f, 0f), lineHalfWidth * 0.80f);
    }

    private PropertyCollection GetSceneCapture2DSourceProperties()
    {
        if (Properties.GetProp<ObjectProperty>("CaptureComponent2D")?.ResolveToExport(Pcc, Editor?.PackageCache) is { } captureComponent2D)
        {
            return captureComponent2D.GetCondensedProperties();
        }

        if (Properties.GetProp<ObjectProperty>("SceneCapture")?.ResolveToExport(Pcc, Editor?.PackageCache) is { } sceneCaptureComponent)
        {
            return sceneCaptureComponent.GetCondensedProperties();
        }

        if (Properties.GetProp<ObjectProperty>("SceneCaptureComponent")?.ResolveToExport(Pcc, Editor?.PackageCache) is { } sceneCaptureComponent2)
        {
            return sceneCaptureComponent2.GetCondensedProperties();
        }

        if (Properties.GetProp<ArrayProperty<ObjectProperty>>("Components") is { } components)
        {
            foreach (IEntry entry in components.ResolveToEntries(Pcc))
            {
                if (entry is ExportEntry cmpExport
                    && cmpExport.ClassName.Contains("SceneCapture2D", StringComparison.OrdinalIgnoreCase))
                {
                    return cmpExport.GetCondensedProperties();
                }
            }
        }

        return Properties;
    }

    private float ReadFloatProperty(PropertyCollection primarySource, string propName, float defaultValue)
    {
        if (primarySource.GetProp<FloatProperty>(propName) is { } floatProp)
        {
            return floatProp.Value;
        }

        if (primarySource.GetProp<IntProperty>(propName) is { } intProp)
        {
            return intProp.Value;
        }

        if (!ReferenceEquals(primarySource, Properties))
        {
            if (Properties.GetProp<FloatProperty>(propName) is { } actorFloatProp)
            {
                return actorFloatProp.Value;
            }

            if (Properties.GetProp<IntProperty>(propName) is { } actorIntProp)
            {
                return actorIntProp.Value;
            }
        }

        return defaultValue;
    }

    private static void AddLinePrism(BatchedPrimitives.MeshBuilder mesh, ref int vertexCounter, Vector3 a, Vector3 b, float halfWidth)
    {
        Vector3 d = b - a;
        if (d.LengthSquared() < 0.0001f)
        {
            return;
        }

        Vector3 forward = Vector3.Normalize(d);
        Vector3 fallbackUp = MathF.Abs(Vector3.Dot(forward, Vector3.UnitZ)) > 0.92f ? Vector3.UnitY : Vector3.UnitZ;
        Vector3 right = Vector3.Normalize(Vector3.Cross(fallbackUp, forward)) * halfWidth;
        Vector3 up = Vector3.Normalize(Vector3.Cross(forward, right)) * halfWidth;

        int a0 = AddIndexedVertex(mesh, ref vertexCounter, a.X + right.X + up.X, a.Y + right.Y + up.Y, a.Z + right.Z + up.Z);
        int a1 = AddIndexedVertex(mesh, ref vertexCounter, a.X + right.X - up.X, a.Y + right.Y - up.Y, a.Z + right.Z - up.Z);
        int a2 = AddIndexedVertex(mesh, ref vertexCounter, a.X - right.X - up.X, a.Y - right.Y - up.Y, a.Z - right.Z - up.Z);
        int a3 = AddIndexedVertex(mesh, ref vertexCounter, a.X - right.X + up.X, a.Y - right.Y + up.Y, a.Z - right.Z + up.Z);

        int b0 = AddIndexedVertex(mesh, ref vertexCounter, b.X + right.X + up.X, b.Y + right.Y + up.Y, b.Z + right.Z + up.Z);
        int b1 = AddIndexedVertex(mesh, ref vertexCounter, b.X + right.X - up.X, b.Y + right.Y - up.Y, b.Z + right.Z - up.Z);
        int b2 = AddIndexedVertex(mesh, ref vertexCounter, b.X - right.X - up.X, b.Y - right.Y - up.Y, b.Z - right.Z - up.Z);
        int b3 = AddIndexedVertex(mesh, ref vertexCounter, b.X - right.X + up.X, b.Y - right.Y + up.Y, b.Z - right.Z + up.Z);

        AddQuad(mesh, a0, b0, b1, a1);
        AddQuad(mesh, a1, b1, b2, a2);
        AddQuad(mesh, a2, b2, b3, a3);
        AddQuad(mesh, a3, b3, b0, a0);
    }

    private static void RenderStartFlag(BatchedPrimitives.MeshBuilder mesh, float radius)
    {
        float poleHalfWidth = radius * 0.08f;
        float poleMinZ = -radius * 0.9f;
        float poleMaxZ = radius * 0.95f;
        float poleX = -radius * 0.22f;

        // Pole box (verts 0..7)
        mesh.AddVertex(poleX - poleHalfWidth, -poleHalfWidth, poleMinZ);
        mesh.AddVertex(poleX + poleHalfWidth, -poleHalfWidth, poleMinZ);
        mesh.AddVertex(poleX + poleHalfWidth, poleHalfWidth, poleMinZ);
        mesh.AddVertex(poleX - poleHalfWidth, poleHalfWidth, poleMinZ);
        mesh.AddVertex(poleX - poleHalfWidth, -poleHalfWidth, poleMaxZ);
        mesh.AddVertex(poleX + poleHalfWidth, -poleHalfWidth, poleMaxZ);
        mesh.AddVertex(poleX + poleHalfWidth, poleHalfWidth, poleMaxZ);
        mesh.AddVertex(poleX - poleHalfWidth, poleHalfWidth, poleMaxZ);

        mesh.AddTriangle(0, 2, 1); mesh.AddTriangle(0, 3, 2);
        mesh.AddTriangle(4, 5, 6); mesh.AddTriangle(4, 6, 7);
        mesh.AddTriangle(0, 1, 5); mesh.AddTriangle(0, 5, 4);
        mesh.AddTriangle(1, 2, 6); mesh.AddTriangle(1, 6, 5);
        mesh.AddTriangle(2, 3, 7); mesh.AddTriangle(2, 7, 6);
        mesh.AddTriangle(3, 0, 4); mesh.AddTriangle(3, 4, 7);

        // Pennant box (verts 8..15)
        float flagMinX = poleX + poleHalfWidth;
        float flagMaxX = radius * 0.72f;
        float flagHalfY = radius * 0.11f;
        float flagMinZ = radius * 0.22f;
        float flagMaxZ = radius * 0.7f;

        mesh.AddVertex(flagMinX, -flagHalfY, flagMinZ);
        mesh.AddVertex(flagMaxX, -flagHalfY, flagMinZ);
        mesh.AddVertex(flagMaxX, flagHalfY, flagMinZ);
        mesh.AddVertex(flagMinX, flagHalfY, flagMinZ);
        mesh.AddVertex(flagMinX, -flagHalfY, flagMaxZ);
        mesh.AddVertex(flagMaxX, -flagHalfY, flagMaxZ);
        mesh.AddVertex(flagMaxX, flagHalfY, flagMaxZ);
        mesh.AddVertex(flagMinX, flagHalfY, flagMaxZ);

        mesh.AddTriangle(8, 10, 9); mesh.AddTriangle(8, 11, 10);
        mesh.AddTriangle(12, 13, 14); mesh.AddTriangle(12, 14, 15);
        mesh.AddTriangle(8, 9, 13); mesh.AddTriangle(8, 13, 12);
        mesh.AddTriangle(9, 10, 14); mesh.AddTriangle(9, 14, 13);
        mesh.AddTriangle(10, 11, 15); mesh.AddTriangle(10, 15, 14);
        mesh.AddTriangle(11, 8, 12); mesh.AddTriangle(11, 12, 15);
    }

    private static void RenderLocationCrosshair(BatchedPrimitives.MeshBuilder mesh, float radius)
    {
        float armLength = radius * 0.95f;
        float t = radius * 0.10f;
        float z = radius * 0.08f;

        // X arm plate (verts 0..7)
        mesh.AddVertex(-armLength, -t, -z);
        mesh.AddVertex(armLength, -t, -z);
        mesh.AddVertex(armLength, t, -z);
        mesh.AddVertex(-armLength, t, -z);
        mesh.AddVertex(-armLength, -t, z);
        mesh.AddVertex(armLength, -t, z);
        mesh.AddVertex(armLength, t, z);
        mesh.AddVertex(-armLength, t, z);

        mesh.AddTriangle(0, 2, 1); mesh.AddTriangle(0, 3, 2);
        mesh.AddTriangle(4, 5, 6); mesh.AddTriangle(4, 6, 7);
        mesh.AddTriangle(0, 1, 5); mesh.AddTriangle(0, 5, 4);
        mesh.AddTriangle(1, 2, 6); mesh.AddTriangle(1, 6, 5);
        mesh.AddTriangle(2, 3, 7); mesh.AddTriangle(2, 7, 6);
        mesh.AddTriangle(3, 0, 4); mesh.AddTriangle(3, 4, 7);

        // Y arm plate (verts 8..15)
        mesh.AddVertex(-t, -armLength, -z);
        mesh.AddVertex(t, -armLength, -z);
        mesh.AddVertex(t, armLength, -z);
        mesh.AddVertex(-t, armLength, -z);
        mesh.AddVertex(-t, -armLength, z);
        mesh.AddVertex(t, -armLength, z);
        mesh.AddVertex(t, armLength, z);
        mesh.AddVertex(-t, armLength, z);

        mesh.AddTriangle(8, 10, 9); mesh.AddTriangle(8, 11, 10);
        mesh.AddTriangle(12, 13, 14); mesh.AddTriangle(12, 14, 15);
        mesh.AddTriangle(8, 9, 13); mesh.AddTriangle(8, 13, 12);
        mesh.AddTriangle(9, 10, 14); mesh.AddTriangle(9, 14, 13);
        mesh.AddTriangle(10, 11, 15); mesh.AddTriangle(10, 15, 14);
        mesh.AddTriangle(11, 8, 12); mesh.AddTriangle(11, 12, 15);

        // Center diamond marker
        RenderOctahedron(mesh, radius * 0.30f);
    }

    private static void RenderSoundSpeakerWaves(BatchedPrimitives.MeshBuilder mesh, float radius)
    {
        int v = 0;

        // Speaker body (box)
        float bMinX = -radius * 0.62f;
        float bMaxX = -radius * 0.20f;
        float bHalfY = radius * 0.25f;
        float bHalfZ = radius * 0.32f;

        int b0 = AddIndexedVertex(mesh, ref v, bMinX, -bHalfY, -bHalfZ);
        int b1 = AddIndexedVertex(mesh, ref v, bMaxX, -bHalfY, -bHalfZ);
        int b2 = AddIndexedVertex(mesh, ref v, bMaxX, bHalfY, -bHalfZ);
        int b3 = AddIndexedVertex(mesh, ref v, bMinX, bHalfY, -bHalfZ);
        int b4 = AddIndexedVertex(mesh, ref v, bMinX, -bHalfY, bHalfZ);
        int b5 = AddIndexedVertex(mesh, ref v, bMaxX, -bHalfY, bHalfZ);
        int b6 = AddIndexedVertex(mesh, ref v, bMaxX, bHalfY, bHalfZ);
        int b7 = AddIndexedVertex(mesh, ref v, bMinX, bHalfY, bHalfZ);

        AddQuad(mesh, b0, b1, b2, b3);
        AddQuad(mesh, b4, b7, b6, b5);
        AddQuad(mesh, b0, b4, b5, b1);
        AddQuad(mesh, b1, b5, b6, b2);
        AddQuad(mesh, b2, b6, b7, b3);
        AddQuad(mesh, b3, b7, b4, b0);

        // Speaker cone (truncated to a point)
        float cBaseX = bMaxX;
        float cTipX = radius * 0.40f;
        float cHalfY = radius * 0.19f;
        float cHalfZ = radius * 0.24f;

        int c0 = AddIndexedVertex(mesh, ref v, cBaseX, -cHalfY, -cHalfZ);
        int c1 = AddIndexedVertex(mesh, ref v, cBaseX, cHalfY, -cHalfZ);
        int c2 = AddIndexedVertex(mesh, ref v, cBaseX, cHalfY, cHalfZ);
        int c3 = AddIndexedVertex(mesh, ref v, cBaseX, -cHalfY, cHalfZ);
        int cTip = AddIndexedVertex(mesh, ref v, cTipX, 0, 0);

        mesh.AddTriangle(c0, c1, cTip);
        mesh.AddTriangle(c1, c2, cTip);
        mesh.AddTriangle(c2, c3, cTip);
        mesh.AddTriangle(c3, c0, cTip);

        float centerX = bMinX - radius * 0.10f;
        float halfDepth = radius * 0.04f;
        AddWaveArc(mesh, ref v, centerX, halfDepth, radius * 0.20f, radius * 0.32f, MathF.PI);
        AddWaveArc(mesh, ref v, centerX, halfDepth, radius * 0.38f, radius * 0.52f, MathF.PI);
        AddWaveArc(mesh, ref v, centerX, halfDepth, radius * 0.58f, radius * 0.74f, MathF.PI);
    }

    private static void RenderDecalProjectionStamp(BatchedPrimitives.MeshBuilder mesh, float radius)
    {
        int v = 0;

        // Tilted stamp plate (diamond-like quad with thickness)
        float half = radius * 0.58f;
        float zTop = radius * 0.22f;
        float zBottom = radius * 0.04f;

        int p0 = AddIndexedVertex(mesh, ref v, 0, -half, zBottom);
        int p1 = AddIndexedVertex(mesh, ref v, half, 0, zBottom);
        int p2 = AddIndexedVertex(mesh, ref v, 0, half, zBottom);
        int p3 = AddIndexedVertex(mesh, ref v, -half, 0, zBottom);
        int p4 = AddIndexedVertex(mesh, ref v, 0, -half, zTop);
        int p5 = AddIndexedVertex(mesh, ref v, half, 0, zTop);
        int p6 = AddIndexedVertex(mesh, ref v, 0, half, zTop);
        int p7 = AddIndexedVertex(mesh, ref v, -half, 0, zTop);

        AddQuad(mesh, p0, p1, p2, p3);
        AddQuad(mesh, p4, p7, p6, p5);
        AddQuad(mesh, p0, p4, p5, p1);
        AddQuad(mesh, p1, p5, p6, p2);
        AddQuad(mesh, p2, p6, p7, p3);
        AddQuad(mesh, p3, p7, p4, p0);

        // Projection arrow shaft
        float shaftHalf = radius * 0.08f;
        float shaftTop = zBottom;
        float shaftBottom = -radius * 0.52f;

        int s0 = AddIndexedVertex(mesh, ref v, -shaftHalf, -shaftHalf, shaftBottom);
        int s1 = AddIndexedVertex(mesh, ref v, shaftHalf, -shaftHalf, shaftBottom);
        int s2 = AddIndexedVertex(mesh, ref v, shaftHalf, shaftHalf, shaftBottom);
        int s3 = AddIndexedVertex(mesh, ref v, -shaftHalf, shaftHalf, shaftBottom);
        int s4 = AddIndexedVertex(mesh, ref v, -shaftHalf, -shaftHalf, shaftTop);
        int s5 = AddIndexedVertex(mesh, ref v, shaftHalf, -shaftHalf, shaftTop);
        int s6 = AddIndexedVertex(mesh, ref v, shaftHalf, shaftHalf, shaftTop);
        int s7 = AddIndexedVertex(mesh, ref v, -shaftHalf, shaftHalf, shaftTop);

        AddQuad(mesh, s0, s1, s2, s3);
        AddQuad(mesh, s4, s7, s6, s5);
        AddQuad(mesh, s0, s4, s5, s1);
        AddQuad(mesh, s1, s5, s6, s2);
        AddQuad(mesh, s2, s6, s7, s3);
        AddQuad(mesh, s3, s7, s4, s0);

        // Arrow tip (projection direction)
        float tipBase = shaftBottom;
        float tipBottom = -radius * 0.92f;
        float tipHalf = radius * 0.20f;

        int t0 = AddIndexedVertex(mesh, ref v, -tipHalf, -tipHalf, tipBase);
        int t1 = AddIndexedVertex(mesh, ref v, tipHalf, -tipHalf, tipBase);
        int t2 = AddIndexedVertex(mesh, ref v, tipHalf, tipHalf, tipBase);
        int t3 = AddIndexedVertex(mesh, ref v, -tipHalf, tipHalf, tipBase);
        int tA = AddIndexedVertex(mesh, ref v, 0, 0, tipBottom);

        mesh.AddTriangle(t0, t1, tA);
        mesh.AddTriangle(t1, t2, tA);
        mesh.AddTriangle(t2, t3, tA);
        mesh.AddTriangle(t3, t0, tA);
    }

    private static void RenderFilmCameraSilhouette(BatchedPrimitives.MeshBuilder mesh, float radius)
    {
        int v = 0;

        // Camera body
        float bMinX = -radius * 0.45f;
        float bMaxX = radius * 0.35f;
        float bHalfY = radius * 0.22f;
        float bHalfZ = radius * 0.28f;

        int b0 = AddIndexedVertex(mesh, ref v, bMinX, -bHalfY, -bHalfZ);
        int b1 = AddIndexedVertex(mesh, ref v, bMaxX, -bHalfY, -bHalfZ);
        int b2 = AddIndexedVertex(mesh, ref v, bMaxX, bHalfY, -bHalfZ);
        int b3 = AddIndexedVertex(mesh, ref v, bMinX, bHalfY, -bHalfZ);
        int b4 = AddIndexedVertex(mesh, ref v, bMinX, -bHalfY, bHalfZ);
        int b5 = AddIndexedVertex(mesh, ref v, bMaxX, -bHalfY, bHalfZ);
        int b6 = AddIndexedVertex(mesh, ref v, bMaxX, bHalfY, bHalfZ);
        int b7 = AddIndexedVertex(mesh, ref v, bMinX, bHalfY, bHalfZ);

        AddQuad(mesh, b0, b1, b2, b3);
        AddQuad(mesh, b4, b7, b6, b5);
        AddQuad(mesh, b0, b4, b5, b1);
        AddQuad(mesh, b1, b5, b6, b2);
        AddQuad(mesh, b2, b6, b7, b3);
        AddQuad(mesh, b3, b7, b4, b0);

        // Lens barrel (forward)
        float lMinX = bMaxX;
        float lMaxX = radius * 0.75f;
        float lHalfY = radius * 0.16f;
        float lHalfZ = radius * 0.16f;

        int l0 = AddIndexedVertex(mesh, ref v, lMinX, -lHalfY, -lHalfZ);
        int l1 = AddIndexedVertex(mesh, ref v, lMaxX, -lHalfY, -lHalfZ);
        int l2 = AddIndexedVertex(mesh, ref v, lMaxX, lHalfY, -lHalfZ);
        int l3 = AddIndexedVertex(mesh, ref v, lMinX, lHalfY, -lHalfZ);
        int l4 = AddIndexedVertex(mesh, ref v, lMinX, -lHalfY, lHalfZ);
        int l5 = AddIndexedVertex(mesh, ref v, lMaxX, -lHalfY, lHalfZ);
        int l6 = AddIndexedVertex(mesh, ref v, lMaxX, lHalfY, lHalfZ);
        int l7 = AddIndexedVertex(mesh, ref v, lMinX, lHalfY, lHalfZ);

        AddQuad(mesh, l0, l1, l2, l3);
        AddQuad(mesh, l4, l7, l6, l5);
        AddQuad(mesh, l0, l4, l5, l1);
        AddQuad(mesh, l1, l5, l6, l2);
        AddQuad(mesh, l2, l6, l7, l3);
        AddQuad(mesh, l3, l7, l4, l0);

        // Film reels (two short cylinders so they read as circular reels)
        const int reelSegments = 16;
        float rearReelX = -radius * 0.24f;
        float frontReelX = radius * 0.02f;
        float reelCenterZ = bHalfZ + radius * 0.12f;
        float reelRadius = radius * 0.14f;
        float reelHalfThickness = radius * 0.06f;

        AddReelCylinder(mesh, ref v, rearReelX, 0f, reelCenterZ, reelRadius, reelHalfThickness, reelSegments);
        AddReelCylinder(mesh, ref v, frontReelX, 0f, reelCenterZ, reelRadius, reelHalfThickness, reelSegments);
    }

    private static void RenderCaptureDirectionConeWire(BatchedPrimitives.MeshBuilder mesh, float radius)
    {
        int v = 0;

        float tipX = radius * 0.84f;
        float baseX = radius * 2.05f;
        float baseRadius = radius * 0.92f;
        float ringHalfDepth = radius * 0.018f;
        float ringThickness = radius * 0.055f;

        AddRingBandYZ(mesh, ref v, tipX + (baseX - tipX) * 0.38f, baseRadius * 0.38f, ringHalfDepth, ringThickness);
        AddRingBandYZ(mesh, ref v, tipX + (baseX - tipX) * 0.68f, baseRadius * 0.68f, ringHalfDepth, ringThickness);
        AddRingBandYZ(mesh, ref v, baseX, baseRadius, ringHalfDepth, ringThickness);

        float ribHalfWidth = radius * 0.03f;
        for (int i = 0; i < 4; i++)
        {
            float angle = MathF.PI * 0.5f * i;
            float ca = MathF.Cos(angle);
            float sa = MathF.Sin(angle);

            float baseY = baseRadius * ca;
            float baseZ = baseRadius * sa;

            float offY = -sa * ribHalfWidth;
            float offZ = ca * ribHalfWidth;

            int a0 = AddIndexedVertex(mesh, ref v, tipX, offY, offZ);
            int a1 = AddIndexedVertex(mesh, ref v, tipX, -offY, -offZ);
            int b0 = AddIndexedVertex(mesh, ref v, baseX, baseY + offY, baseZ + offZ);
            int b1 = AddIndexedVertex(mesh, ref v, baseX, baseY - offY, baseZ - offZ);
            AddQuad(mesh, a0, b0, b1, a1);
        }
    }

    private static void AddRingBandYZ(BatchedPrimitives.MeshBuilder mesh, ref int vertexCounter, float x, float radius, float halfDepth, float thickness)
    {
        const int segments = 16;
        float innerR = MathF.Max(0.0001f, radius - thickness * 0.5f);
        float outerR = radius + thickness * 0.5f;

        for (int i = 0; i < segments; i++)
        {
            float t0 = MathF.PI * 2f * i / segments;
            float t1 = MathF.PI * 2f * (i + 1) / segments;

            float c0 = MathF.Cos(t0);
            float s0 = MathF.Sin(t0);
            float c1 = MathF.Cos(t1);
            float s1 = MathF.Sin(t1);

            int i0f = AddIndexedVertex(mesh, ref vertexCounter, x + halfDepth, innerR * c0, innerR * s0);
            int i1f = AddIndexedVertex(mesh, ref vertexCounter, x + halfDepth, innerR * c1, innerR * s1);
            int o1f = AddIndexedVertex(mesh, ref vertexCounter, x + halfDepth, outerR * c1, outerR * s1);
            int o0f = AddIndexedVertex(mesh, ref vertexCounter, x + halfDepth, outerR * c0, outerR * s0);

            int i0b = AddIndexedVertex(mesh, ref vertexCounter, x - halfDepth, innerR * c0, innerR * s0);
            int i1b = AddIndexedVertex(mesh, ref vertexCounter, x - halfDepth, innerR * c1, innerR * s1);
            int o1b = AddIndexedVertex(mesh, ref vertexCounter, x - halfDepth, outerR * c1, outerR * s1);
            int o0b = AddIndexedVertex(mesh, ref vertexCounter, x - halfDepth, outerR * c0, outerR * s0);

            AddQuad(mesh, i0f, i1f, o1f, o0f);
            AddQuad(mesh, i0b, o0b, o1b, i1b);
            AddQuad(mesh, i0f, i0b, i1b, i1f);
            AddQuad(mesh, o0f, o1f, o1b, o0b);
        }
    }

    private static void AddReelCylinder(BatchedPrimitives.MeshBuilder mesh, ref int vertexCounter,
        float centerX, float centerY, float centerZ, float radius, float halfThickness, int segments)
    {
        // Reel disc plane: XZ (vertical), thickness axis: Y
        int topCenter = AddIndexedVertex(mesh, ref vertexCounter, centerX, centerY + halfThickness, centerZ);
        int bottomCenter = AddIndexedVertex(mesh, ref vertexCounter, centerX, centerY - halfThickness, centerZ);

        int[] topRing = new int[segments];
        int[] bottomRing = new int[segments];
        for (int i = 0; i < segments; i++)
        {
            float a = MathF.PI * 2f * i / segments;
            float x = centerX + radius * MathF.Cos(a);
            float z = centerZ + radius * MathF.Sin(a);
            topRing[i] = AddIndexedVertex(mesh, ref vertexCounter, x, centerY + halfThickness, z);
            bottomRing[i] = AddIndexedVertex(mesh, ref vertexCounter, x, centerY - halfThickness, z);
        }

        for (int i = 0; i < segments; i++)
        {
            int next = (i + 1) % segments;
            mesh.AddTriangle(topCenter, topRing[i], topRing[next]);
            mesh.AddTriangle(bottomCenter, bottomRing[next], bottomRing[i]);
            AddQuad(mesh, topRing[i], topRing[next], bottomRing[next], bottomRing[i]);
        }
    }

    private static int AddIndexedVertex(BatchedPrimitives.MeshBuilder mesh, ref int vertexCounter, float x, float y, float z)
    {
        mesh.AddVertex(x, y, z);
        return vertexCounter++;
    }

    private static void AddQuad(BatchedPrimitives.MeshBuilder mesh, int a, int b, int c, int d)
    {
        mesh.AddTriangle(a, b, c);
        mesh.AddTriangle(a, c, d);
    }

    private static void AddWaveArc(BatchedPrimitives.MeshBuilder mesh, ref int vertexCounter, float centerX, float halfDepth, float innerR, float outerR, float angleOffset)
    {
        const int segments = 12;
        const float startA = -1.2f;
        const float endA = 1.2f;

        for (int i = 0; i < segments; i++)
        {
            float t0 = i / (float)segments;
            float t1 = (i + 1) / (float)segments;
            float a0 = angleOffset + startA + (endA - startA) * t0;
            float a1 = angleOffset + startA + (endA - startA) * t1;

            float ca0 = MathF.Cos(a0);
            float sa0 = MathF.Sin(a0);
            float ca1 = MathF.Cos(a1);
            float sa1 = MathF.Sin(a1);

            int i0f = AddIndexedVertex(mesh, ref vertexCounter, centerX + innerR * ca0, halfDepth, innerR * sa0);
            int i1f = AddIndexedVertex(mesh, ref vertexCounter, centerX + innerR * ca1, halfDepth, innerR * sa1);
            int o1f = AddIndexedVertex(mesh, ref vertexCounter, centerX + outerR * ca1, halfDepth, outerR * sa1);
            int o0f = AddIndexedVertex(mesh, ref vertexCounter, centerX + outerR * ca0, halfDepth, outerR * sa0);

            int i0b = AddIndexedVertex(mesh, ref vertexCounter, centerX + innerR * ca0, -halfDepth, innerR * sa0);
            int i1b = AddIndexedVertex(mesh, ref vertexCounter, centerX + innerR * ca1, -halfDepth, innerR * sa1);
            int o1b = AddIndexedVertex(mesh, ref vertexCounter, centerX + outerR * ca1, -halfDepth, outerR * sa1);
            int o0b = AddIndexedVertex(mesh, ref vertexCounter, centerX + outerR * ca0, -halfDepth, outerR * sa0);

            AddQuad(mesh, i0f, i1f, o1f, o0f);
            AddQuad(mesh, i0b, o0b, o1b, i1b);
            AddQuad(mesh, i0f, i0b, i1b, i1f);
            AddQuad(mesh, o0f, o1f, o1b, o0b);
        }
    }

    private static void RenderEmitterConeBurst(BatchedPrimitives.MeshBuilder mesh, float radius)
    {
        const int segments = 8;
        float baseRadius = radius * 0.55f;
        float baseZ = radius * 0.7f;
        float tipZ = -radius * 1.05f;

        mesh.AddVertex(0, 0, tipZ); // 0 cone tip
        for (int i = 0; i < segments; i++)
        {
            float theta = MathF.PI * 2f * i / segments;
            mesh.AddVertex(baseRadius * MathF.Cos(theta), baseRadius * MathF.Sin(theta), baseZ); // 1..segments
        }

        for (int i = 0; i < segments; i++)
        {
            int current = 1 + i;
            int next = 1 + ((i + 1) % segments);
            mesh.AddTriangle(0, next, current);
        }

        int baseCenter = 1 + segments;
        mesh.AddVertex(0, 0, baseZ); // base center
        for (int i = 0; i < segments; i++)
        {
            int current = 1 + i;
            int next = 1 + ((i + 1) % segments);
            mesh.AddTriangle(baseCenter, current, next);
        }

        // Small burst at tip (mini-octahedron)
        float burstCenterZ = tipZ - radius * 0.35f;
        float burstRadius = radius * 0.32f;
        int burstStart = baseCenter + 1;

        mesh.AddVertex(0, 0, burstCenterZ + burstRadius); // top
        mesh.AddVertex(burstRadius, 0, burstCenterZ); // +X
        mesh.AddVertex(0, burstRadius, burstCenterZ); // +Y
        mesh.AddVertex(-burstRadius, 0, burstCenterZ); // -X
        mesh.AddVertex(0, -burstRadius, burstCenterZ); // -Y
        mesh.AddVertex(0, 0, burstCenterZ - burstRadius); // bottom

        mesh.AddTriangle(burstStart, burstStart + 1, burstStart + 2);
        mesh.AddTriangle(burstStart, burstStart + 2, burstStart + 3);
        mesh.AddTriangle(burstStart, burstStart + 3, burstStart + 4);
        mesh.AddTriangle(burstStart, burstStart + 4, burstStart + 1);

        mesh.AddTriangle(burstStart + 5, burstStart + 2, burstStart + 1);
        mesh.AddTriangle(burstStart + 5, burstStart + 3, burstStart + 2);
        mesh.AddTriangle(burstStart + 5, burstStart + 4, burstStart + 3);
        mesh.AddTriangle(burstStart + 5, burstStart + 1, burstStart + 4);
    }

    private static void RenderOrb(BatchedPrimitives.MeshBuilder mesh, float radius)
    {
        const int stacks = 4;
        const int slices = 7;

        mesh.AddVertex(0, 0, radius);
        for (int stack = 1; stack < stacks; stack++)
        {
            float phi = MathF.PI * stack / stacks;
            float sinPhi = MathF.Sin(phi);
            float cosPhi = MathF.Cos(phi);
            for (int slice = 0; slice < slices; slice++)
            {
                float theta = MathF.PI * 2f * slice / slices;
                mesh.AddVertex(
                    radius * sinPhi * MathF.Cos(theta),
                    radius * sinPhi * MathF.Sin(theta),
                    radius * cosPhi);
            }
        }

        int bottomIndex = 1 + ((stacks - 1) * slices - slices);
        mesh.AddVertex(0, 0, -radius);
        int southPoleIndex = 1 + ((stacks - 1) * slices);
        for (int slice = 0; slice < slices; slice++)
        {
            int nextSlice = (slice + 1) % slices;
            mesh.AddTriangle(0, 1 + nextSlice, 1 + slice);
        }

        for (int stack = 0; stack < stacks - 2; stack++)
        {
            int rowStart = 1 + (stack * slices);
            int nextRowStart = rowStart + slices;
            for (int slice = 0; slice < slices; slice++)
            {
                int nextSlice = (slice + 1) % slices;
                int current = rowStart + slice;
                int currentNext = rowStart + nextSlice;
                int below = nextRowStart + slice;
                int belowNext = nextRowStart + nextSlice;
                mesh.AddTriangle(current, currentNext, below);
                mesh.AddTriangle(currentNext, belowNext, below);
            }
        }

        for (int slice = 0; slice < slices; slice++)
        {
            int nextSlice = (slice + 1) % slices;
            mesh.AddTriangle(southPoleIndex, bottomIndex + slice, bottomIndex + nextSlice);
        }
    }

    private static void RenderOctahedron(BatchedPrimitives.MeshBuilder mesh, float radius)
    {
        mesh.AddVertex(0, 0, radius);       // 0 top
        mesh.AddVertex(radius, 0, 0);       // 1 +X
        mesh.AddVertex(0, radius, 0);       // 2 +Y
        mesh.AddVertex(-radius, 0, 0);      // 3 -X
        mesh.AddVertex(0, -radius, 0);      // 4 -Y
        mesh.AddVertex(0, 0, -radius);      // 5 bottom

        mesh.AddTriangle(0, 1, 2);
        mesh.AddTriangle(0, 2, 3);
        mesh.AddTriangle(0, 3, 4);
        mesh.AddTriangle(0, 4, 1);

        mesh.AddTriangle(5, 2, 1);
        mesh.AddTriangle(5, 3, 2);
        mesh.AddTriangle(5, 4, 3);
        mesh.AddTriangle(5, 1, 4);
    }

    private static void RenderPyramid(BatchedPrimitives.MeshBuilder mesh, float radius)
    {
        float baseHalf = radius * 0.55f;
        float apexZ = radius;
        float baseZ = -radius * 0.55f;

        mesh.AddVertex(0, 0, apexZ);                // 0 apex
        mesh.AddVertex(-baseHalf, -baseHalf, baseZ);// 1
        mesh.AddVertex(baseHalf, -baseHalf, baseZ); // 2
        mesh.AddVertex(baseHalf, baseHalf, baseZ);  // 3
        mesh.AddVertex(-baseHalf, baseHalf, baseZ); // 4

        mesh.AddTriangle(0, 1, 2);
        mesh.AddTriangle(0, 2, 3);
        mesh.AddTriangle(0, 3, 4);
        mesh.AddTriangle(0, 4, 1);

        mesh.AddTriangle(1, 3, 2);
        mesh.AddTriangle(1, 4, 3);
    }
}

public class SFXDroppedAmmoProxy : ActorProxy
{
    public StaticMeshComponentProxy AmmoMesh;
    public SFXDroppedAmmoProxy(IActorEditorContext context, ExportEntry actorExport) : base(context, actorExport)
    {
        AddComponent(context.RenderContext, ref AmmoMesh);
    }
}

public class SFXDroppedGrenadeProxy : ActorProxy
{
    public StaticMeshComponentProxy GrenadeMesh;
    public SFXDroppedGrenadeProxy(IActorEditorContext context, ExportEntry actorExport) : base(context, actorExport)
    {
        AddComponent(context.RenderContext, ref GrenadeMesh);
    }
}