using System;
using System.Collections.Generic;
using System.Numerics;

namespace UnityReader.Internal;

// Decodes a vendor-parsed AnimationClip (generic-skeletal Mecanim shape with m_MuscleClip
// + m_ClipBindingConstant) into a per-bone BoneCurves list in System.Numerics types.
internal static class AnimationClipProcessor
{
    // Transform-binding attribute codes; see UnityCsReference: AnimationClipBindings.
    private const uint AttrPosition = 1;
    private const uint AttrRotation = 2;
    private const uint AttrScale    = 3;
    private const uint AttrEuler    = 4;

    public static IReadOnlyList<BoneCurves> Decode(AssetStudio.AnimationClip clip, PathChecksumCache pathCache)
    {
        if (clip.m_Legacy)
            throw new NotSupportedException(
                $"Legacy AnimationClip layout is not supported (clip '{clip.m_Name}').");
        if (clip.m_MuscleClip == null || clip.m_ClipBindingConstant == null)
            throw new NotSupportedException(
                $"Clip '{clip.m_Name}' lacks the muscle-clip + binding-constant pair (generic-skeletal layout).");

        var muscle = clip.m_MuscleClip;
        var inner = muscle.m_Clip?.data;
        if (inner == null)
            throw new NotSupportedException($"Clip '{clip.m_Name}' has empty muscle-clip data.");

        var streamedCount = (int)inner.m_StreamedClip.curveCount;
        var denseCount    = (int)inner.m_DenseClip.m_CurveCount;
        var constantCount = inner.m_ConstantClip?.data?.Length ?? 0;
        var totalCurves   = streamedCount + denseCount + constantCount;

        var streams = new Dictionary<int, List<KeyRecord>>(totalCurves);

        var streamedFrames = inner.m_StreamedClip.ReadData();
        AggregateStreamedFrames(streamedFrames, streams);
        EmitDenseKeyframes(inner.m_DenseClip, streamedCount, streams);
        EmitConstantKeyframes(inner.m_ConstantClip, streamedCount, denseCount,
            ComputeLastSampleTime(streamedFrames, muscle.m_StopTime), streams);

        var perBone = new Dictionary<string, BoneCurvesBuilder>(StringComparer.Ordinal);
        var bindings = clip.m_ClipBindingConstant;
        var cursor = 0;
        while (cursor < totalCurves)
        {
            var binding = bindings.FindBinding(cursor);
            var span = StrideForBinding(binding);
            if (span <= 0) { cursor++; continue; }

            if (binding.typeID == AssetStudio.ClassIDType.Transform)
                BuildBoneCurves(binding, streams, cursor, pathCache, perBone);

            cursor += span;
        }

        var result = new List<BoneCurves>(perBone.Count);
        foreach (var kv in perBone)
            result.Add(kv.Value.Emit(kv.Key));
        return result;
    }

    private static void AggregateStreamedFrames(
        List<AssetStudio.StreamedClip.StreamedFrame> frames,
        Dictionary<int, List<KeyRecord>> sink)
    {
        // Skip sentinels: frame 0 carries PPtr-only keys with a float.MinValue time anchor,
        // and the last frame is a dummy end marker. Neither contributes to transform curves.
        if (frames.Count < 3) return;
        for (var i = 1; i < frames.Count - 1; i++)
        {
            var frame = frames[i];
            foreach (var key in frame.keyList)
            {
                if (!sink.TryGetValue(key.index, out var list))
                    sink[key.index] = list = new List<KeyRecord>();
                list.Add(new KeyRecord(frame.time, key.value, key.inSlope, key.outSlope));
            }
        }

        // Translate vendor stepped-tangent convention to the canonical one. Vendor sets
        // inSlope=+Inf on the next key for stepped segments; canonical wants +Inf on the
        // departing key's outSlope and 0 on the receiving inSlope. Swap accordingly.
        foreach (var list in sink.Values)
        {
            for (var i = 0; i < list.Count - 1; i++)
            {
                if (float.IsPositiveInfinity(list[i + 1].InSlope))
                {
                    list[i]     = list[i]     with { OutSlope = float.PositiveInfinity };
                    list[i + 1] = list[i + 1] with { InSlope  = 0f };
                }
            }
        }
    }

    private static void EmitDenseKeyframes(
        AssetStudio.DenseClip dense,
        int streamedCount,
        Dictionary<int, List<KeyRecord>> sink)
    {
        if (dense == null || dense.m_FrameCount == 0 || dense.m_CurveCount == 0) return;
        var stride = (int)dense.m_CurveCount;
        var rate = dense.m_SampleRate;
        var begin = dense.m_BeginTime;
        var samples = dense.m_SampleArray;

        for (var col = 0; col < stride; col++)
        {
            var curveId = streamedCount + col;
            var list = new List<KeyRecord>(dense.m_FrameCount);
            for (var frame = 0; frame < dense.m_FrameCount; frame++)
            {
                var time = begin + frame / rate;
                var value = samples[frame * stride + col];
                list.Add(new KeyRecord(time, value, InSlope: 0f, OutSlope: 0f));
            }
            sink[curveId] = list;
        }
    }

    private static void EmitConstantKeyframes(
        AssetStudio.ConstantClip? constant,
        int streamedCount,
        int denseCount,
        float lastTime,
        Dictionary<int, List<KeyRecord>> sink)
    {
        if (constant?.data == null || constant.data.Length == 0) return;
        var baseId = streamedCount + denseCount;
        for (var col = 0; col < constant.data.Length; col++)
        {
            var v = constant.data[col];
            sink[baseId + col] = new List<KeyRecord>
            {
                new KeyRecord(0f,        v, InSlope: 0f, OutSlope: 0f),
                new KeyRecord(lastTime,  v, InSlope: 0f, OutSlope: 0f),
            };
        }
    }

    private static float ComputeLastSampleTime(
        List<AssetStudio.StreamedClip.StreamedFrame> streamedFrames,
        float clipStopTime)
    {
        // Anchor is the later of the streamed timeline's last key-bearing frame and the
        // clip's declared StopTime; fall back to StopTime if the timeline is empty.
        if (streamedFrames == null || streamedFrames.Count == 0) return clipStopTime;
        var latest = clipStopTime;
        for (var i = streamedFrames.Count - 1; i >= 0; i--)
        {
            if (streamedFrames[i].keyList.Count > 0)
            {
                if (streamedFrames[i].time > latest) latest = streamedFrames[i].time;
                break;
            }
        }
        return latest;
    }

    private static int StrideForBinding(AssetStudio.GenericBinding b)
    {
        if (b.typeID != AssetStudio.ClassIDType.Transform) return 1;
        return b.attribute switch
        {
            AttrScale    => 3,
            AttrPosition => 3,
            AttrRotation => 4,
            AttrEuler    => 3,
            _            => 1,
        };
    }

    private static void BuildBoneCurves(
        AssetStudio.GenericBinding binding,
        Dictionary<int, List<KeyRecord>> streams,
        int firstCurveId,
        PathChecksumCache pathCache,
        Dictionary<string, BoneCurvesBuilder> perBone)
    {
        if (!pathCache.TryResolve(binding.path, out var bonePath))
            bonePath = $"<unresolved:0x{binding.path:X8}>";

        if (!perBone.TryGetValue(bonePath, out var builder))
            perBone[bonePath] = builder = new BoneCurvesBuilder();

        switch (binding.attribute)
        {
            case AttrScale:
                builder.Scale = StackVector3(streams, firstCurveId);
                break;
            case AttrPosition:
                builder.Position = StackVector3(streams, firstCurveId);
                break;
            case AttrRotation:
                builder.Rotation = StackQuaternion(streams, firstCurveId);
                break;
            case AttrEuler:
                // Distinct channel family (kBindTransformEuler) that must not feed the
                // Rotation curve, otherwise prefabs like excalibur_artifact's
                // artifact_anim_action mis-render as spinning instead of bobbing in place.
                break;
        }
    }

    private static Vector3Curve? StackVector3(Dictionary<int, List<KeyRecord>> streams, int firstId)
    {
        if (!streams.TryGetValue(firstId,     out var x) ||
            !streams.TryGetValue(firstId + 1, out var y) ||
            !streams.TryGetValue(firstId + 2, out var z))
            return null;

        var n = MinCount(x, y, z);
        if (n == 0) return null;

        var keys = new Keyframe<Vector3>[n];
        for (var i = 0; i < n; i++)
        {
            keys[i] = new Keyframe<Vector3>(
                Time: x[i].Time,
                Value:    new Vector3(x[i].Value,    y[i].Value,    z[i].Value),
                InSlope:  new Vector3(x[i].InSlope,  y[i].InSlope,  z[i].InSlope),
                OutSlope: new Vector3(x[i].OutSlope, y[i].OutSlope, z[i].OutSlope));
        }
        return new Vector3Curve(keys);
    }

    private static QuaternionCurve? StackQuaternion(Dictionary<int, List<KeyRecord>> streams, int firstId)
    {
        if (!streams.TryGetValue(firstId,     out var x) ||
            !streams.TryGetValue(firstId + 1, out var y) ||
            !streams.TryGetValue(firstId + 2, out var z) ||
            !streams.TryGetValue(firstId + 3, out var w))
            return null;

        var n = MinCount(x, y, z, w);
        if (n == 0) return null;

        var keys = new Keyframe<Quaternion>[n];
        for (var i = 0; i < n; i++)
        {
            keys[i] = new Keyframe<Quaternion>(
                Time: x[i].Time,
                Value:    new Quaternion(x[i].Value,    y[i].Value,    z[i].Value,    w[i].Value),
                InSlope:  new Quaternion(x[i].InSlope,  y[i].InSlope,  z[i].InSlope,  w[i].InSlope),
                OutSlope: new Quaternion(x[i].OutSlope, y[i].OutSlope, z[i].OutSlope, w[i].OutSlope));
        }
        return new QuaternionCurve(keys);
    }

    private static int MinCount(params List<KeyRecord>[] streams)
    {
        var n = int.MaxValue;
        foreach (var s in streams)
            if (s.Count < n) n = s.Count;
        return n;
    }

    private readonly record struct KeyRecord(float Time, float Value, float InSlope, float OutSlope);

    private sealed class BoneCurvesBuilder
    {
        public Vector3Curve? Position;
        public QuaternionCurve? Rotation;
        public Vector3Curve? Scale;

        public BoneCurves Emit(string bonePath) => new BoneCurves(bonePath, Position, Rotation, Scale);
    }
}
