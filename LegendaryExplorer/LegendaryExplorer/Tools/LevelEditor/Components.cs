using LegendaryExplorer.Misc;
using LegendaryExplorer.Tools.LevelEditor.Scene3D;
using LegendaryExplorerCore.Helpers;
using LegendaryExplorerCore.Packages;
using LegendaryExplorerCore.Unreal;
using LegendaryExplorerCore.Unreal.Animation;
using LegendaryExplorerCore.Unreal.BinaryConverters;
using LegendaryExplorerCore.Unreal.ObjectInfo;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

//change this to switch between game and LEX shaders for LevelEditor
using VertexType = LegendaryExplorer.Tools.LevelEditor.Scene3D.LEVertex;

namespace LegendaryExplorer.Tools.LevelEditor;

public class PrimitiveComponentProxy : NotifyPropertyChangedBase, IDisposable
{
    public Matrix4x4 LocalToWorld;

    public PropertyCollection Properties;

    public ExportEntry Export { get; protected set; }

    public ActorProxy Actor;

    protected readonly MeshRenderContext RenderContext;

    protected virtual Matrix4x4 ParentToWorld => Actor.LocalToWorld;

    private Rotator rotation;
    private Vector3 translation;
    private Vector3 scale3D;
    private float scale;
    private bool absoluteTranslation;
    private bool absoluteRotation;
    private bool absoluteScale;
    public Rotator Rotation
    {
        get => rotation;
        set { if (SetProperty(ref rotation, value)) UpdateLocalToWorld(); }
    }
    public Vector3 Translation
    {
        get => translation;
        set { if (SetProperty(ref translation, value)) UpdateLocalToWorld(); }
    }
    public Vector3 Scale3D
    {
        get => scale3D;
        set { if (SetProperty(ref scale3D, value)) UpdateLocalToWorld(); }
    }
    public float Scale
    {
        get => scale;
        set { if (SetProperty(ref scale, value)) UpdateLocalToWorld(); }
    }
    public bool AbsoluteTranslation
    {
        get => absoluteTranslation;
        set { if (SetProperty(ref absoluteTranslation, value)) UpdateLocalToWorld(); }
    }
    public bool AbsoluteRotation
    {
        get => absoluteRotation;
        set { if (SetProperty(ref absoluteRotation, value)) UpdateLocalToWorld(); }
    }
    public bool AbsoluteScale
    {
        get => absoluteScale;
        set { if (SetProperty(ref absoluteScale, value)) UpdateLocalToWorld(); }
    }

    public bool IsVisible { get; set; } = true;

    public bool HiddenGame { get; }

    protected PrimitiveComponentProxy(MeshRenderContext context, ExportEntry componentExport, ActorProxy parent)
    {
        Actor = parent;
        Export = componentExport;
        RenderContext = context;
        Properties = componentExport.GetCondensedProperties(filterForeignObjectReferences: false);
        HiddenGame = Properties.GetProp<BoolProperty>("HiddenGame")?.Value ?? false;

        var rotationProp = Properties.GetProp<StructProperty>("Rotation");
        var translationProp = Properties.GetProp<StructProperty>("Translation");
        var scale3DProp = Properties.GetProp<StructProperty>("Scale3D");

        scale = Properties.GetProp<FloatProperty>("Scale")?.Value ?? 1;
        translation = translationProp != null ? CommonStructs.GetVector3(translationProp) : Vector3.Zero;
        scale3D = scale3DProp != null ? CommonStructs.GetVector3(scale3DProp) : Vector3.One;
        rotation = rotationProp != null ? CommonStructs.GetRotator(rotationProp) : new Rotator(0, 0, 0);

        absoluteTranslation = Properties.GetProp<BoolProperty>("AbsoluteTranslation")?.Value ?? false;
        absoluteRotation = Properties.GetProp<BoolProperty>("AbsoluteRotation")?.Value ?? false;
        absoluteScale = Properties.GetProp<BoolProperty>("AbsoluteScale")?.Value ?? false;

        UpdateSelfLocalToWorld();
    }

    public static PrimitiveComponentProxy Create(MeshRenderContext context, ExportEntry componentExport, ActorProxy parent)
    {
        string className = componentExport.ClassName;
        switch (className)
        {
            case "BrushComponent":
                return new BrushComponentProxy(context, componentExport, parent);
        }
        if (componentExport.IsA("LightComponent"))
        {
            return new LightComponentProxy(context, componentExport, parent);
        }
        if (GlobalUnrealObjectInfo.IsA(className, "StaticMeshComponent", componentExport.Game))
        {
            return new StaticMeshComponentProxy(context, componentExport, parent);
        }
        if (GlobalUnrealObjectInfo.IsA(className, "SkeletalMeshComponent", componentExport.Game))
        {
            return new SkeletalMeshComponentProxy(context, componentExport, parent);
        }

        return new PrimitiveComponentProxy(context, componentExport, parent);
    }

    public virtual void Render(MeshRenderContext context, RenderPass pass) { }

    public virtual void UpdateScene(MeshRenderContext context, float deltaTime) { }

    public virtual void CommitChanges() { }

    public virtual void MarkClean() { }

    private void UpdateSelfLocalToWorld()
    {
        var parentMatrix = ParentToWorld;
        if (absoluteTranslation)
        {
            parentMatrix.Translation = Vector3.Zero;
        }
        if (absoluteRotation || absoluteScale)
        {
            Vector3 x = parentMatrix.GetAxis(0);
            Vector3 y = parentMatrix.GetAxis(1);
            Vector3 z = parentMatrix.GetAxis(2);

            if (absoluteScale)
            {
                x = x.Normal();
                y = y.Normal();
                z = z.Normal();
            }
            if (absoluteRotation)
            {
                x = new Vector3(x.Length(), 0, 0);
                y = new Vector3(0, y.Length(), 0);
                z = new Vector3(0, 0, z.Length());
            }
            parentMatrix[0, 0] = x.X; parentMatrix[0, 1] = x.Y; parentMatrix[0, 2] = x.Z;
            parentMatrix[1, 0] = y.X; parentMatrix[1, 1] = y.Y; parentMatrix[1, 2] = y.Z;
            parentMatrix[2, 0] = z.X; parentMatrix[2, 1] = z.Y; parentMatrix[2, 2] = z.Z;
        }

        LocalToWorld = ActorUtils.ComposeLocalToWorld(translation, rotation, scale * scale3D) * parentMatrix;
    }

    public virtual void UpdateLocalToWorld()
    {
        UpdateSelfLocalToWorld();
    }

    public virtual BoxSphereBounds GetBounds()
    {
        return new BoxSphereBounds
        {
            Origin = LocalToWorld.Translation
        };
    }
    public virtual bool TestUIndexes(HashSet<int> uIndexes)
    {
        if (uIndexes.Contains(Export.UIndex))
        {
            return true;
        }
        return false;
    }

    #region IDisposeable
    private bool isDisposed;
    protected virtual void Dispose(bool disposing)
    {
        if (!isDisposed)
        {
            if (disposing)
            {
                // TODO: dispose managed state (managed objects)
            }

            // TODO: free unmanaged resources (unmanaged objects) and override finalizer
            // TODO: set large fields to null
            isDisposed = true;
        }
    }

    // // TODO: override finalizer only if 'Dispose(bool disposing)' has code to free unmanaged resources
    // ~PrimitiveComponentProxy()
    // {
    //     // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
    //     Dispose(disposing: false);
    // }

    public void Dispose()
    {
        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
    #endregion
}

public abstract class MeshComponentProxy : PrimitiveComponentProxy
{
    public bool IsVolumetric;
    public string MeshIFP { get; protected set; }
    protected ModelPreview<VertexType> Mesh;
    public int LOD;
    public List<IEntry> MaterialOverrides = [];

    private List<(int Start, int End)> VolumetricOutlineEdges;

    protected void ClassifyVolumetricMesh()
    {
        IsVolumetric = VolumetricMeshClassifier.IsVolumetric(MeshIFP, Mesh?.Materials.Keys);
    }

    public void DrawVolumetricTint(LevelEditorRenderContext context)
    {
        if (!IsVisible || !IsVolumetric || Mesh is null || LOD >= Mesh.LODs.Count) return;
        context.RenderVolumetricTint(Mesh.LODs[LOD].Mesh);
    }

    public void DrawVolumetricEditingOutline(LevelEditorRenderContext context)
    {
        if (!IsVisible || !IsVolumetric || Mesh is null || LOD >= Mesh.LODs.Count) return;
        var mesh = Mesh.LODs[LOD].Mesh;
        if (VolumetricOutlineEdges is null)
        {
            var edges = new HashSet<(int Start, int End)>();
            foreach (var triangle in mesh.Triangles)
            {
                AddEdge((int)triangle.Vertex1, (int)triangle.Vertex2);
                AddEdge((int)triangle.Vertex2, (int)triangle.Vertex3);
                AddEdge((int)triangle.Vertex3, (int)triangle.Vertex1);
            }
            VolumetricOutlineEdges = edges.ToList();

            void AddEdge(int start, int end)
            {
                if (start < mesh.Vertices.Count && end < mesh.Vertices.Count)
                {
                    edges.Add(start < end ? (start, end) : (end, start));
                }
            }
        }
        Vector4 outlineColor = new(0f, 1f, 1f, 1f);
        foreach (var (start, end) in VolumetricOutlineEdges)
        {
            context.Primitives.AddLine(Vector3.Transform(mesh.Vertices[start].Position, mesh.LocalToWorld),
                Vector3.Transform(mesh.Vertices[end].Position, mesh.LocalToWorld), outlineColor, Actor.HitID);
        }
        var bounds = mesh.TransformedBounds;
        Vector3 min = bounds.Origin - bounds.BoxExtent;
        Vector3 max = bounds.Origin + bounds.BoxExtent;
        Vector4 boundsColor = new(1f, 0.85f, 0f, 1f);
        for (int axis = 0; axis < 3; axis++)
        {
            for (int corner = 0; corner < 4; corner++)
            {
                Vector3 start = min;
                start[(axis + 1) % 3] = (corner & 1) == 0 ? min[(axis + 1) % 3] : max[(axis + 1) % 3];
                start[(axis + 2) % 3] = (corner & 2) == 0 ? min[(axis + 2) % 3] : max[(axis + 2) % 3];
                Vector3 end = start;
                end[axis] = max[axis];
                Vector3 midpoint = (start + end) * 0.5f;
                float thickness = context.Camera.IsOrthographic
                    ? context.Camera.OrthoWidth / Math.Max(context.Width, 1) * 2f
                    : Math.Max(Vector3.Distance(midpoint, context.Camera.Position) * 0.002f, 0.5f);
                var beam = context.Primitives.BuildMesh(boundsColor, Actor.HitID, Matrix4x4.Identity);
                Vector3 offsetA = Vector3.Zero;
                Vector3 offsetB = Vector3.Zero;
                offsetA[(axis + 1) % 3] = thickness;
                offsetB[(axis + 2) % 3] = thickness;
                beam.AddVertex(start - offsetA - offsetB);
                beam.AddVertex(start + offsetA - offsetB);
                beam.AddVertex(start + offsetA + offsetB);
                beam.AddVertex(start - offsetA + offsetB);
                beam.AddVertex(end - offsetA - offsetB);
                beam.AddVertex(end + offsetA - offsetB);
                beam.AddVertex(end + offsetA + offsetB);
                beam.AddVertex(end - offsetA + offsetB);
                for (int face = 0; face < 4; face++)
                {
                    int next = (face + 1) % 4;
                    beam.AddTriangle(face, next, next + 4);
                    beam.AddTriangle(face, next + 4, face + 4);
                }
            }
        }
    }

    private LightEnvironmentPrimitive LightEnvironmentPrimitive;
    private DynamicLightEnvironment LightEnvironment;

    protected bool IsInScene() => !HiddenGame && Actor is not { IsHidden: true };

    protected bool IsShown() => IsVisible && (Actor is null || RenderContext.IsActorVisible(Actor));

    protected void JoinLightEnvironment(MeshRenderContext context, DynamicLightEnvironment lightEnvironment, LightingChannels lightingChannels, bool castsShadow,
        MeshStaticLighting staticLighting = null)
    {
        if (lightEnvironment is null || Mesh is null || Mesh.LODs.Count <= LOD)
        {
            return;
        }
        ModelPreviewLOD<VertexType> lod = Mesh.LODs[LOD];
        int lodIndex = LOD;
        LightEnvironmentPrimitive = new LightEnvironmentPrimitive
        {
            GetBounds = () => lod.Mesh.TransformedBounds,
            GetLocalToWorld = () => lod.Mesh.LocalToWorld,
            DrawShadowCaster = castsShadow ? ctx => Mesh?.DrawShadowCaster(ctx, lodIndex) : null,
            IsInScene = IsInScene,
            IsShown = IsShown,
            LightingChannels = lightingChannels,
        };
        LightEnvironment = context.AddLightEnvironmentPrimitive(lightEnvironment, LightEnvironmentPrimitive);
        lod.LightEnvironment = LightEnvironment;
        if (staticLighting is not null)
        {
            staticLighting.LightEnvironment = LightEnvironment;
        }
    }

    protected MeshComponentProxy(MeshRenderContext context, ExportEntry componentExport, ActorProxy parent) : base(context, componentExport, parent)
    {
        if (Properties.GetProp<ArrayProperty<ObjectProperty>>("Materials") is { } mats)
        {
            MaterialOverrides.AddRange(mats.Select(x => x.Value != 0 ? x.ResolveToEntry(Export.FileRef) : null));
        }
    }

    public override BoxSphereBounds GetBounds()
    {
        if (Mesh is null or { LODs.Count: 0 })
        {
            return base.GetBounds();
        }
        return Mesh.LODs[LOD].Mesh.TransformedBounds;
    }

    protected override void Dispose(bool disposing)
    {
        if (LightEnvironmentPrimitive is not null)
        {
            RenderContext.RemoveLightEnvironmentPrimitive(LightEnvironment, LightEnvironmentPrimitive);
            LightEnvironmentPrimitive = null;
        }
        Mesh?.Dispose();
        base.Dispose(disposing);
    }
}

public class StaticMeshComponentProxy : MeshComponentProxy
{
    private readonly Mesh<WorldVertex> CollisionMesh;
    private readonly MeshStaticLighting StaticLighting;

    public StaticMeshComponentProxy(MeshRenderContext context, ExportEntry componentExport, ActorProxy parent) : base(context, componentExport, parent)
    {
        if (Properties.GetProp<ObjectProperty>("StaticMesh")?.ResolveToExport(Export.FileRef, context.PackageCache) is ExportEntry meshExport)
        {
            StaticMesh stm = meshExport.GetBinaryData<StaticMesh>();
            if (stm.LODModels.Length > LOD)
            {
                stm.SetMaterials(MaterialOverrides, true);
                MaterialOverrides.Clear();
                MeshStaticLighting staticLighting = MeshStaticLighting.Create(context, Export, stm, LOD);
                Mesh = new ModelPreview<VertexType>(context, stm, LOD, staticLighting);
                MeshIFP = meshExport.InstancedFullPath;
                if (staticLighting is not null)
                {
                    StaticLighting = staticLighting;
                    staticLighting.IsInScene = IsInScene;
                    staticLighting.IsShown = IsShown;
                    if (MeshRenderContext.SupportsLightEnvironments(Export.Game))
                    {
                        staticLighting.GeometryMesh = context.GetLevelGeometryMesh($"{meshExport.FileRef.FilePath}|{meshExport.UIndex}",
                            () => ReadLineCheckTriangles(stm));
                    }
                    JoinLightEnvironment(context, staticLighting.LightEnvironment, staticLighting.Channels, staticLighting.CastsDynamicShadow, staticLighting);
                }
                ClassifyVolumetricMesh();
            }
            CollisionMesh = context.GetMeshFromAggGeom(stm.GetCollisionMeshProperty(Export.FileRef));
            UpdateSelfLocalToWorld();
        }
    }

    private static (Vector3[] Positions, int[] Indices) ReadLineCheckTriangles(StaticMesh stm)
    {
        var kDOPTriangles = stm.kDOPTreeME3UDKLE?.Triangles ?? [];
        if (stm.LODModels.Length == 0 || kDOPTriangles.Length == 0)
        {
            return ([], []);
        }
        var vertexData = stm.LODModels[0].PositionVertexBuffer.VertexData;
        var lodPositions = new Vector3[vertexData.Length];
        for (int i = 0; i < vertexData.Length; i++)
        {
            lodPositions[i] = new Vector3(vertexData[i].X, vertexData[i].Y, vertexData[i].Z);
        }
        var triangleIndices = new int[kDOPTriangles.Length * 3];
        for (int i = 0; i < kDOPTriangles.Length; i++)
        {
            triangleIndices[i * 3] = kDOPTriangles[i].Vertex1;
            triangleIndices[i * 3 + 1] = kDOPTriangles[i].Vertex2;
            triangleIndices[i * 3 + 2] = kDOPTriangles[i].Vertex3;
        }
        return (lodPositions, triangleIndices);
    }

    public override void Render(MeshRenderContext context, RenderPass pass)
    {
        if (!IsVisible) return;
        if (pass is RenderPass.Collision)
        {
            if (CollisionMesh is not null)
            {
                context.RenderMeshAsWireframe(CollisionMesh);
            }
            return;
        }
        Mesh?.Render(pass, context, LOD);
    }

    public override void UpdateLocalToWorld()
    {
        base.UpdateLocalToWorld();
        UpdateSelfLocalToWorld();
    }

    private void UpdateSelfLocalToWorld()
    {
        if (CollisionMesh is not null)
        {
            CollisionMesh.LocalToWorld = LocalToWorld;
        }
        if (Mesh is not null)
        {
            Mesh.UpdateLocalToWorld(LocalToWorld);
            if (StaticLighting is { GeometryMesh: not null, CanBlockVisibilityTraces: true })
            {
                RenderContext.InvalidateLevelGeometry();
            }
        }
    }

    protected override void Dispose(bool disposing)
    {
        CollisionMesh?.Dispose();
        base.Dispose(disposing);
    }
}

public class SkeletalMeshComponentProxy : MeshComponentProxy
{
    SkinnedMeshRenderer skinnedMeshRenderer;
    AnimSequencePlayer animPlayer;
    public bool ForceWireframeRender { get; set; }
    public ExportEntry SkeletalMeshExport { get; private set; }
    public SkeletalMesh SkeletalMeshBinary { get; private set; }
    public MeshBone[] RefSkeleton { get; private set; }

    private readonly PrimitiveComponentProxy TransformParent;

    protected override Matrix4x4 ParentToWorld => TransformParent?.LocalToWorld ?? base.ParentToWorld;

    public SkeletalMeshComponentProxy(MeshRenderContext context, ExportEntry componentExport, ActorProxy parent) : base(context, componentExport, parent)
    {
        bool bTransformFromAnimParent = Properties.GetProp<BoolProperty>("bTransformFromAnimParent")?.Value ?? true;
        if (bTransformFromAnimParent
            && Properties.GetProp<ObjectProperty>("ParentAnimComponent")?.ResolveToEntry(Export.FileRef) is ExportEntry parentAnimExport
            && parent.Components.FirstOrDefault(cmp => cmp.Export == parentAnimExport) is { } parentAnimComponent)
        {
            TransformParent = parentAnimComponent;
            base.UpdateLocalToWorld();
        }
        if (Properties.GetProp<ObjectProperty>("SkeletalMesh")?.ResolveToExport(Export.FileRef, context.PackageCache) is ExportEntry meshExport)
        {
            SkeletalMeshExport = meshExport;
            SkeletalMesh skm = meshExport.GetBinaryData<SkeletalMesh>();
            SkeletalMeshBinary = skm;
            RefSkeleton = skm.RefSkeleton;
            PropertyCollection condensedProps = null;
            DynamicLightEnvironment lightEnvironment = null;
            if (skm.LODModels.Length > LOD)
            {
                skm.SetMaterials(MaterialOverrides, true);
                MaterialOverrides.Clear();
                MeshStaticLighting staticLighting = null;
                if (Export.Game.IsLEGame())
                {
                    condensedProps = Export.GetCondensedProperties(context.PackageCache, resolveImports: true, mergeStructs: true);
                    if (!context.ResolveLightEnvironment(Export, condensedProps, parent?.Export, out lightEnvironment))
                    {
                        staticLighting = MeshStaticLighting.CreateDynamic(context, Export, condensedProps);
                    }
                }
                Mesh = new ModelPreview<VertexType>(context, skm, staticLighting, LOD);
                if (staticLighting is not null)
                {
                    staticLighting.IsInScene = IsInScene;
                    staticLighting.IsShown = IsShown;
                }
                MeshIFP = meshExport.InstancedFullPath;
                ClassifyVolumetricMesh();
                skinnedMeshRenderer = new SkinnedMeshRenderer();
                skinnedMeshRenderer.BuildFromSkeletalMesh(meshExport.FileRef.Game, skm.LODModels[LOD]);
                animPlayer = new AnimSequencePlayer(skm);
            }
            UpdateSelfLocalToWorld();
            if (Mesh is not null && lightEnvironment is not null)
            {
                bool castsShadow = condensedProps.GetProp<BoolProperty>("CastShadow") is not { Value: false }
                                   && condensedProps.GetProp<BoolProperty>("bCastDynamicShadow") is not { Value: false };
                JoinLightEnvironment(context, lightEnvironment, LightingChannels.FromProperty(condensedProps.GetProp<StructProperty>("LightingChannels"), default,
                    isInitialized: false, LightingChannels.DynamicPrimitiveDefault), castsShadow);
            }
        }
    }

    public override void UpdateScene(MeshRenderContext context, float deltaTime)
    {
        if (Mesh is not null && skinnedMeshRenderer.NeedsUpdate)
        {
            skinnedMeshRenderer.UpdateSkinning(context.ImmediateContext, Mesh.LODs[LOD].Mesh, animPlayer);
        }
    }

    public override void Render(MeshRenderContext context, RenderPass pass)
    {
        if (!IsVisible) return;
        if (ForceWireframeRender)
        {
            if (pass is RenderPass.Base && Mesh is { LODs.Count: > 0 })
            {
                context.RenderMeshAsWireframe(Mesh.LODs[LOD].Mesh);
            }
            return;
        }
        Mesh?.Render(pass, context, LOD);
    }

    public void SetAnimation(AnimSequence animSequence, float pos)
    {
        if (animPlayer is null) return;
        if (animSequence is null)
        {
            if (animPlayer.HasAnimation)
            {
                //cancel animation, reset to ref pose
                animPlayer.SetAnimation(null);
                skinnedMeshRenderer.NeedsUpdate = true;
            }
            return;
        }
        if (animSequence.Name != animPlayer.AnimName)
        {
            animPlayer.SetAnimation(animSequence);
        }
        animPlayer.SetCurrentTime(pos);
        skinnedMeshRenderer.NeedsUpdate = true;
    }

    public void ApplyMorph(LegendaryExplorerCore.Unreal.Classes.BonePosition[] bonePositions, Vector3[][] morphLods)
    {
        if (Mesh is null) return;
        if (morphLods?.Length > LOD)
        {
            skinnedMeshRenderer.UpdateVertexPositions(morphLods[LOD]);
            skinnedMeshRenderer.NeedsUpdate = true;
        }
        if (bonePositions is not null && animPlayer is not null)
        {
            animPlayer.ApplyBonePositions(bonePositions);
            skinnedMeshRenderer.NeedsUpdate = true;
        }
    }

    public override void UpdateLocalToWorld()
    {
        base.UpdateLocalToWorld();
        UpdateSelfLocalToWorld();
    }

    private void UpdateSelfLocalToWorld()
    {
        Mesh?.UpdateLocalToWorld(LocalToWorld);
    }
}

public class BrushComponentProxy : PrimitiveComponentProxy
{
    private readonly Mesh<WorldVertex> Brush;

    public BrushComponentProxy(MeshRenderContext context, ExportEntry componentExport, ActorProxy parent) : base(context, componentExport, parent)
    {
        Brush = context.GetMeshFromAggGeom(Properties.GetProp<StructProperty>("BrushAggGeom"));
        UpdateSelfLocalToWorld();
    }

    public override void Render(MeshRenderContext context, RenderPass pass)
    {
        if (!IsVisible) return;
        if (Brush is not null && pass is RenderPass.Base or RenderPass.Collision)
        {
            context.RenderMeshAsWireframe(Brush);
        }
    }

    public override void UpdateLocalToWorld()
    {
        base.UpdateLocalToWorld();
        UpdateSelfLocalToWorld();
    }

    private void UpdateSelfLocalToWorld()
    {
        if (Brush is not null)
        {
            Brush.LocalToWorld = LocalToWorld;
        }
    }

    protected override void Dispose(bool disposing)
    {
        Brush?.Dispose();
        base.Dispose(disposing);
    }
}

public class LightComponentProxy : PrimitiveComponentProxy
{
    private enum LightRenderShape
    {
        Point,
        Spot,
        Directional
    }

    private readonly LightRenderShape renderShape;
    private readonly float innerConeAngle;
    private readonly float outerConeAngle;
    private float radius;
    private float sourceRadius;
    private float brightness;
    private System.Windows.Media.Color lightColor;
    private bool lightingChannelStatic = true;
    private bool lightingChannelDynamic = true;
    private bool lightingChannelCompositeDynamic = true;

    private (Matrix4x4 Transform, float Radius, float SourceRadius, float Brightness,
        System.Windows.Media.Color Color, bool Static, bool Dynamic, bool Composite)? previewState;
    private SceneLight previewLight;
    private SceneLight originalLight;
    private (Matrix4x4 Transform, float Radius, float SourceRadius, float Brightness,
        System.Windows.Media.Color Color, bool Static, bool Dynamic, bool Composite) initialState;

    public SceneLight GetPreviewLight(PackageCache cache, SceneLight baseline)
    {
        originalLight ??= baseline;
        var state = (LocalToWorld, Radius, SourceRadius, Brightness, LightColor,
            LightingChannelStatic, LightingChannelDynamic, LightingChannelCompositeDynamic);
        if (state == initialState) return originalLight;
        if (previewState == state) return previewLight;
        var props = Export.GetCondensedProperties(cache, resolveImports: true, mergeStructs: true).DeepClone();
        if (Brightness != initialState.Brightness) props.AddOrReplaceProp(new FloatProperty(Brightness, "Brightness"));
        if (Radius != initialState.Radius) props.AddOrReplaceProp(new FloatProperty(Radius, "Radius"));
        if (SourceRadius != initialState.SourceRadius) props.AddOrReplaceProp(new FloatProperty(SourceRadius, "SourceRadius"));
        if (LightColor != initialState.Color)
            props.AddOrReplaceProp(CommonStructs.ColorProp(System.Drawing.Color.FromArgb(LightColor.A, LightColor.R, LightColor.G, LightColor.B), "LightColor"));
        if (LightingChannelStatic != initialState.Static || LightingChannelDynamic != initialState.Dynamic
            || LightingChannelCompositeDynamic != initialState.Composite)
        {
            var channels = props.GetProp<StructProperty>("LightingChannels") ?? new StructProperty("LightingChannelContainer", false,
                new BoolProperty(true, "bInitialized")) { Name = "LightingChannels" };
            channels.Properties.AddOrReplaceProp(new BoolProperty(true, "bInitialized"));
            if (LightingChannelStatic != initialState.Static) channels.Properties.AddOrReplaceProp(new BoolProperty(LightingChannelStatic, "Static"));
            if (LightingChannelDynamic != initialState.Dynamic) channels.Properties.AddOrReplaceProp(new BoolProperty(LightingChannelDynamic, "Dynamic"));
            if (LightingChannelCompositeDynamic != initialState.Composite) channels.Properties.AddOrReplaceProp(new BoolProperty(LightingChannelCompositeDynamic, "CompositeDynamic"));
            props.AddOrReplaceProp(channels);
        }
        bool moved = LocalToWorld != initialState.Transform;
        if (moved)
        {
            props.RemoveNamedProperty("CachedParentToWorld");
            props.AddOrReplaceProp(CommonStructs.Vector3Prop(Vector3.Zero, "Translation"));
        }
        string ownerClass = Actor is CollectionActorComponentProxy collection ? collection.CollectionActorExport.ClassName : Actor.Export.ClassName;
        previewLight = SceneLight.Create(Export, ownerClass, cache,
            moved ? LocalToWorld : Actor.LocalToWorld, originalLight.VisibilityExport, props);
        previewState = state;
        return previewLight;
    }

    public float Radius
    {
        get => radius;
        set
        {
            if (Actor?.IsReadOnly ?? false) return;
            if (SetProperty(ref radius, Math.Max(0, value)))
            {
                Actor?.MarkDirty();
            }
        }
    }

    public float SourceRadius
    {
        get => sourceRadius;
        set
        {
            if (Actor?.IsReadOnly ?? false) return;
            if (SetProperty(ref sourceRadius, Math.Max(0, value)))
            {
                Actor?.MarkDirty();
            }
        }
    }

    public float Brightness
    {
        get => brightness;
        set
        {
            if (Actor?.IsReadOnly ?? false) return;
            if (SetProperty(ref brightness, Math.Max(0, value)))
            {
                Actor?.MarkDirty();
            }
        }
    }

    public System.Windows.Media.Color LightColor
    {
        get => lightColor;
        set
        {
            if (Actor?.IsReadOnly ?? false) return;
            if (SetProperty(ref lightColor, value))
            {
                Actor?.MarkDirty();
            }
        }
    }

    public bool LightingChannelStatic
    {
        get => lightingChannelStatic;
        set
        {
            if (Actor?.IsReadOnly ?? false) return;
            if (SetProperty(ref lightingChannelStatic, value))
            {
                Actor?.MarkDirty();
            }
        }
    }

    public bool LightingChannelDynamic
    {
        get => lightingChannelDynamic;
        set
        {
            if (Actor?.IsReadOnly ?? false) return;
            if (SetProperty(ref lightingChannelDynamic, value))
            {
                Actor?.MarkDirty();
            }
        }
    }

    public bool LightingChannelCompositeDynamic
    {
        get => lightingChannelCompositeDynamic;
        set
        {
            if (Actor?.IsReadOnly ?? false) return;
            if (SetProperty(ref lightingChannelCompositeDynamic, value))
            {
                Actor?.MarkDirty();
            }
        }
    }

    public LightComponentProxy(MeshRenderContext context, ExportEntry componentExport, ActorProxy parent) : base(context, componentExport, parent)
    {
        string className = componentExport.ClassName;
        renderShape = GlobalUnrealObjectInfo.IsA(className, "SpotLightComponent", componentExport.Game)
            ? LightRenderShape.Spot
            : GlobalUnrealObjectInfo.IsA(className, "DirectionalLightComponent", componentExport.Game)
                ? LightRenderShape.Directional
                : LightRenderShape.Point;

        radius = Properties.GetProp<FloatProperty>("Radius")?.Value ?? 1024f;
        innerConeAngle = Properties.GetProp<FloatProperty>("InnerConeAngle")?.Value ?? 0f;
        outerConeAngle = Properties.GetProp<FloatProperty>("OuterConeAngle")?.Value ?? 44f;
        sourceRadius = Properties.GetProp<FloatProperty>("SourceRadius")?.Value ?? 32f;
        brightness = Properties.GetProp<FloatProperty>("Brightness")?.Value ?? 1f;
        lightColor = GetInitialLightColor();
        if (Properties.GetProp<StructProperty>("LightingChannels") is { } lightingChannels)
        {
            lightingChannelStatic = lightingChannels.GetProp<BoolProperty>("Static")?.Value ?? true;
            lightingChannelDynamic = lightingChannels.GetProp<BoolProperty>("Dynamic")?.Value ?? true;
            lightingChannelCompositeDynamic = lightingChannels.GetProp<BoolProperty>("CompositeDynamic")?.Value ?? true;
        }
        initialState = (LocalToWorld, Radius, SourceRadius, Brightness, LightColor,
            LightingChannelStatic, LightingChannelDynamic, LightingChannelCompositeDynamic);
    }

    public override void Render(MeshRenderContext context, RenderPass pass)
    {
        if (!IsVisible || pass is not RenderPass.Base)
        {
            return;
        }

        LevelEditorRenderContext levelContext = (LevelEditorRenderContext)context;
        switch (renderShape)
        {
            case LightRenderShape.Directional:
                RenderDirectionalLight(levelContext);
                break;
            case LightRenderShape.Spot:
                RenderSpotLight(levelContext);
                break;
            default:
                RenderPointLight(levelContext);
                break;
        }
    }

    public override BoxSphereBounds GetBounds()
    {
        float extent = renderShape is LightRenderShape.Directional ? 56f : 40f;
        return new BoxSphereBounds
        {
            Origin = LocalToWorld.Translation,
            BoxExtent = new Vector3(extent),
            SphereRadius = extent
        };
    }

    public override void CommitChanges()
    {
        var props = Properties;
        if (props.ContainsNamedProp("Brightness") || Brightness != 1f)
        {
            props.AddOrReplaceProp(new FloatProperty(Brightness, "Brightness"));
        }
        if (props.ContainsNamedProp("Radius") || Radius != 1024f)
        {
            props.AddOrReplaceProp(new FloatProperty(Radius, "Radius"));
        }
        if (props.ContainsNamedProp("SourceRadius") || SourceRadius != 32f)
        {
            props.AddOrReplaceProp(new FloatProperty(SourceRadius, "SourceRadius"));
        }

        props.AddOrReplaceProp(CommonStructs.ColorProp(System.Drawing.Color.FromArgb(LightColor.A, LightColor.R, LightColor.G, LightColor.B), "LightColor"));

        var lightingChannels = props.GetProp<StructProperty>("LightingChannels") ?? new StructProperty("LightingChannelContainer", false,
            new BoolProperty(true, "bIsInitialized"))
        {
            Name = "LightingChannels"
        };
        lightingChannels.Properties.AddOrReplaceProp(new BoolProperty(true, "bIsInitialized"));
        lightingChannels.Properties.AddOrReplaceProp(new BoolProperty(LightingChannelStatic, "Static"));
        lightingChannels.Properties.AddOrReplaceProp(new BoolProperty(LightingChannelDynamic, "Dynamic"));
        lightingChannels.Properties.AddOrReplaceProp(new BoolProperty(LightingChannelCompositeDynamic, "CompositeDynamic"));
        props.AddOrReplaceProp(lightingChannels);

        Export.WriteProperties(props);
    }

    private System.Windows.Media.Color GetInitialLightColor()
    {
        if (Properties.GetProp<StructProperty>("LightColor") is { } lightColorProp)
        {
            var lightColor = CommonStructs.GetColor(lightColorProp);
            return System.Windows.Media.Color.FromArgb(lightColor.A, lightColor.R, lightColor.G, lightColor.B);
        }

        return renderShape is LightRenderShape.Directional
            ? System.Windows.Media.Color.FromRgb(140, 204, 255)
            : System.Windows.Media.Color.FromRgb(255, 242, 140);
    }

    private Vector4 GetDisplayColorVector() => new(LightColor.R / 255f, LightColor.G / 255f, LightColor.B / 255f, 1f);

    private void RenderPointLight(LevelEditorRenderContext context)
    {
        RenderOrb(context);
    }

    private void RenderSpotLight(LevelEditorRenderContext context)
    {
        RenderOrb(context);
    }

    private void RenderDirectionalLight(LevelEditorRenderContext context)
    {
        RenderOrb(context);
    }

    private float RenderOrb(LevelEditorRenderContext context)
    {
        Vector3 origin = LocalToWorld.Translation;
        float iconRadius = GetIconRadius(context, origin);
        AddOrb(context, iconRadius, GetDisplayColorVector());
        return iconRadius;
    }

    private void AddOrb(LevelEditorRenderContext context, float orbRadius, Vector4 orbColor)
    {
        var mesh = context.Primitives.BuildMesh(orbColor, Actor.HitID, Matrix4x4.CreateTranslation(LocalToWorld.Translation));
        const int stacks = 5;
        const int slices = 8;

        mesh.AddVertex(0, 0, orbRadius);
        for (int stack = 1; stack < stacks; stack++)
        {
            float phi = MathF.PI * stack / stacks;
            float sinPhi = MathF.Sin(phi);
            float cosPhi = MathF.Cos(phi);
            for (int slice = 0; slice < slices; slice++)
            {
                float theta = MathF.PI * 2f * slice / slices;
                mesh.AddVertex(
                    orbRadius * sinPhi * MathF.Cos(theta),
                    orbRadius * sinPhi * MathF.Sin(theta),
                    orbRadius * cosPhi);
            }
        }
        int bottomIndex = 1 + ((stacks - 1) * slices - slices);
        mesh.AddVertex(0, 0, -orbRadius);
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

    private float GetIconRadius(LevelEditorRenderContext context, Vector3 origin)
    {
        float brightnessScale = Math.Clamp(0.9f + (MathF.Sqrt(MathF.Max(brightness, 0.05f)) * 0.18f), 0.85f, 1.35f);
        float proximityScale = 1f;
        if (!context.Camera.IsOrthographic)
        {
            float distance = Vector3.Distance(context.Camera.Position, origin);
            proximityScale += 0.4f * Math.Clamp(1f - (distance / 3000f), 0f, 1f);
        }

        float pixelRadius = 8f * brightnessScale * proximityScale;
        float worldUnitsPerPixel = context.Camera.IsOrthographic
            ? context.Camera.OrthoWidth / context.Width
            : context.WorldToScreen(origin).W / (context.Width * context.Camera.ProjectionMatrix[0, 0]);
        return MathF.Max(worldUnitsPerPixel * pixelRadius, 7f);
    }
}