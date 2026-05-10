using System.Collections.Generic;
using System.IO.Hashing;
using System.Text;

namespace UnityReader.Internal;

// Reverse-lookup from Unity's CRC32 binding-path hash back to the slash-separated
// transform path. Per-Animator: identical hashes resolve to different paths on
// different skeletons. Filled from AnimatorController.m_TOS (cheap, pre-baked) plus
// a BFS over the Animator's transforms hashing each path with CRC-32/ISO-HDLC
// (polynomial 0xEDB88320, matching Unity's binding hash) to catch anything m_TOS misses.
internal sealed class PathChecksumCache
{
    private readonly Dictionary<uint, string> _byHash;

    private PathChecksumCache(Dictionary<uint, string> byHash) => _byHash = byHash;

    public bool TryResolve(uint hash, out string path)
    {
        if (hash == 0)
        {
            path = string.Empty;
            return true;
        }
        return _byHash.TryGetValue(hash, out path!);
    }

    public static PathChecksumCache BuildForAnimator(Animator animator)
    {
        var table = new Dictionary<uint, string>
        {
            [0u] = string.Empty,
        };

        var ctrlVendor = animator.Vendor.m_Controller;
        if (ctrlVendor != null && ctrlVendor.TryGet(out var rt))
        {
            var concreteCtrl = ResolveBaseController(rt);
            if (concreteCtrl?.m_TOS is { } tos)
                foreach (var kv in tos)
                    table[kv.Key] = kv.Value ?? string.Empty;
        }

        // The Animator's own root maps to hash 0 (seeded above), so start from its
        // direct children with an empty path prefix.
        var rootGo = animator.GameObject;
        if (rootGo?.Transform is { } rootT)
        {
            foreach (var child in rootT.Children)
                if (child is not null)
                    WalkAndHash(child, parentPath: string.Empty, table);
        }

        return new PathChecksumCache(table);
    }

    private static AssetStudio.AnimatorController? ResolveBaseController(AssetStudio.RuntimeAnimatorController rt)
    {
        // Bounded depth so a malformed override cycle cannot trap us.
        for (var step = 0; step < 8; step++)
        {
            switch (rt)
            {
                case AssetStudio.AnimatorController ac:
                    return ac;
                case AssetStudio.AnimatorOverrideController aoc:
                    if (aoc.m_Controller == null || !aoc.m_Controller.TryGet(out var inner)) return null;
                    rt = inner;
                    break;
                default:
                    return null;
            }
        }
        return null;
    }

    private static void WalkAndHash(Transform node, string parentPath, Dictionary<uint, string> sink)
    {
        var nodeName = node.GameObject?.Name ?? string.Empty;
        var here = parentPath.Length == 0 ? nodeName : parentPath + "/" + nodeName;

        var hash = Crc32.HashToUInt32(Encoding.UTF8.GetBytes(here));
        sink.TryAdd(hash, here);

        foreach (var child in node.Children)
            if (child is not null)
                WalkAndHash(child, here, sink);
    }
}
