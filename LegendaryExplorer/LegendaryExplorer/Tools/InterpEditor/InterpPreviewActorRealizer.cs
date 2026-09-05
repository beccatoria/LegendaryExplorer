using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LegendaryExplorer.Tools.LevelEditor;
using LegendaryExplorerCore.Packages;

namespace LegendaryExplorer.Tools.InterpEditor;

public sealed class InterpPreviewActorProxyFactory : IInterpPreviewActorProxyFactory
{
    public ActorProxy Create(
        IActorEditorContext actorEditorContext,
        IMEPackage package,
        InterpPreviewActorBuildDescriptor descriptor)
    {
        if (!package.TryGetUExport(descriptor.ExportUIndex, out ExportEntry actorExport))
        {
            throw new InvalidDataException($"Actor export #{descriptor.ExportUIndex} is missing.");
        }

        if (!string.Equals(actorExport.ClassName, descriptor.ExpectedClassName, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"Actor export #{descriptor.ExportUIndex} changed class from {descriptor.ExpectedClassName} to {actorExport.ClassName}.");
        }

        switch (descriptor.Kind)
        {
            case InterpPreviewActorBuildKind.Actor:
                return ActorProxy.Create(actorEditorContext, actorExport);
            case InterpPreviewActorBuildKind.StaticMeshCollectionComponent:
                return new StaticMeshComponentActorProxy(
                    actorEditorContext,
                    GetCollectionExport(package, descriptor),
                    actorExport,
                    descriptor.LocalToWorld,
                    descriptor.Location,
                    descriptor.Scale,
                    descriptor.Rotation);
            case InterpPreviewActorBuildKind.StaticLightCollectionComponent:
                return new StaticLightComponentActorProxy(
                    actorEditorContext,
                    GetCollectionExport(package, descriptor),
                    actorExport,
                    descriptor.LocalToWorld,
                    descriptor.Location,
                    descriptor.Scale,
                    descriptor.Rotation);
            default:
                throw new ArgumentOutOfRangeException(nameof(descriptor.Kind), descriptor.Kind, "Unknown actor build kind.");
        }
    }

    private static ExportEntry GetCollectionExport(IMEPackage package, InterpPreviewActorBuildDescriptor descriptor)
    {
        if (!package.TryGetUExport(descriptor.CollectionExportUIndex, out ExportEntry collectionExport))
        {
            throw new InvalidDataException($"Collection export #{descriptor.CollectionExportUIndex} is missing.");
        }

        string expectedClass = descriptor.Kind == InterpPreviewActorBuildKind.StaticMeshCollectionComponent
            ? "StaticMeshCollectionActor"
            : "StaticLightCollectionActor";
        if (!string.Equals(collectionExport.ClassName, expectedClass, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"Collection export #{descriptor.CollectionExportUIndex} is not a {expectedClass}.");
        }

        return collectionExport;
    }
}

public sealed class InterpPreviewActorRealizer : IInterpPreviewActorRealizer
{
    private const int DefaultBatchSize = 64;
    private readonly IInterpPreviewDispatcher _dispatcher;
    private readonly IInterpPreviewActorProxyFactory _proxyFactory;
    private readonly int _batchSize;

    public InterpPreviewActorRealizer(
        IInterpPreviewDispatcher dispatcher,
        IInterpPreviewActorProxyFactory proxyFactory,
        int batchSize = DefaultBatchSize)
    {
        _dispatcher = dispatcher;
        _proxyFactory = proxyFactory;
        _batchSize = Math.Max(1, batchSize);
    }

    public async Task<InterpPreviewRealizeResult> RealizeAsync(
        InterpPreviewPreparedResource preparedResource,
        IActorEditorContext actorEditorContext,
        Func<bool> canContinue,
        CancellationToken cancellationToken)
    {
        if (preparedResource is null)
        {
            return InterpPreviewRealizeResult.Failed(new ArgumentNullException(nameof(preparedResource)));
        }

        if (!canContinue())
        {
            preparedResource.Dispose();
            return InterpPreviewRealizeResult.Superseded();
        }

        IMEPackage package = null;
        var actors = new List<ActorProxy>();
        try
        {
            bool transferred = await _dispatcher.InvokeAsync(() =>
            {
                _dispatcher.VerifyAccess();
                cancellationToken.ThrowIfCancellationRequested();
                return canContinue() && preparedResource.TryBeginRealization(out package);
            }, cancellationToken).ConfigureAwait(false);

            if (!transferred)
            {
                preparedResource.Dispose();
                return cancellationToken.IsCancellationRequested
                    ? InterpPreviewRealizeResult.Cancelled()
                    : InterpPreviewRealizeResult.Superseded();
            }

            IReadOnlyList<InterpPreviewActorBuildDescriptor> descriptors = preparedResource.Descriptors;
            for (int start = 0; start < descriptors.Count; start += _batchSize)
            {
                int batchStart = start;
                int batchCount = Math.Min(_batchSize, descriptors.Count - batchStart);
                await _dispatcher.InvokeAsync(() =>
                {
                    _dispatcher.VerifyAccess();
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!canContinue())
                    {
                        throw new InterpPreviewSupersededException();
                    }

                    for (int index = batchStart; index < batchStart + batchCount; index++)
                    {
                        if (_proxyFactory.Create(actorEditorContext, package, descriptors[index]) is { } actor)
                        {
                            actors.Add(actor);
                        }
                    }
                }, cancellationToken).ConfigureAwait(false);

                if (start + batchCount < descriptors.Count)
                {
                    await _dispatcher.YieldAsync(cancellationToken).ConfigureAwait(false);
                }
            }

            return await _dispatcher.InvokeAsync(() =>
            {
                _dispatcher.VerifyAccess();
                cancellationToken.ThrowIfCancellationRequested();
                if (!canContinue())
                {
                    throw new InterpPreviewSupersededException();
                }

                foreach (ActorProxy actor in actors)
                {
                    actor.ResolveAttachment(actors);
                }

                var loadedLevel = new InterpPreviewLoadedLevel(
                    preparedResource.FullPath,
                    package,
                    actors.OrderBy(actor => actor.Export.UIndex).ToList(),
                    preparedResource.ResourceKind,
                    preparedResource.PlayerVariant);
                package = null;
                actors.Clear();
                preparedResource.MarkLoaded();
                return InterpPreviewRealizeResult.Loaded(loadedLevel);
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            await DisposeCandidateAsync(actors, package, preparedResource).ConfigureAwait(false);
            return InterpPreviewRealizeResult.Cancelled();
        }
        catch (InterpPreviewSupersededException)
        {
            await DisposeCandidateAsync(actors, package, preparedResource).ConfigureAwait(false);
            return InterpPreviewRealizeResult.Superseded();
        }
        catch (Exception ex)
        {
            await DisposeCandidateAsync(actors, package, preparedResource).ConfigureAwait(false);
            return InterpPreviewRealizeResult.Failed(ex);
        }
    }

    private Task DisposeCandidateAsync(
        IEnumerable<ActorProxy> actors,
        IMEPackage package,
        InterpPreviewPreparedResource preparedResource)
    {
        if (package is null)
        {
            preparedResource.Dispose();
            return Task.CompletedTask;
        }

        return _dispatcher.InvokeAsync(() =>
        {
            _dispatcher.VerifyAccess();
            foreach (ActorProxy actor in actors)
            {
                actor?.Dispose();
            }

            package.Dispose();
            preparedResource.MarkRealizationDisposed();
        });
    }

    private sealed class InterpPreviewSupersededException : Exception
    {
    }
}
