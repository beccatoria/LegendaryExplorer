using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using LegendaryExplorer.Tools.LevelEditor;
using LegendaryExplorerCore.Packages;
using LegendaryExplorerCore.Unreal.BinaryConverters;
using LegendaryExplorerCore.Unreal.ObjectInfo;

namespace LegendaryExplorer.Tools.InterpEditor;

public sealed class InterpPreviewLevelLoader : IInterpPreviewLevelLoader
{
    public InterpPreviewLoadedLevel LoadLevel(string path, IActorEditorContext actorEditorContext, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string fullPath = Path.GetFullPath(path);

        IMEPackage package = MEPackageHandler.OpenMEPackage(fullPath);
        try
        {
            ExportEntry levelExport = package.Exports.FirstOrDefault(export => export.ClassName == "Level");
            if (levelExport is null)
            {
                throw new InvalidDataException($"{Path.GetFileName(fullPath)} is not a level file.");
            }

            Level level = levelExport.GetBinaryData<Level>();
            List<ActorProxy> actors = LoadActors(level, actorEditorContext);
            cancellationToken.ThrowIfCancellationRequested();
            return new InterpPreviewLoadedLevel(fullPath, package, actors);
        }
        catch
        {
            package.Dispose();
            throw;
        }
    }

    private static List<ActorProxy> LoadActors(Level level, IActorEditorContext actorEditorContext)
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
                        actors.Add(new StaticMeshComponentActorProxy(actorEditorContext, component, collection, index));
                    }
                }
            }
            else if (actorExport.ClassName == "StaticLightCollectionActor")
            {
                var collection = actorExport.GetBinaryData<StaticLightCollectionActor>();
                for (int index = 0; index < collection.Components.Count; index++)
                {
                    if (!level.Export.FileRef.TryGetUExport(collection.Components[index], out ExportEntry lightExport))
                    {
                        continue;
                    }

                    actors.Add(new StaticLightComponentActorProxy(actorEditorContext, lightExport, collection, index));
                }
            }
            else if (ActorProxy.Create(actorEditorContext, actorExport) is { } actor)
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
}
