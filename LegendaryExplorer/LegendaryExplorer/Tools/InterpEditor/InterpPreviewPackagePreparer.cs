using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LegendaryExplorerCore.Packages;
using LegendaryExplorerCore.Unreal.BinaryConverters;

namespace LegendaryExplorer.Tools.InterpEditor;

public sealed class InterpPreviewPackagePreparer : IInterpPreviewPackagePreparer
{
    public Task<InterpPreviewPrepareResult> PrepareAsync(string path, CancellationToken cancellationToken)
    {
        return Task.Run(() => Prepare(path, cancellationToken), cancellationToken);
    }

    private static InterpPreviewPrepareResult Prepare(string path, CancellationToken cancellationToken)
    {
        IMEPackage package = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            string fullPath = Path.GetFullPath(path);
            package = MEPackageHandler.OpenMEPackage(fullPath, forceLoadFromDisk: true);
            cancellationToken.ThrowIfCancellationRequested();

            ExportEntry levelExport = package.Exports.FirstOrDefault(export => export.ClassName == "Level");
            if (levelExport is null)
            {
                throw new InvalidDataException($"{Path.GetFileName(fullPath)} is not a level file.");
            }

            Level level = levelExport.GetBinaryData<Level>();
            cancellationToken.ThrowIfCancellationRequested();

            IReadOnlyList<InterpPreviewActorBuildDescriptor> descriptors = BuildDescriptors(package, level, cancellationToken);
            var preparedResource = new InterpPreviewPreparedResource(fullPath, package, descriptors);
            package = null;
            return InterpPreviewPrepareResult.Prepared(preparedResource);
        }
        catch (OperationCanceledException)
        {
            package?.Dispose();
            return InterpPreviewPrepareResult.Cancelled();
        }
        catch (Exception ex)
        {
            package?.Dispose();
            return cancellationToken.IsCancellationRequested
                ? InterpPreviewPrepareResult.Cancelled()
                : InterpPreviewPrepareResult.Failed(ex);
        }
    }

    private static IReadOnlyList<InterpPreviewActorBuildDescriptor> BuildDescriptors(
        IMEPackage package,
        Level level,
        CancellationToken cancellationToken)
    {
        var descriptors = new List<InterpPreviewActorBuildDescriptor>();
        int sourceOrder = 0;

        foreach (int actorUIndex in level.Actors)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!package.TryGetUExport(actorUIndex, out ExportEntry actorExport))
            {
                continue;
            }

            if (actorExport.ClassName == "StaticMeshCollectionActor")
            {
                AddCollectionDescriptors(
                    package,
                    actorExport,
                    actorExport.GetBinaryData<StaticMeshCollectionActor>(),
                    InterpPreviewActorBuildKind.StaticMeshCollectionComponent,
                    descriptors,
                    ref sourceOrder,
                    cancellationToken);
            }
            else if (actorExport.ClassName == "StaticLightCollectionActor")
            {
                AddCollectionDescriptors(
                    package,
                    actorExport,
                    actorExport.GetBinaryData<StaticLightCollectionActor>(),
                    InterpPreviewActorBuildKind.StaticLightCollectionComponent,
                    descriptors,
                    ref sourceOrder,
                    cancellationToken);
            }
            else
            {
                descriptors.Add(new InterpPreviewActorBuildDescriptor(
                    InterpPreviewActorBuildKind.Actor,
                    actorExport.UIndex,
                    actorExport.ClassName,
                    0,
                    -1,
                    default,
                    default,
                    default,
                    default,
                    sourceOrder++));
            }
        }

        return descriptors
            .OrderBy(descriptor => descriptor.ExportUIndex)
            .ThenBy(descriptor => descriptor.SourceOrder)
            .ToArray();
    }

    private static void AddCollectionDescriptors(
        IMEPackage package,
        ExportEntry collectionExport,
        StaticCollectionActor collection,
        InterpPreviewActorBuildKind kind,
        ICollection<InterpPreviewActorBuildDescriptor> descriptors,
        ref int sourceOrder,
        CancellationToken cancellationToken)
    {
        if (collection.Components is null || collection.LocalToWorldTransforms is null)
        {
            return;
        }

        int count = Math.Min(collection.Components.Count, collection.LocalToWorldTransforms.Count);
        for (int index = 0; index < count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!package.TryGetUExport(collection.Components[index], out ExportEntry componentExport))
            {
                continue;
            }

            var (location, scale, rotation) = collection.GetDecomposedTransformationForIndex(index);
            descriptors.Add(new InterpPreviewActorBuildDescriptor(
                kind,
                componentExport.UIndex,
                componentExport.ClassName,
                collectionExport.UIndex,
                index,
                collection.LocalToWorldTransforms[index],
                location,
                scale,
                rotation,
                sourceOrder++));
        }
    }
}
