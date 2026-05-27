using LegendaryExplorerCore.Unreal;

namespace LegendaryExplorer.Tools.AssetDatabase.Scanners
{
    internal class RemoteEventScanner : AssetScanner
    {
        public override void ScanExport(ExportScanInfo e, ConcurrentAssetDB db, AssetDBScanOptions options)
        {
            if (e.IsDefault)
            {
                return;
            }

            RemoteEventUsageType? usageType = e.ClassName switch
            {
                "SeqAct_ActivateRemoteEvent" => RemoteEventUsageType.SeqAct,
                "SeqEvent_RemoteEvent" => RemoteEventUsageType.SeqEvt,
                _ => null
            };

            if (usageType is null)
            {
                return;
            }

            var eventName = e.Properties.GetProp<NameProperty>("EventName")?.Value.Name;
            if (string.IsNullOrWhiteSpace(eventName))
            {
                return;
            }

            var key = eventName.ToLowerInvariant();
            var record = db.GeneratedRemoteEvents.GetOrAdd(key, _ => new RemoteEventRecord(eventName));
            lock (record)
            {
                if (string.IsNullOrEmpty(record.EventName))
                {
                    record.EventName = eventName;
                }

                record.Usages.Add(new RemoteEventUsage(e.FileKey, e.Export.UIndex, e.IsMod, usageType.Value));
            }
        }
    }
}
