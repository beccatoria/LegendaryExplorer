using System;
using System.Collections.Generic;
using System.Globalization;

namespace LegendaryExplorer.Tools.LevelEditor;

public sealed class EditorVisibilityState
{
    public HashSet<string> VisibleActorKeys { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> HiddenActorClasses { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> VisibleActorClasses { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, bool> ActorVisibilityOverrides { get; } = new(StringComparer.OrdinalIgnoreCase);
    public bool DefaultClassVisibility { get; set; } = true;
    public bool HasUserEditedVisibleSets { get; set; }

    public static string GetActorKey(string packagePath, int exportIndex)
        => $"{packagePath}|{exportIndex.ToString(CultureInfo.InvariantCulture)}";

    public bool IsInVisibleSet(string actorKey, float distanceSquared, float maximumDistanceSquared)
        => VisibleActorKeys.Contains(actorKey) && !(distanceSquared > maximumDistanceSquared);

    public bool IsClassVisible(string className) => VisibleActorClasses.Contains(className)
        || (DefaultClassVisibility && !HiddenActorClasses.Contains(className));

    public void SetClassVisibility(string className, bool visible)
    {
        if (visible)
        {
            HiddenActorClasses.Remove(className);
            VisibleActorClasses.Add(className);
        }
        else
        {
            VisibleActorClasses.Remove(className);
            HiddenActorClasses.Add(className);
        }
    }

    public void SetActorVisibility(string actorKey, bool visible)
    {
        ActorVisibilityOverrides[actorKey] = visible;
        SetMembership(actorKey, visible);
    }

    public void RegisterActor(string actorKey, string className)
        => SetMembership(actorKey, ActorVisibilityOverrides.TryGetValue(actorKey, out bool visible)
            ? visible : IsClassVisible(className));

    public void ApplyClassSelection(IReadOnlyList<(string Key, string Class)> actors, ISet<string> visibleClasses,
        ISet<string> resetActorClasses, bool clearActorOverrides, bool? defaultClassVisibility = null)
    {
        if (defaultClassVisibility.HasValue) DefaultClassVisibility = defaultClassVisibility.Value;
        if (clearActorOverrides) ActorVisibilityOverrides.Clear();
        foreach (var actor in actors)
        {
            SetClassVisibility(actor.Class, visibleClasses.Contains(actor.Class));
            // A deliberate class action applies to the whole class. Unchanged classes retain actor edits.
            if (resetActorClasses.Contains(actor.Class)) ActorVisibilityOverrides.Remove(actor.Key);
            RegisterActor(actor.Key, actor.Class);
        }
    }

    public void ForgetActor(string actorKey)
    {
        VisibleActorKeys.Remove(actorKey);
        ActorVisibilityOverrides.Remove(actorKey);
    }

    public void Reset(bool defaultClassVisibility = true)
    {
        VisibleActorKeys.Clear();
        HiddenActorClasses.Clear();
        VisibleActorClasses.Clear();
        ActorVisibilityOverrides.Clear();
        DefaultClassVisibility = defaultClassVisibility;
        HasUserEditedVisibleSets = false;
    }

    private void SetMembership(string actorKey, bool visible)
    {
        if (visible) VisibleActorKeys.Add(actorKey);
        else VisibleActorKeys.Remove(actorKey);
    }
}
