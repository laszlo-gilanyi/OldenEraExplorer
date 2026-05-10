#nullable enable
using System.IO.Hashing;
using System.Text;
using AssetExtractor.Models;
using AssetExtractor.Export.Interfaces;
using AssetExtractor.Extraction;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SharpGLTF.Scenes;
using SharpGLTF.Transforms;

namespace AssetExtractor.Export;

public class NodeHierarchyBuilder : INodeHierarchyBuilder
{
    private readonly ILogger<NodeHierarchyBuilder> _logger;

    public NodeHierarchyBuilder(ILogger<NodeHierarchyBuilder>? logger = null)
    {
        _logger = logger ?? NullLogger<NodeHierarchyBuilder>.Instance;
    }

    // applyGltfForward applies the Unity-to-glTF 90 degree Y rotation; isometricExtra
    // adds another 45 degrees for map objects.
    public Dictionary<string, NodeBuilder> BuildNodeHierarchy(
        SceneBuilder scene,
        UnitData unitData,
        HashSet<string> usedNodeNames,
        bool applyGltfForward = true,
        bool isometricExtra = false)
    {
        var nodeBuilders = new Dictionary<string, NodeBuilder>(StringComparer.Ordinal);

        string? rootKey = null;
        string? wrapperKey = null;
        string? innerKey = null;

        if (unitData.RootNode != null)
        {
            rootKey = GetGameObjectKey(unitData.RootNode);

            string rootBaseName = unitData.RootNode.Name;
            if (unitData.InnerNode != null && string.Equals(rootBaseName, unitData.InnerNode.Name, StringComparison.Ordinal))
            {
                rootBaseName = $"{rootBaseName}_root";
            }

            string rootName = EnsureUniqueNodeName(rootBaseName, $"go{unitData.RootNode.PathID}", usedNodeNames);
            var rootBuilder = new NodeBuilder(rootName);

            if (unitData.LookPointPosition != null)
            {
                SetNodeTransformWithLookPointCorrection(rootBuilder, unitData.RootNode.LocalTransform, unitData.LookPointPosition);
            }
            else if (applyGltfForward && isometricExtra)
            {
                SetNodeTransformWithIsometricForward(rootBuilder, unitData.RootNode.LocalTransform);
            }
            else if (applyGltfForward)
            {
                SetNodeTransformWithGltfForward(rootBuilder, unitData.RootNode.LocalTransform);
            }
            else
            {
                SetNodeTransform(rootBuilder, unitData.RootNode.LocalTransform);
            }
            nodeBuilders[rootKey] = rootBuilder;
            scene.AddNode(rootBuilder);
        }

        if (unitData.WrapperNode != null && unitData.RootNode != null)
        {
            wrapperKey = GetGameObjectKey(unitData.WrapperNode);

            if (rootKey != null && string.Equals(wrapperKey, rootKey, StringComparison.Ordinal))
            {
                wrapperKey = rootKey;
            }
            else
            {
                string wrapperName = EnsureUniqueNodeName(unitData.WrapperNode.Name, $"go{unitData.WrapperNode.PathID}", usedNodeNames);
                var wrapperBuilder = new NodeBuilder(wrapperName);

                // Normalize scale_roll magnitude to plus/minus one so the viewer can
                // apply the JSON value cleanly; preserve sign because negative scale
                // encodes mirroring.
                var wrapperTransform = unitData.WrapperNode.LocalTransform;
                if (HierarchyExtractor.IsScaleWrapperNode(unitData.WrapperNode))
                {
                    var originalScale = wrapperTransform.Scale;

                    var normalizedScale = new Models.Vector3(
                        originalScale.X < 0 ? -1f : 1f,
                        originalScale.Y < 0 ? -1f : 1f,
                        originalScale.Z < 0 ? -1f : 1f
                    );

                    wrapperTransform = new Models.Transform
                    {
                        Position = wrapperTransform.Position,
                        Rotation = wrapperTransform.Rotation,
                        Scale = normalizedScale
                    };

                    bool hasNegative = originalScale.X < 0 || originalScale.Y < 0 || originalScale.Z < 0;
                    if (hasNegative)
                    {
                        _logger.LogInformation("Preserved negative scale for mirroring on '{WrapperNodeName}': ({NormalizedX:F0}, {NormalizedY:F0}, {NormalizedZ:F0})",
                            unitData.WrapperNode.Name, normalizedScale.X, normalizedScale.Y, normalizedScale.Z);
                    }
                    _logger.LogInformation("Normalized scale from wrapper node '{WrapperNodeName}' (original: {OriginalX:F2}, {OriginalY:F2}, {OriginalZ:F2})",
                        unitData.WrapperNode.Name, originalScale.X, originalScale.Y, originalScale.Z);
                }

                SetNodeTransform(wrapperBuilder, wrapperTransform);
                nodeBuilders[wrapperKey] = wrapperBuilder;

                if (rootKey != null && nodeBuilders.TryGetValue(rootKey, out var rootBuilder))
                {
                    rootBuilder.AddNode(wrapperBuilder);
                }
            }
        }

        if (unitData.InnerNode != null && unitData.WrapperNode != null)
        {
            innerKey = GetGameObjectKey(unitData.InnerNode);

            if (wrapperKey != null && string.Equals(innerKey, wrapperKey, StringComparison.Ordinal))
            {
                return nodeBuilders;
            }

            string innerName = EnsureUniqueNodeName(unitData.InnerNode.Name, $"go{unitData.InnerNode.PathID}", usedNodeNames);
            var innerBuilder = new NodeBuilder(innerName);
            SetNodeTransform(innerBuilder, unitData.InnerNode.LocalTransform);
            nodeBuilders[innerKey] = innerBuilder;

            if (wrapperKey != null && nodeBuilders.TryGetValue(wrapperKey, out var wrapperBuilder))
            {
                wrapperBuilder.AddNode(innerBuilder);
            }
        }

        return nodeBuilders;
    }

    public void AddSkeleton(
        SceneBuilder scene,
        UnitData unitData,
        Dictionary<string, NodeBuilder> nodeBuilders,
        HashSet<string> usedNodeNames)
    {
        var skeleton = unitData.Skeleton!;
        var boneBuilders = new Dictionary<string, NodeBuilder>(StringComparer.Ordinal);

        foreach (var bone in skeleton.Bones)
        {
            string uniqueToken = $"b{Crc32.HashToUInt32(Encoding.UTF8.GetBytes(bone.Path)):x8}";
            string boneNodeName = EnsureUniqueNodeName(bone.Name, uniqueToken, usedNodeNames);
            var boneBuilder = new NodeBuilder(boneNodeName);
            SetNodeTransform(boneBuilder, bone.LocalTransform);
            boneBuilders[bone.Path] = boneBuilder;
        }

        foreach (var bone in skeleton.Bones)
        {
            var boneBuilder = boneBuilders[bone.Path];

            if (bone.ParentIndex >= 0 && bone.ParentIndex < skeleton.Bones.Count && bone.ParentIndex != bone.Index)
            {
                var parentBone = skeleton.Bones[bone.ParentIndex];
                if (boneBuilders.TryGetValue(parentBone.Path, out var parentBuilder))
                {
                    parentBuilder.AddNode(boneBuilder);
                }
                else
                {
                    _logger.LogWarning("Parent bone not found for '{BonePath}' - attaching to inner node", bone.Path);
                    AttachBoneToInnerOrScene(scene, unitData, nodeBuilders, boneBuilder);
                }
            }
            else
            {
                AttachBoneToInnerOrScene(scene, unitData, nodeBuilders, boneBuilder);
            }
        }

        foreach (var kvp in boneBuilders)
        {
            if (!nodeBuilders.ContainsKey(kvp.Key))
            {
                nodeBuilders[kvp.Key] = kvp.Value;
            }
        }

        // Unity uses an empty path to mean the Animator's own GameObject; route those
        // bindings to the inner node so animation curves resolve.
        if (!nodeBuilders.ContainsKey(""))
        {
            var innerKey = unitData.InnerNode != null ? GetGameObjectKey(unitData.InnerNode) : null;
            if (innerKey != null && nodeBuilders.TryGetValue(innerKey, out var innerBuilder))
            {
                nodeBuilders[""] = innerBuilder;
                _logger.LogDebug("Added empty path mapping to inner node '{InnerNodeName}'", unitData.InnerNode!.Name);
            }
        }
    }

    private void AttachBoneToInnerOrScene(
        SceneBuilder scene,
        UnitData unitData,
        Dictionary<string, NodeBuilder> nodeBuilders,
        NodeBuilder boneBuilder)
    {
        var innerKey = unitData.InnerNode != null ? GetGameObjectKey(unitData.InnerNode) : null;
        if (innerKey != null && nodeBuilders.TryGetValue(innerKey, out var innerBuilder))
        {
            innerBuilder.AddNode(boneBuilder);
        }
        else
        {
            scene.AddNode(boneBuilder);
        }
    }

    private static string GetGameObjectKey(HierarchyNode node) => $"go:{node.PathID}";

    private static string EnsureUniqueNodeName(string baseName, string uniqueToken, HashSet<string> usedNames)
    {
        if (string.IsNullOrEmpty(baseName))
        {
            baseName = "Unnamed";
        }

        if (usedNames.Add(baseName))
        {
            return baseName;
        }

        string candidate = $"{baseName}__{uniqueToken}";
        if (usedNames.Add(candidate))
        {
            return candidate;
        }

        int counter = 2;
        while (true)
        {
            string numbered = $"{candidate}__{counter}";
            if (usedNames.Add(numbered))
            {
                return numbered;
            }
            counter++;
        }
    }

    private void SetNodeTransform(NodeBuilder node, Models.Transform transform)
    {
        var unityRotation = new System.Numerics.Quaternion(
            transform.Rotation.X, transform.Rotation.Y, transform.Rotation.Z, transform.Rotation.W);
        var unityPosition = new System.Numerics.Vector3(
            transform.Position.X, transform.Position.Y, transform.Position.Z);

        var affine = new AffineTransform(
            new System.Numerics.Vector3(transform.Scale.X, transform.Scale.Y, transform.Scale.Z),
            GlbCoordinateConversion.ToGltfQuaternionConvert(unityRotation),
            GlbCoordinateConversion.ToGltfVector3Convert(unityPosition)
        );

        node.SetLocalTransform(affine, false);
    }

    private void SetNodeTransformWithIsometricForward(NodeBuilder node, Models.Transform transform)
    {
        var unityRotation = new System.Numerics.Quaternion(
            transform.Rotation.X, transform.Rotation.Y, transform.Rotation.Z, transform.Rotation.W);
        var unityPosition = new System.Numerics.Vector3(
            transform.Position.X, transform.Position.Y, transform.Position.Z);

        var convertedRotation = GlbCoordinateConversion.ToGltfQuaternionConvert(unityRotation);
        var convertedPosition = GlbCoordinateConversion.ToGltfVector3Convert(unityPosition);

        const float baseAngle = MathF.PI / 2f;      // 90°
        const float isoAngle = MathF.PI / 4f;       // 45°
        float totalAngle = baseAngle + isoAngle;    // 135°

        float sinHalf = MathF.Sin(totalAngle / 2f);
        float cosHalf = MathF.Cos(totalAngle / 2f);
        var rotationY = new System.Numerics.Quaternion(0, sinHalf, 0, cosHalf);

        var finalRotation = System.Numerics.Quaternion.Normalize(rotationY * convertedRotation);

        float cosAngle = MathF.Cos(totalAngle);
        float sinAngle = MathF.Sin(totalAngle);
        var finalPosition = new System.Numerics.Vector3(
            convertedPosition.X * cosAngle + convertedPosition.Z * sinAngle,
            convertedPosition.Y,
            -convertedPosition.X * sinAngle + convertedPosition.Z * cosAngle
        );

        _logger.LogInformation("Applied 90° + 45° = 135° rotation (isometric map object)");

        var affine = new AffineTransform(
            new System.Numerics.Vector3(transform.Scale.X, transform.Scale.Y, transform.Scale.Z),
            finalRotation,
            finalPosition
        );

        node.SetLocalTransform(affine, false);
    }

    private void SetNodeTransformWithGltfForward(NodeBuilder node, Models.Transform transform)
    {
        var unityRotation = new System.Numerics.Quaternion(
            transform.Rotation.X, transform.Rotation.Y, transform.Rotation.Z, transform.Rotation.W);
        var unityPosition = new System.Numerics.Vector3(
            transform.Position.X, transform.Position.Y, transform.Position.Z);

        var convertedRotation = GlbCoordinateConversion.ToGltfQuaternionConvert(unityRotation);
        var convertedPosition = GlbCoordinateConversion.ToGltfVector3Convert(unityPosition);

        const float sin45 = 0.7071067811865476f;
        const float cos45 = 0.7071067811865476f;
        var rotation90Y = new System.Numerics.Quaternion(0, sin45, 0, cos45);

        var finalRotation = System.Numerics.Quaternion.Normalize(rotation90Y * convertedRotation);

        var finalPosition = new System.Numerics.Vector3(
            convertedPosition.Z, convertedPosition.Y, -convertedPosition.X);

        var affine = new AffineTransform(
            new System.Numerics.Vector3(transform.Scale.X, transform.Scale.Y, transform.Scale.Z),
            finalRotation,
            finalPosition
        );

        node.SetLocalTransform(affine, false);
    }

    // Unity look_point is the hero's position, but the model's entrance faces away from
    // the hero, so we rotate to (180 minus lookAngle) to align with glTF Z+.
    private void SetNodeTransformWithLookPointCorrection(NodeBuilder node, Models.Transform transform, Models.Vector3 lookPoint)
    {
        var unityRotation = new System.Numerics.Quaternion(
            transform.Rotation.X, transform.Rotation.Y, transform.Rotation.Z, transform.Rotation.W);
        var unityPosition = new System.Numerics.Vector3(
            transform.Position.X, transform.Position.Y, transform.Position.Z);

        var convertedRotation = GlbCoordinateConversion.ToGltfQuaternionConvert(unityRotation);
        var convertedPosition = GlbCoordinateConversion.ToGltfVector3Convert(unityPosition);

        float lookAngle = MathF.Atan2(lookPoint.X, -lookPoint.Z);

        float targetAngle = MathF.PI - lookAngle;

        float sinHalf = MathF.Sin(targetAngle / 2f);
        float cosHalf = MathF.Cos(targetAngle / 2f);
        var lookCorrectionRotation = new System.Numerics.Quaternion(0, sinHalf, 0, cosHalf);

        var finalRotation = System.Numerics.Quaternion.Normalize(lookCorrectionRotation * convertedRotation);

        float cosAngle = MathF.Cos(targetAngle);
        float sinAngle = MathF.Sin(targetAngle);
        var finalPosition = new System.Numerics.Vector3(
            convertedPosition.X * cosAngle + convertedPosition.Z * sinAngle,
            convertedPosition.Y,
            -convertedPosition.X * sinAngle + convertedPosition.Z * cosAngle
        );

        _logger.LogInformation("Applied look_point correction: angle={LookAngleDegrees:F1}°, target={TargetAngleDegrees:F1}°",
            lookAngle * 180f / MathF.PI, targetAngle * 180f / MathF.PI);

        var affine = new AffineTransform(
            new System.Numerics.Vector3(transform.Scale.X, transform.Scale.Y, transform.Scale.Z),
            finalRotation,
            finalPosition
        );

        node.SetLocalTransform(affine, false);
    }
}
