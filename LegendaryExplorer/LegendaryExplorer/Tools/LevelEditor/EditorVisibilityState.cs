using System;
using System.Collections.Generic;
using System.Globalization;

namespace LegendaryExplorer.Tools.LevelEditor;

public sealed class EditorVisibilityState
{
    public HashSet<string> VisibleActorKeys { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> HiddenActorClasses { get; } = new(StringComparer.OrdinalIgnoreCase);
    public bool HasUserEditedVisibleSets { get; set; }

    public static string GetActorKey(string packagePath, int exportIndex)
        => $"{packagePath}|{exportIndex.ToString(CultureInfo.InvariantCulture)}";

    public bool IsInVisibleSet(string actorKey, float distanceSquared, float maximumDistanceSquared)
        => VisibleActorKeys.Contains(actorKey) && !(distanceSquared > maximumDistanceSquared);
}
