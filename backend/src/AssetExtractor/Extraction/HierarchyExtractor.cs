#nullable enable
using AssetExtractor.Models;
using AssetExtractor.Extraction.Interfaces;

namespace AssetExtractor.Extraction;

public class HierarchyExtractor : IHierarchyExtractor
{
    public HierarchyNode? BuildHierarchyNode(UnityReader.GameObject gameObject)
    {
        var node = new HierarchyNode
        {
            Name = gameObject.Name,
            PathID = gameObject.PathId,
            SourceFile = gameObject.SourceFile,
            IsActive = gameObject.IsActive,
            HasAnimator = gameObject.TryGetComponent<UnityReader.Animator>(out _)
        };

        var transform = gameObject.Transform;
        if (transform == null)
        {
            return node;
        }

        var localPos = transform.LocalPosition;
        var localRot = transform.LocalRotation;
        var localScale = transform.LocalScale;

        node.LocalTransform = new Models.Transform
        {
            Position = new Models.Vector3(localPos.X, localPos.Y, localPos.Z),
            Rotation = new Models.Quaternion(localRot.X, localRot.Y, localRot.Z, localRot.W),
            Scale = new Models.Vector3(localScale.X, localScale.Y, localScale.Z)
        };

        foreach (var childTransform in transform.Children)
        {
            if (childTransform == null) continue;
            var childGO = childTransform.GameObject;
            if (childGO == null) continue;

            var childNode = BuildHierarchyNode(childGO);
            if (childNode != null)
            {
                childNode.Parent = node;
                node.Children.Add(childNode);
            }
        }

        return node;
    }

    public (HierarchyNode? wrapper, HierarchyNode? inner) SelectWrapperAndInnerNodes(
        HierarchyNode prefabRoot,
        string unitName)
    {
        foreach (var child in prefabRoot.Children)
        {
            if (IsWrapperNode(child))
            {
                var innerCandidates = new[] { child }.Concat(child.Children)
                    .Where(n => n.IsActive && n.HasAnimator && !IsVFXNode(n.Name))
                    .ToList();

                if (innerCandidates.Count == 0) continue;

                if (innerCandidates.Count == 1)
                    return (child, innerCandidates[0]);

                HierarchyNode? best = null;
                int bestScore = -1;
                foreach (var candidate in innerCandidates)
                {
                    int score = ScoreInnerCandidate(candidate, unitName);
                    if (score > bestScore)
                    {
                        best = candidate;
                        bestScore = score;
                    }
                }

                if (best != null)
                    return (child, best);
            }
        }

        var candidates = new List<HierarchyNode>();
        SearchForAnimator(prefabRoot, candidates);

        if (candidates.Count > 0)
        {
            HierarchyNode? foundInner = null;
            int bestScore = -1;
            foreach (var candidate in candidates)
            {
                int score = ScoreInnerCandidate(candidate, unitName);
                if (score > bestScore)
                {
                    foundInner = candidate;
                    bestScore = score;
                }
            }

            if (foundInner != null)
            {
                var wrapper = FindWrapperAncestor(foundInner, prefabRoot) ?? foundInner.Parent ?? prefabRoot;
                return (wrapper, foundInner);
            }
        }

        return (null, null);
    }

    public (HierarchyNode? wrapper, HierarchyNode? inner) SelectWrapperAndInnerNodesForMapObject(
        HierarchyNode root)
    {
        return (root, root);
    }

    private static HierarchyNode? FindWrapperAncestor(HierarchyNode inner, HierarchyNode prefabRoot)
    {
        var current = inner.Parent;
        while (current != null && current != prefabRoot)
        {
            if (IsWrapperNode(current)) return current;
            current = current.Parent;
        }
        return null;
    }

    private void SearchForAnimator(HierarchyNode node, List<HierarchyNode> candidates)
    {
        if (!node.IsActive) return;
        if (node.HasAnimator && !IsVFXNode(node.Name))
        {
            candidates.Add(node);
        }
        foreach (var child in node.Children)
        {
            SearchForAnimator(child, candidates);
        }
    }

    private static int ScoreInnerCandidate(HierarchyNode candidate, string unitName)
    {
        int score = 0;
        var candidateName = candidate.Name ?? "";
        var candidateLower = candidateName.ToLowerInvariant();
        var unitLower = unitName.ToLowerInvariant();

        if (candidateLower == unitLower) score += 10000;
        else if (candidateLower.StartsWith(unitLower)) score += 5000;
        else if (candidateLower.Contains(unitLower)) score += 1000;

        if (candidateName.Contains("_upg") || candidateName.Contains("_alt"))
        {
            if (!unitName.Contains("_upg") && !unitName.Contains("_alt"))
                score -= 2000;
        }

        return score;
    }

    private static bool IsWrapperNode(HierarchyNode node)
    {
        if (Utilities.WrapperHeuristics.IsWrapperRotation(node.LocalTransform.Rotation))
            return true;

        if (node.Name?.Contains("scale_roll", StringComparison.OrdinalIgnoreCase) == true)
            return true;

        return false;
    }

    public static bool IsScaleWrapperNode(HierarchyNode node)
    {
        return node.Name?.Contains("scale_roll", StringComparison.OrdinalIgnoreCase) == true ||
               node.Name?.Contains("scale_wrapper", StringComparison.OrdinalIgnoreCase) == true ||
               node.Name?.EndsWith("_roll", StringComparison.OrdinalIgnoreCase) == true;
    }

    public static bool IsVFXNode(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        name = name.Trim();
        var lower = name.ToLowerInvariant();

        if (lower.StartsWith("fx_") || lower.StartsWith("vfx_") ||
            lower.StartsWith("global_") || lower.StartsWith("slash"))
            return true;

        if (lower.Contains("fx_") || lower.Contains("_hit_") ||
            lower.Contains("_receiver") || lower.Contains("_emitter") ||
            lower.Contains("_impact_") || lower.Contains("_ability_") ||
            lower.Contains("_wave"))
            return true;

        if (lower.EndsWith("_vfx") || lower.EndsWith("_wave") || lower.EndsWith("_hit_root"))
            return true;

        if (lower == "fx_empty" || lower == "transform")
            return true;

        return false;
    }

    public static bool IsTerrainBlendNode(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        var lower = name.Trim().ToLowerInvariant();
        return lower.StartsWith("ground_") && char.IsDigit(lower[^1]);
    }
}
