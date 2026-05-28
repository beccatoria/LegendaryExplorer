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
    public OpenLevelFile OwningFile { get; set; }

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
        "AmbientSound",
        "WwiseMicPosOrient",
        "CameraActor",
        "DecalActor",
        "MaterialInstanceActor",
        "LensFlareSource",
        "BioStage"
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    public static bool CanCreate(ExportEntry actorExport)
    {
        return actorExport.IsA(SupportedClasses) || actorExport.IsA("Light");
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
        if (GlobalUnrealObjectInfo.IsA(className, "AmbientSound", actorExport.Game))
        {
            return new IconActorProxy(context, actorExport, IconActorCategory.AmbientSound);
        }
        if (GlobalUnrealObjectInfo.IsA(className, "WwiseMicPosOrient", actorExport.Game))
        {
            return new IconActorProxy(context, actorExport, IconActorCategory.AmbientSound);
        }
        if (GlobalUnrealObjectInfo.IsA(className, "CameraActor", actorExport.Game))
        {
            return new IconActorProxy(context, actorExport, IconActorCategory.Camera);
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
            return new IconActorProxy(context, actorExport, IconActorCategory.Decal);
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

public class BioStageActorProxy : ActorProxy
{
    public SkeletalMeshComponentProxy MeshComponent;
    public StaticMeshComponentProxy StaticMeshComponent;
    public BrushComponentProxy BrushComponent;
    private List<PrimitiveComponentProxy> StageComponents = [];

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
    }

    public override int HitPriority => IHitProxy.WireFramePriority;

    public override void Render(LevelEditorRenderContext context, RenderPass pass)
    {
        base.Render(context, pass);

        if (pass is not (RenderPass.Base or RenderPass.Hair))
        {
            return;
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

    protected CollectionActorComponentProxy(IActorEditorContext context, StaticCollectionActor collectionActor, ExportEntry componentActor, int index) : base(componentActor)
    {
        Editor = context;
        CollectionActorExport = collectionActor.Export;

        LocalToWorld = collectionActor.LocalToWorldTransforms[index];
        (location, drawScale3D, rotation) = collectionActor.GetDecomposedTransformationForIndex(index);
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
    AmbientSound,
    Camera,
    Decal,
    LensFlareLight
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
                IsAmbientSound = true;
                break;
            case IconActorCategory.Camera:
                IsCameraActor = true;
                IsCinematicActor = true;
                break;
            case IconActorCategory.Decal:
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
            IconActorCategory.AmbientSound => new Vector4(0.25f, 0.88f, 1.0f, 1f),
            IconActorCategory.Camera => new Vector4(1.0f, 0.92f, 0.18f, 1f),
            IconActorCategory.Decal => new Vector4(0.92f, 0.38f, 1.0f, 1f),
            IconActorCategory.LensFlareLight => new Vector4(1.0f, 0.95f, 0.35f, 1f),
            _ => Vector4.One
        };

        float categoryScale = IconCategory switch
        {
            IconActorCategory.StartPoint => 1.18f,
            IconActorCategory.TargetPoint => 1.12f,
            IconActorCategory.Camera => 1.1f,
            _ => 1f
        };

        float radius = 10.5f * categoryScale;
        if (!context.Camera.IsOrthographic)
        {
            float distance = Vector3.Distance(LocalToWorld.Translation, context.Camera.Position);
            radius = Math.Clamp((7f + (distance * 0.0042f)) * categoryScale, 7f, 24f);
        }

        var mesh = context.Primitives.BuildMesh(color, HitID, Matrix4x4.CreateTranslation(LocalToWorld.Translation));
        switch (IconCategory)
        {
            case IconActorCategory.AmbientSound:
                RenderOctahedron(mesh, radius);
                break;
            case IconActorCategory.Camera:
                RenderPyramid(mesh, radius);
                break;
            default:
                RenderOrb(mesh, radius);
                break;
        }
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