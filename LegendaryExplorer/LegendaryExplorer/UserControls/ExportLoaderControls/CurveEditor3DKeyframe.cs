using System;
using System.Numerics;
using LegendaryExplorer.Misc;
using LegendaryExplorer.Tools.LevelEditor;
using LegendaryExplorerCore.Unreal;
using LegendaryExplorerCore.Unreal.BinaryConverters;

namespace LegendaryExplorer.UserControls.ExportLoaderControls;

public sealed class CurveEditor3DKeyframe : NotifyPropertyChangedBase, IHitProxy, ITransformWidgetTarget
{
    private readonly Action<CurveEditor3DKeyframe, float?> changed;
    private float time;
    private Vector3 location;
    private Vector3 rotation;
    private EInterpCurveMode posTrackInterpMode;
    private EInterpCurveMode eulerTrackInterpMode;

    internal CurveEditor3DKeyframe(
        InterpCurvePoint<Vector3> positionPoint,
        InterpCurvePoint<Vector3> rotationPoint,
        Vector3 rotation,
        Action<CurveEditor3DKeyframe, float?> changed)
    {
        PositionPoint = positionPoint;
        RotationPoint = rotationPoint;
        time = positionPoint.InVal;
        location = positionPoint.OutVal;
        this.rotation = rotation;
        posTrackInterpMode = positionPoint.InterpMode;
        eulerTrackInterpMode = rotationPoint?.InterpMode ?? positionPoint.InterpMode;
        this.changed = changed;
    }

    internal InterpCurvePoint<Vector3> PositionPoint { get; }

    internal InterpCurvePoint<Vector3> RotationPoint { get; set; }

    public float Time
    {
        get => time;
        set
        {
            if (time == value)
            {
                return;
            }

            float previousTime = time;
            time = value;
            OnPropertyChanged();
            changed(this, previousTime);
        }
    }

    public Vector3 Location
    {
        get => location;
        set => SetLocation(value, null);
    }

    public Vector3 Rotation => rotation;

    public float X
    {
        get => location.X;
        set => SetLocation(location with { X = value }, nameof(X));
    }

    public float Y
    {
        get => location.Y;
        set => SetLocation(location with { Y = value }, nameof(Y));
    }

    public float Z
    {
        get => location.Z;
        set => SetLocation(location with { Z = value }, nameof(Z));
    }

    public float Roll
    {
        get => rotation.X;
        set => SetRotation(rotation with { X = value }, nameof(Roll));
    }

    public float Pitch
    {
        get => rotation.Y;
        set => SetRotation(rotation with { Y = value }, nameof(Pitch));
    }

    public float Yaw
    {
        get => rotation.Z;
        set => SetRotation(rotation with { Z = value }, nameof(Yaw));
    }

    public EInterpCurveMode PosTrackInterpMode
    {
        get => posTrackInterpMode;
        set => SetPosTrackInterpMode(value, commit: true);
    }

    public EInterpCurveMode EulerTrackInterpMode
    {
        get => eulerTrackInterpMode;
        set => SetEulerTrackInterpMode(value, commit: true);
    }

    internal bool SetPosTrackInterpMode(EInterpCurveMode value, bool commit)
    {
        if (!SetProperty(ref posTrackInterpMode, value, nameof(PosTrackInterpMode)))
        {
            return false;
        }

        if (commit)
        {
            changed(this, null);
        }

        return true;
    }

    internal bool SetEulerTrackInterpMode(EInterpCurveMode value, bool commit)
    {
        if (!SetProperty(ref eulerTrackInterpMode, value, nameof(EulerTrackInterpMode)))
        {
            return false;
        }

        if (commit)
        {
            changed(this, null);
        }

        return true;
    }

    public int HitID { get; set; }

    public int HitPriority => IHitProxy.UIPriority;

    Rotator ITransformWidgetTarget.Rotation
    {
        get => Rotator.FromDegreesVector(rotation);
        set => SetRotation(value.GetDegreesVector(), null);
    }

    public float DrawScale { get; set; } = 1f;

    public Vector3 DrawScale3D { get; set; } = Vector3.One;

    public bool IsReadOnly => false;

    public Matrix4x4 LocalToWorld => ActorUtils.ComposeLocalToWorld(Location, Rotator.FromDegreesVector(Rotation), Vector3.One);

    public TransformSnapshot SnapshotTransform() => new(Location, Rotator.FromDegreesVector(Rotation), DrawScale, DrawScale3D);

    private void SetLocation(Vector3 value, string propertyName)
    {
        if (location == value)
        {
            return;
        }

        location = value;
        if (propertyName is not null)
        {
            OnPropertyChanged(propertyName);
        }
        else
        {
            OnPropertyChanged(nameof(X));
            OnPropertyChanged(nameof(Y));
            OnPropertyChanged(nameof(Z));
        }
        OnPropertyChanged(nameof(Location));
        changed(this, null);
    }

    private void SetRotation(Vector3 value, string propertyName)
    {
        if (rotation == value)
        {
            return;
        }

        rotation = value;
        if (propertyName is not null)
        {
            OnPropertyChanged(propertyName);
        }
        else
        {
            OnPropertyChanged(nameof(Roll));
            OnPropertyChanged(nameof(Pitch));
            OnPropertyChanged(nameof(Yaw));
        }
        OnPropertyChanged(nameof(Rotation));
        changed(this, null);
    }
}
