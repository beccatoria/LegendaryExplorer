using System;
using LegendaryExplorerCore.Packages;
using LegendaryExplorerCore.Unreal;
using LegendaryExplorerCore.Unreal.BinaryConverters;

namespace LegendaryExplorer.Tools.AssetDatabase.Scanners
{
    internal class MeshScanner : AssetScanner
    {
        public override void ScanExport(ExportScanInfo e, ConcurrentAssetDB db, AssetDBScanOptions options)
        {
            if (e.IsDefault)
            {
                return;
            }

            if (e.ClassName is "SkeletalMesh" or "StaticMesh")
            {
                AddStandardMeshRecord(e, db);
            }
            else if (e.ClassName == "BrushComponent")
            {
                AddVolumeMeshRecord(e, db);
            }
        }

        private static void AddStandardMeshRecord(ExportScanInfo e, ConcurrentAssetDB db)
        {
            var meshUsage = new MeshUsage(e.FileKey, e.Export.UIndex, e.IsMod);
            if (db.GeneratedMeshes.ContainsKey(e.AssetKey))
            {
                var mr = db.GeneratedMeshes[e.AssetKey];
                lock (mr)
                {
                    mr.Usages.Add(meshUsage);
                }
            }
            else
            {
                bool isSkel = e.ClassName == "SkeletalMesh";
                int bones = 0;
                if (isSkel)
                {
                    var bin = ObjectBinary.From<SkeletalMesh>(e.Export);
                    bones = bin?.RefSkeleton.Length ?? 0;
                }

                var newMeshRec = new MeshRecord(e.ObjectNameInstanced, isSkel, e.IsMod, bones);
                newMeshRec.Usages.Add(meshUsage);
                if (!db.GeneratedMeshes.TryAdd(e.AssetKey, newMeshRec))
                {
                    var mr = db.GeneratedMeshes[e.AssetKey];
                    lock (mr)
                    {
                        mr.Usages.Add(meshUsage);
                    }
                }
            }
        }

        private static void AddVolumeMeshRecord(ExportScanInfo e, ConcurrentAssetDB db)
        {
            if (e.Export.Parent is not ExportEntry ownerExport || ownerExport.IsDefaultObject)
            {
                return;
            }

            if (!ownerExport.ClassName.Contains("Volume", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (e.Properties.GetProp<StructProperty>("BrushAggGeom") is null)
            {
                return;
            }

            var volumeUsage = new MeshUsage(e.FileKey, e.Export.UIndex, e.IsMod);
            string ownerLabel = $"{ownerExport.ObjectName.Instanced} ({ownerExport.ClassName}) [{e.FileName}]";
            string volumeKey = $"volume::{e.FileKey}::{ownerExport.UIndex}";

            if (db.GeneratedMeshes.ContainsKey(volumeKey))
            {
                var existing = db.GeneratedMeshes[volumeKey];
                lock (existing)
                {
                    existing.Usages.Add(volumeUsage);
                }
            }
            else
            {
                var volumeMeshRecord = new MeshRecord(ownerLabel, false, e.IsMod, MeshRecord.TriggerVolumeBoneCountSentinel);
                volumeMeshRecord.Usages.Add(volumeUsage);

                if (!db.GeneratedMeshes.TryAdd(volumeKey, volumeMeshRecord))
                {
                    var existing = db.GeneratedMeshes[volumeKey];
                    lock (existing)
                    {
                        existing.Usages.Add(volumeUsage);
                    }
                }
            }
        }
    }
}
