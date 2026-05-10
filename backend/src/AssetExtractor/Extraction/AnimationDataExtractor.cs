#nullable enable
using AssetExtractor.Models;
using AssetExtractor.Extraction.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace AssetExtractor.Extraction;

public class AnimationDataExtractor : IAnimationDataExtractor
{
    private readonly ILogger<AnimationDataExtractor> _logger;
    private readonly IGameObjectProvider _gameObjectProvider;

    public AnimationDataExtractor(IGameObjectProvider gameObjectProvider, ILogger<AnimationDataExtractor>? logger = null)
    {
        _logger = logger ?? NullLogger<AnimationDataExtractor>.Instance;
        _gameObjectProvider = gameObjectProvider;
    }

    public List<AnimationData> ExtractAnimations(HierarchyNode inner, SkeletonData skeleton, List<string> skinJointPaths)
    {
        var animations = new List<AnimationData>();

        try
        {
            var gameObject = _gameObjectProvider.FindGameObjectByPathId(inner.SourceFile, inner.PathID);
            if (gameObject == null)
            {
                _logger.LogWarning("Cannot find GameObject for inner node: {InnerNodeName}", inner.Name);
                return animations;
            }

            if (!gameObject.TryGetComponent<UnityReader.Animator>(out var animator))
            {
                _logger.LogWarning("No Animator component found on node: {NodeName}", inner.Name);
                return animations;
            }

            var pathToIndex = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var bone in skeleton.Bones)
            {
                if (!string.IsNullOrEmpty(bone.Path) && !pathToIndex.ContainsKey(bone.Path))
                    pathToIndex[bone.Path] = bone.Index;
            }

            var clips = animator.AnimationClips;
            var usedAnimationNames = new HashSet<string>(StringComparer.Ordinal);

            foreach (var clip in clips)
            {
                if (clip == null) continue;

                var animData = ExtractSingleClip(clip, animator, pathToIndex);
                if (animData != null && animData.Channels.Count > 0)
                {
                    animData.Name = MakeUniqueName(NormalizeAnimationName(animData.Name), usedAnimationNames);
                    animations.Add(animData);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to extract animations");
        }

        return animations;
    }

    private AnimationData? ExtractSingleClip(UnityReader.AnimationClip clip, UnityReader.Animator animator, Dictionary<string, int> pathToIndex)
    {
        try
        {
            var name = string.IsNullOrEmpty(clip.Name) ? "animation" : clip.Name;
            if (!clip.HasMuscleClip || !clip.HasClipBindingConstant) return null;

            IReadOnlyList<UnityReader.BoneCurves> boneCurves;
            try { boneCurves = animator.ProcessClip(clip); }
            catch (NotSupportedException) { return null; }

            var animData = new AnimationData
            {
                Name = name,
                FrameRate = clip.SampleRate,
                Length = clip.Length
            };

            foreach (var bc in boneCurves)
            {
                int boneIndex = ResolveBoneIndex(bc.BonePath, pathToIndex);

                if (bc.Position != null)
                    EmitVector3Curve(bc.Position, bc.BonePath, boneIndex, animData,
                        AnimationProperty.PositionX, AnimationProperty.PositionY, AnimationProperty.PositionZ);
                if (bc.Rotation != null)
                    EmitQuaternionCurve(bc.Rotation, bc.BonePath, boneIndex, animData);
                if (bc.Scale != null)
                    EmitVector3Curve(bc.Scale, bc.BonePath, boneIndex, animData,
                        AnimationProperty.ScaleX, AnimationProperty.ScaleY, AnimationProperty.ScaleZ);
            }

            return animData;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to extract clip '{ClipName}'", clip.Name);
            return null;
        }
    }

    private static void EmitVector3Curve(UnityReader.Vector3Curve curve, string path, int boneIndex, AnimationData animData,
        AnimationProperty propX, AnimationProperty propY, AnimationProperty propZ)
    {
        var channelX = new AnimationChannel { BonePath = path, BoneIndex = boneIndex, Property = propX, Keyframes = new() };
        var channelY = new AnimationChannel { BonePath = path, BoneIndex = boneIndex, Property = propY, Keyframes = new() };
        var channelZ = new AnimationChannel { BonePath = path, BoneIndex = boneIndex, Property = propZ, Keyframes = new() };

        foreach (var kf in curve.Keyframes)
        {
            channelX.Keyframes.Add(new Keyframe { Time = kf.Time, Value = kf.Value.X, InTangent = kf.InSlope.X, OutTangent = kf.OutSlope.X });
            channelY.Keyframes.Add(new Keyframe { Time = kf.Time, Value = kf.Value.Y, InTangent = kf.InSlope.Y, OutTangent = kf.OutSlope.Y });
            channelZ.Keyframes.Add(new Keyframe { Time = kf.Time, Value = kf.Value.Z, InTangent = kf.InSlope.Z, OutTangent = kf.OutSlope.Z });
        }

        if (channelX.Keyframes.Count > 0) animData.Channels.Add(channelX);
        if (channelY.Keyframes.Count > 0) animData.Channels.Add(channelY);
        if (channelZ.Keyframes.Count > 0) animData.Channels.Add(channelZ);
    }

    private static void EmitQuaternionCurve(UnityReader.QuaternionCurve curve, string path, int boneIndex, AnimationData animData)
    {
        var channelX = new AnimationChannel { BonePath = path, BoneIndex = boneIndex, Property = AnimationProperty.RotationX, Keyframes = new() };
        var channelY = new AnimationChannel { BonePath = path, BoneIndex = boneIndex, Property = AnimationProperty.RotationY, Keyframes = new() };
        var channelZ = new AnimationChannel { BonePath = path, BoneIndex = boneIndex, Property = AnimationProperty.RotationZ, Keyframes = new() };
        var channelW = new AnimationChannel { BonePath = path, BoneIndex = boneIndex, Property = AnimationProperty.RotationW, Keyframes = new() };

        foreach (var kf in curve.Keyframes)
        {
            channelX.Keyframes.Add(new Keyframe { Time = kf.Time, Value = kf.Value.X, InTangent = kf.InSlope.X, OutTangent = kf.OutSlope.X });
            channelY.Keyframes.Add(new Keyframe { Time = kf.Time, Value = kf.Value.Y, InTangent = kf.InSlope.Y, OutTangent = kf.OutSlope.Y });
            channelZ.Keyframes.Add(new Keyframe { Time = kf.Time, Value = kf.Value.Z, InTangent = kf.InSlope.Z, OutTangent = kf.OutSlope.Z });
            channelW.Keyframes.Add(new Keyframe { Time = kf.Time, Value = kf.Value.W, InTangent = kf.InSlope.W, OutTangent = kf.OutSlope.W });
        }

        if (channelX.Keyframes.Count > 0) animData.Channels.Add(channelX);
        if (channelY.Keyframes.Count > 0) animData.Channels.Add(channelY);
        if (channelZ.Keyframes.Count > 0) animData.Channels.Add(channelZ);
        if (channelW.Keyframes.Count > 0) animData.Channels.Add(channelW);
    }

    private static int ResolveBoneIndex(string path, Dictionary<string, int> pathToIndex)
    {
        if (string.IsNullOrEmpty(path)) return -1;
        return pathToIndex.TryGetValue(path, out int index) ? index : -1;
    }

    private static string NormalizeAnimationName(string rawName)
    {
        if (string.IsNullOrWhiteSpace(rawName)) return "animation";
        var name = rawName.Trim();

        var lastPipeIndex = name.LastIndexOf('|');
        if (lastPipeIndex >= 0)
        {
            var afterPipe = name[(lastPipeIndex + 1)..].Trim();
            name = !string.IsNullOrEmpty(afterPipe) ? afterPipe : name[..lastPipeIndex].Trim();
        }

        if (name.StartsWith("Upg_", StringComparison.OrdinalIgnoreCase)) name = name[4..];

        var i = 0;
        while (i < name.Length && char.IsDigit(name[i])) i++;
        if (i > 0 && i < name.Length)
        {
            var j = i;
            while (j < name.Length && (name[j] == '.' || name[j] == '_' || name[j] == '-' || name[j] == ' ')) j++;
            if (j > i) name = name[j..];
        }

        var sb = new System.Text.StringBuilder(name.Length);
        var lastWasUnderscore = false;
        foreach (var ch in name)
        {
            if (char.IsLetterOrDigit(ch)) { sb.Append(char.ToLowerInvariant(ch)); lastWasUnderscore = false; }
            else if (!lastWasUnderscore) { sb.Append('_'); lastWasUnderscore = true; }
        }

        var normalized = sb.ToString().Trim('_');
        return normalized.Length > 0 ? normalized : "animation";
    }

    private static string MakeUniqueName(string baseName, HashSet<string> usedNames)
    {
        var name = string.IsNullOrWhiteSpace(baseName) ? "animation" : baseName;
        if (usedNames.Add(name)) return name;

        var suffix = 2;
        while (true)
        {
            var candidate = $"{name}_{suffix}";
            if (usedNames.Add(candidate)) return candidate;
            suffix++;
        }
    }
}
