using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using LegendaryExplorer.Misc;
using LegendaryExplorerCore.Helpers;
using LegendaryExplorerCore.SharpDX;
using LegendaryExplorerCore.Unreal.BinaryConverters;

namespace LegendaryExplorer.Tools.LevelEditor;

public readonly record struct TransformSnapshot(
    Vector3 Location,
    Rotator Rotation,
    float DrawScale,
    Vector3 DrawScale3D)
{
    public bool Equals(TransformSnapshot other) =>
        Location == other.Location &&
        Rotation == other.Rotation &&
        DrawScale == other.DrawScale &&
        DrawScale3D == other.DrawScale3D;
}

public static class GroupTransformEdit
{
    public static TransformBatchAction Apply(ActorProxy lead, IEnumerable<ActorProxy> members,
        Func<ActorProxy, bool> isAvailable, TransformSnapshot before, TransformSnapshot after, string description,
        IReadOnlyDictionary<ActorProxy, TransformSnapshot> startingTransforms = null)
    {
        bool locationChanged = before.Location != after.Location;
        bool rotationChanged = before.Rotation != after.Rotation;
        Matrix4x4 rotationDelta = Matrix4x4.Identity;
        if (rotationChanged && Matrix4x4.Invert(before.Rotation.ToRotationMatrix(), out Matrix4x4 inverse))
        {
            rotationDelta = inverse * after.Rotation.ToRotationMatrix();
        }

        var entries = new List<(ActorProxy Actor, TransformSnapshot Before, TransformSnapshot After)>
        {
            (lead, before, after)
        };
        var visited = new HashSet<ActorProxy> { lead };
        foreach (ActorProxy member in members)
        {
            if (member is null || !visited.Add(member) || member.IsReadOnly || !isAvailable(member)) continue;
            TransformSnapshot memberBefore = startingTransforms is not null
                ? startingTransforms[member] : member.SnapshotTransform();
            Vector3 location = memberBefore.Location;
            if (rotationChanged)
            {
                location = before.Location + Vector3.Transform(location - before.Location, rotationDelta);
            }
            if (locationChanged) location += after.Location - before.Location;
            var memberAfter = new TransformSnapshot(
                location,
                rotationChanged ? (memberBefore.Rotation.ToRotationMatrix() * rotationDelta).GetRotator() : memberBefore.Rotation,
                before.DrawScale != after.DrawScale ? ApplyScaleDelta(memberBefore.DrawScale, before.DrawScale, after.DrawScale) : memberBefore.DrawScale,
                before.DrawScale3D != after.DrawScale3D ? new Vector3(
                    ApplyScaleDelta(memberBefore.DrawScale3D.X, before.DrawScale3D.X, after.DrawScale3D.X),
                    ApplyScaleDelta(memberBefore.DrawScale3D.Y, before.DrawScale3D.Y, after.DrawScale3D.Y),
                    ApplyScaleDelta(memberBefore.DrawScale3D.Z, before.DrawScale3D.Z, after.DrawScale3D.Z)) : memberBefore.DrawScale3D);
            if (!member.SnapshotTransform().Equals(memberAfter))
            {
                member.RestoreTransform(memberAfter);
            }
            if (!memberBefore.Equals(memberAfter))
            {
                entries.Add((member, memberBefore, member.SnapshotTransform()));
            }
        }
        return new TransformBatchAction(entries, description);
    }

    private static float ApplyScaleDelta(float value, float before, float after)
    {
        if (before != 0f)
        {
            float factor = after / before;
            if (!float.IsNaN(factor) && !float.IsInfinity(factor)) return value * factor;
        }
        return value + after - before;
    }
}

public sealed class GroupTransformDrag
{
    private readonly ActorProxy _pivot;
    private readonly TransformSnapshot _before;
    private readonly Dictionary<ActorProxy, TransformSnapshot> _startingTransforms;
    private readonly Func<ActorProxy, bool> _isAvailable;

    public GroupTransformDrag(ActorProxy pivot, IEnumerable<ActorProxy> members, Func<ActorProxy, bool> isAvailable)
    {
        _pivot = pivot;
        _before = pivot.SnapshotTransform();
        _isAvailable = isAvailable;
        _startingTransforms = members.Where(actor => actor is not null && !actor.IsReadOnly && isAvailable(actor))
            .Distinct().ToDictionary(actor => actor, actor => actor.SnapshotTransform());
    }

    public TransformBatchAction Update(TransformSnapshot after, string description)
        => GroupTransformEdit.Apply(_pivot, _startingTransforms.Keys, _isAvailable, _before, after,
            description, _startingTransforms);
}

public interface IUndoAction
{
    string Description { get; }
    void Undo();
    void Redo();
    bool Rebind(Func<ActorProxy, ActorProxy> resolve) => true;
}

public class TransformAction : IUndoAction
{
    private ActorProxy _actor;
    private readonly TransformSnapshot _before;
    private readonly TransformSnapshot _after;

    public string Description { get; }

    public TransformAction(ActorProxy actor, TransformSnapshot before, TransformSnapshot after, string description)
    {
        _actor = actor;
        _before = before;
        _after = after;
        Description = description;
    }

    public void Undo() => _actor.RestoreTransform(_before);
    public void Redo() => _actor.RestoreTransform(_after);
    public bool Rebind(Func<ActorProxy, ActorProxy> resolve)
    {
        _actor = resolve(_actor);
        return _actor is not null;
    }
}

public class TransformBatchAction : IUndoAction
{
    private IReadOnlyList<(ActorProxy Actor, TransformSnapshot Before, TransformSnapshot After)> _entries;

    public string Description { get; }

    public TransformBatchAction(IReadOnlyList<(ActorProxy Actor, TransformSnapshot Before, TransformSnapshot After)> entries, string description)
    {
        _entries = entries;
        Description = description;
    }

    public bool Rebind(Func<ActorProxy, ActorProxy> resolve)
    {
        _entries = _entries.Select(entry => (Actor: resolve(entry.Actor), entry.Before, entry.After))
            .Where(entry => entry.Actor is not null).ToArray();
        return _entries.Count > 0;
    }

    public void Undo()
    {
        for (int i = 0; i < _entries.Count; i++)
        {
            var entry = _entries[i];
            entry.Actor.RestoreTransform(entry.Before);
        }
    }

    public void Redo()
    {
        for (int i = 0; i < _entries.Count; i++)
        {
            var entry = _entries[i];
            entry.Actor.RestoreTransform(entry.After);
        }
    }
}

public class UndoHistory : NotifyPropertyChangedBase
{
    private readonly Stack<IUndoAction> _undoStack = new();
    private readonly Stack<IUndoAction> _redoStack = new();

    public const int MaxHistorySize = 1000;

    public bool CanUndo => _undoStack.Count > 0;
    public bool CanRedo => _redoStack.Count > 0;

    public void RebindActors(IEnumerable<ActorProxy> actors)
    {
        var available = actors.ToDictionary(actor => EditorVisibilityState.GetActorKey(actor.Export.FileRef.FilePath, actor.Export.UIndex),
            StringComparer.OrdinalIgnoreCase);
        ActorProxy Resolve(ActorProxy actor) => available.GetValueOrDefault(
            EditorVisibilityState.GetActorKey(actor.Export.FileRef.FilePath, actor.Export.UIndex));
        RebindStack(_undoStack);
        RebindStack(_redoStack);
        NotifyCanUndoRedoChanged();

        void RebindStack(Stack<IUndoAction> stack)
        {
            var retained = stack.Where(action => action.Rebind(Resolve)).ToArray();
            stack.Clear();
            for (int i = retained.Length - 1; i >= 0; i--) stack.Push(retained[i]);
        }
    }

    private void NotifyCanUndoRedoChanged()
    {
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
    }

    public void Push(IUndoAction action)
    {
        if (_undoStack.Count >= MaxHistorySize)
        {
            // Convert to array, keep most recent entries
            var items = _undoStack.ToArray();
            _undoStack.Clear();
            // Items are in LIFO order (newest first), so keep first MaxHistorySize/2
            for (int i = MaxHistorySize / 2 - 1; i >= 0; i--)
            {
                _undoStack.Push(items[i]);
            }
        }
        _undoStack.Push(action);
        _redoStack.Clear();
        NotifyCanUndoRedoChanged();
    }

    public void Undo()
    {
        if (_undoStack.TryPop(out var action))
        {
            action.Undo();
            _redoStack.Push(action);
        }
        NotifyCanUndoRedoChanged();
    }

    public void Redo()
    {
        if (_redoStack.TryPop(out var action))
        {
            action.Redo();
            _undoStack.Push(action);
        }
        NotifyCanUndoRedoChanged();
    }

    public void UndoMultiple(int count)
    {
        for (int i = 0; i < count && _undoStack.TryPop(out var action); i++)
        {
            action.Undo();
            _redoStack.Push(action);
        }
        NotifyCanUndoRedoChanged();
    }

    public void RedoMultiple(int count)
    {
        for (int i = 0; i < count && _redoStack.TryPop(out var action); i++)
        {
            action.Redo();
            _undoStack.Push(action);
        }
        NotifyCanUndoRedoChanged();
    }

    public void Clear()
    {
        _undoStack.Clear();
        _redoStack.Clear();
        NotifyCanUndoRedoChanged();
    }
}
