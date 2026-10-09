using System;
using System.Collections.Generic;

namespace LegendaryExplorer.Tools.LevelEditor;

public sealed class EditorSelectionState
{
    private readonly HashSet<string> _selectedActorKeys = new(StringComparer.OrdinalIgnoreCase);

    public string PrimaryActorKey { get; private set; }

    public void SetSelection(IEnumerable<string> actorKeys, string primaryActorKey)
    {
        ArgumentNullException.ThrowIfNull(actorKeys);
        _selectedActorKeys.Clear();
        _selectedActorKeys.UnionWith(actorKeys);
        PrimaryActorKey = primaryActorKey;
        if (primaryActorKey is not null)
        {
            _selectedActorKeys.Add(primaryActorKey);
        }
    }

    public bool IsSelected(string actorKey) => actorKey is not null && _selectedActorKeys.Contains(actorKey);

    public void Clear()
    {
        _selectedActorKeys.Clear();
        PrimaryActorKey = null;
    }
}
