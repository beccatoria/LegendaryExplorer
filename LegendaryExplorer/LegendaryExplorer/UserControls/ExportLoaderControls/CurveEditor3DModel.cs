using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using LegendaryExplorerCore.Packages;
using LegendaryExplorerCore.Unreal;
using LegendaryExplorerCore.Unreal.BinaryConverters;
using InterpCurveVector = LegendaryExplorerCore.Unreal.BinaryConverters.InterpCurve<System.Numerics.Vector3>;

namespace LegendaryExplorer.UserControls.ExportLoaderControls;

public sealed class CurveEditor3DModel
{
    private const float KeyTimeTolerance = 0.0001f;

    public ExportEntry Export { get; private set; }

    public InterpCurveVector PositionTrack { get; private set; }

    public InterpCurveVector RotationTrack { get; private set; }

    public List<CurveEditor3DKeyframe> Keyframes { get; } = [];

    public event Action Changed;

    public void Load(ExportEntry export)
    {
        Export = export;
        PropertyCollection properties = export.GetProperties();
        var emptyCurve = new StructProperty("InterpCurveVector", false);
        PositionTrack = InterpCurveVector.FromStructProperty(properties.GetProp<StructProperty>("PosTrack") ?? emptyCurve, export.Game);
        RotationTrack = InterpCurveVector.FromStructProperty(properties.GetProp<StructProperty>("EulerTrack") ?? emptyCurve, export.Game);
        RebuildKeyframes();
    }

    public void Clear()
    {
        Export = null;
        PositionTrack = null;
        RotationTrack = null;
        Keyframes.Clear();
    }

    public IReadOnlyList<Vector3> SampleTrajectory(int samplesPerSegment = 16)
    {
        if (Keyframes.Count == 0)
        {
            return [];
        }

        if (Keyframes.Count == 1)
        {
            return [Keyframes[0].Location];
        }

        var samples = new List<Vector3>((Keyframes.Count - 1) * samplesPerSegment + 1);
        for (int keyIndex = 0; keyIndex < Keyframes.Count - 1; keyIndex++)
        {
            float start = Keyframes[keyIndex].Time;
            float end = Keyframes[keyIndex + 1].Time;
            for (int sampleIndex = 0; sampleIndex < samplesPerSegment; sampleIndex++)
            {
                float time = start + ((end - start) * sampleIndex / samplesPerSegment);
                samples.Add(PositionTrack.Eval(time, Vector3.Zero));
            }
        }

        samples.Add(PositionTrack.Eval(Keyframes[^1].Time, Vector3.Zero));
        return samples;
    }

    public CurveEditor3DKeyframe AddKeyframe(CurveEditor3DKeyframe selectedKeyframe, bool addAfter)
    {
        if (Export is null || selectedKeyframe is null)
        {
            return null;
        }

        float newTime = selectedKeyframe.Time + (addAfter ? 5f : -5f);
        Vector3 newLocation = selectedKeyframe.Location + new Vector3(100f, 100f, 100f);
        Vector3 newRotation = selectedKeyframe.Rotation;
        InterpCurvePoint<Vector3> positionPoint = AddPoint(PositionTrack, newTime, newLocation, selectedKeyframe.PosTrackInterpMode);
        InterpCurvePoint<Vector3> rotationPoint = AddPoint(RotationTrack, newTime, newRotation, selectedKeyframe.EulerTrackInterpMode);
        var keyframe = new CurveEditor3DKeyframe(positionPoint, rotationPoint, newRotation, CommitKeyframe);
        Keyframes.Add(keyframe);
        CommitKeyframe(keyframe, null);
        return keyframe;
    }

    public CurveEditor3DKeyframe AddKeyframeAfterLast(Vector3 location)
    {
        if (Export is null || Keyframes.Count == 0)
        {
            return null;
        }

        CurveEditor3DKeyframe lastKeyframe = Keyframes[^1];
        float newTime = lastKeyframe.Time + 1f;
        Vector3 newRotation = lastKeyframe.Rotation;
        InterpCurvePoint<Vector3> positionPoint = AddPoint(PositionTrack, newTime, location, lastKeyframe.PosTrackInterpMode);
        InterpCurvePoint<Vector3> rotationPoint = AddPoint(RotationTrack, newTime, newRotation, lastKeyframe.EulerTrackInterpMode);
        var keyframe = new CurveEditor3DKeyframe(positionPoint, rotationPoint, newRotation, CommitKeyframe);
        Keyframes.Add(keyframe);
        CommitKeyframe(keyframe, null);
        return keyframe;
    }

    public CurveEditor3DKeyframe DeleteKeyframe(CurveEditor3DKeyframe keyframe)
    {
        if (Export is null || keyframe is null)
        {
            return null;
        }

        int index = Keyframes.IndexOf(keyframe);
        if (index < 0)
        {
            return null;
        }

        PositionTrack.Points.Remove(keyframe.PositionPoint);
        if (keyframe.RotationPoint is not null)
        {
            RotationTrack.Points.Remove(keyframe.RotationPoint);
        }
        Keyframes.RemoveAt(index);
        PositionTrack.ReCalculateTangents();
        RotationTrack.ReCalculateTangents();
        WriteTracks();
        Changed?.Invoke();

        if (Keyframes.Count == 0)
        {
            return null;
        }

        return Keyframes[Math.Min(index, Keyframes.Count - 1)];
    }

    public void SetAllPosTrackInterpModes(EInterpCurveMode interpMode)
    {
        if (Export is null)
        {
            return;
        }

        foreach (CurveEditor3DKeyframe keyframe in Keyframes)
        {
            keyframe.SetPosTrackInterpMode(interpMode, commit: false);
        }

        foreach (InterpCurvePoint<Vector3> point in PositionTrack.Points)
        {
            point.InterpMode = interpMode;
        }

        PositionTrack.ReCalculateTangents();
        WriteTracks();
        Changed?.Invoke();
    }

    public void SetAllEulerTrackInterpModes(EInterpCurveMode interpMode)
    {
        if (Export is null)
        {
            return;
        }

        foreach (CurveEditor3DKeyframe keyframe in Keyframes)
        {
            keyframe.SetEulerTrackInterpMode(interpMode, commit: false);
        }

        foreach (InterpCurvePoint<Vector3> point in RotationTrack.Points)
        {
            point.InterpMode = interpMode;
        }

        RotationTrack.ReCalculateTangents();
        WriteTracks();
        Changed?.Invoke();
    }

    private void RebuildKeyframes()
    {
        Keyframes.Clear();
        foreach (InterpCurvePoint<Vector3> positionPoint in PositionTrack.Points.OrderBy(point => point.InVal))
        {
            float time = positionPoint.InVal;
            InterpCurvePoint<Vector3> rotationPoint = FindPoint(RotationTrack, time);
            Keyframes.Add(new CurveEditor3DKeyframe(
                positionPoint,
                rotationPoint,
                RotationTrack.Eval(time, Vector3.Zero),
                CommitKeyframe));
        }
    }

    private void CommitKeyframe(CurveEditor3DKeyframe keyframe, float? previousTime)
    {
        if (Export is null)
        {
            return;
        }

        InterpCurvePoint<Vector3> positionPoint = keyframe.PositionPoint;
        InterpCurvePoint<Vector3> rotationPoint = keyframe.RotationPoint ??= AddPoint(
            RotationTrack,
            previousTime ?? keyframe.Time,
            keyframe.Rotation,
            keyframe.EulerTrackInterpMode);

        positionPoint.InVal = keyframe.Time;
        positionPoint.OutVal = keyframe.Location;
        positionPoint.InterpMode = keyframe.PosTrackInterpMode;
        rotationPoint.InVal = keyframe.Time;
        rotationPoint.OutVal = keyframe.Rotation;
        rotationPoint.InterpMode = keyframe.EulerTrackInterpMode;
        PositionTrack.Points.Sort((left, right) => left.InVal.CompareTo(right.InVal));
        RotationTrack.Points.Sort((left, right) => left.InVal.CompareTo(right.InVal));
        Keyframes.Sort((left, right) => left.Time.CompareTo(right.Time));
        PositionTrack.ReCalculateTangents();
        RotationTrack.ReCalculateTangents();
        WriteTracks();
        Changed?.Invoke();
    }

    private void WriteTracks()
    {
        PropertyCollection properties = Export.GetProperties();
        properties.AddOrReplaceProp(PositionTrack.ToStructProperty(Export.Game, "PosTrack"));
        properties.AddOrReplaceProp(RotationTrack.ToStructProperty(Export.Game, "EulerTrack"));
        Export.WriteProperties(properties);
    }

    private static InterpCurvePoint<Vector3> FindPoint(InterpCurveVector track, float time)
        => track.Points.FirstOrDefault(point => MathF.Abs(point.InVal - time) <= KeyTimeTolerance);

    private static InterpCurvePoint<Vector3> AddPoint(InterpCurveVector track, float time, Vector3 value, EInterpCurveMode interpMode)
    {
        int index = track.AddPoint(time, value, Vector3.Zero, Vector3.Zero, interpMode);
        return track.Points[index];
    }
}
