using System;
using System.Collections.Generic;
using System.Linq;

namespace LegendaryExplorer.Tools.LevelEditor;

public static class VolumetricMeshClassifier
{
    public static bool IsVolumetric(string meshPath, IEnumerable<string> materialPaths) =>
        ContainsVolumetricName(meshPath) || (materialPaths?.Any(ContainsVolumetricName) ?? false);

    private static bool ContainsVolumetricName(string path) =>
        path is not null && (path.Contains("Volumetric", StringComparison.OrdinalIgnoreCase)
                             || path.Contains("VolumeLight", StringComparison.OrdinalIgnoreCase));
}
