using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using LegendaryExplorerCore.Packages;

namespace LegendaryExplorer.Tools.InterpEditor;

public enum InterpPreviewActorBuildKind
{
    Actor,
    StaticMeshCollectionComponent,
    StaticLightCollectionComponent
}

public readonly record struct InterpPreviewActorBuildDescriptor(
    InterpPreviewActorBuildKind Kind,
    int ExportUIndex,
    string ExpectedClassName,
    int CollectionExportUIndex,
    int CollectionIndex,
    Matrix4x4 LocalToWorld,
    Vector3 Location,
    Vector3 Scale,
    LegendaryExplorerCore.Unreal.BinaryConverters.Rotator Rotation,
    int SourceOrder);

public sealed class InterpPreviewPreparedResource : IDisposable
{
    private const int PreparedState = 0;
    private const int RealizingState = 1;
    private const int LoadedState = 2;
    private const int DisposedState = 3;
    private IMEPackage _package;
    private readonly MEGame _game;
    private int _state;

    public InterpPreviewPreparedResource(
        string fullPath,
        IMEPackage package,
        IReadOnlyList<InterpPreviewActorBuildDescriptor> descriptors,
        InterpPreviewResourceKind resourceKind = InterpPreviewResourceKind.Level,
        InterpPreviewPlayerVariant playerVariant = InterpPreviewPlayerVariant.None)
    {
        FullPath = fullPath;
        _package = package;
        _game = package?.Game ?? MEGame.Unknown;
        Descriptors = descriptors is null
            ? Array.Empty<InterpPreviewActorBuildDescriptor>()
            : new List<InterpPreviewActorBuildDescriptor>(descriptors).AsReadOnly();
        ResourceKind = resourceKind;
        PlayerVariant = resourceKind == InterpPreviewResourceKind.PlayerContext
            ? playerVariant
            : InterpPreviewPlayerVariant.None;
    }

    public string FullPath { get; }
    public MEGame Game => _game;
    public InterpPreviewResourceKind ResourceKind { get; }
    public InterpPreviewPlayerVariant PlayerVariant { get; }
    public IReadOnlyList<InterpPreviewActorBuildDescriptor> Descriptors { get; }
    public bool IsPrepared => Volatile.Read(ref _state) == PreparedState;
    public bool IsRealizing => Volatile.Read(ref _state) == RealizingState;
    public bool IsLoaded => Volatile.Read(ref _state) == LoadedState;
    public bool IsDisposed => Volatile.Read(ref _state) == DisposedState;

    internal bool TryBeginRealization(out IMEPackage package)
    {
        if (Interlocked.CompareExchange(ref _state, RealizingState, PreparedState) != PreparedState)
        {
            package = null;
            return false;
        }

        package = Interlocked.Exchange(ref _package, null);
        return true;
    }

    internal void MarkLoaded()
    {
        if (Interlocked.CompareExchange(ref _state, LoadedState, RealizingState) != RealizingState)
        {
            throw new InvalidOperationException("Prepared resource is not being realized.");
        }
    }

    internal void MarkRealizationDisposed()
    {
        Interlocked.CompareExchange(ref _state, DisposedState, RealizingState);
    }

    public void Dispose()
    {
        if (Interlocked.CompareExchange(ref _state, DisposedState, PreparedState) != PreparedState)
        {
            return;
        }

        Interlocked.Exchange(ref _package, null)?.Dispose();
    }
}
