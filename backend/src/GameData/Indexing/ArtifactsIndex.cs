using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace GameData.Indexing;

public sealed class ArtifactsIndex
{
    public sealed record ArtifactRecord(
        string Id,
        string NameSid,
        string DescSid,
        string Rarity,
        string Slot,
        string Icon,
        string ItemSetId,
        string NarrativeDescSid,
        string UpgradeDescSid,
        int MaxLevel,
        int CostBase,
        int CostPerLevel,
        bool IsSpecialItem
    );

    private readonly Dictionary<string, ArtifactRecord> _artifacts = new();
    public IReadOnlyDictionary<string, ArtifactRecord> Artifacts => _artifacts;

    public IReadOnlyList<ArtifactRecord> GetByRarity(string rarity)
    {
        if (string.IsNullOrWhiteSpace(rarity))
            return Array.Empty<ArtifactRecord>();

        return _artifacts.Values
            .Where(a => a.Rarity.Equals(rarity, StringComparison.OrdinalIgnoreCase))
            .OrderBy(a => a.Id)
            .ToList();
    }

    private static string NormalizeIcon(string artifactId, string icon)
    {
        // Scroll artifact IDs don't match their icon asset names
        if (artifactId.StartsWith("mythic_magic_scroll_artifact", StringComparison.OrdinalIgnoreCase))
            return "mythic_scroll_box_artifact";
        if (artifactId.StartsWith("enchanted_magic_scroll_artifact", StringComparison.OrdinalIgnoreCase))
            return "enchanted_magic_scroll_artifact";

        return icon;
    }

    public void Scan(string streamingAssetsRoot)
    {
        _artifacts.Clear();
        var zipPath = Path.Combine(streamingAssetsRoot, "Core.zip");
        if (!File.Exists(zipPath)) return;

        using var zip = ZipFile.OpenRead(zipPath);
        var entries = zip.Entries.Where(e => e.FullName.StartsWith("DB/items/items/", StringComparison.OrdinalIgnoreCase) && e.FullName.EndsWith(".json", StringComparison.OrdinalIgnoreCase) && e.Length > 0);

        foreach (var entry in entries)
        {
            try
            {
                using var stream = entry.Open();
                using var reader = new StreamReader(stream, Encoding.UTF8);
                using var doc = JsonDocument.Parse(reader.ReadToEnd());
                if (!doc.RootElement.TryGetProperty("array", out var array)) continue;

                foreach (var el in array.EnumerateArray())
                {
                    var id = el.TryGetProperty("id", out var idP) ? idP.GetString() ?? "" : "";

                    if (id.StartsWith("campaign_", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var name = el.TryGetProperty("name", out var nameP) ? nameP.GetString() ?? "" : "";
                    var desc = el.TryGetProperty("description", out var descP) ? descP.GetString() ?? "" : "";
                    var rarity = el.TryGetProperty("rarity", out var rP) ? rP.GetString() ?? "" : "";
                    var slot = el.TryGetProperty("slot_", out var sP) ? sP.GetString() ?? "" : "";
                    var icon = el.TryGetProperty("icon", out var iconProp) ? iconProp.GetString() ?? "" : "";
                    var itemSetId = el.TryGetProperty("itemSet", out var setP) ? setP.GetString() ?? "" : "";
                    var narrativeDescSid = el.TryGetProperty("narrativeDescription", out var narP) ? narP.GetString() ?? "" : "";
                    var upgradeDescSid = el.TryGetProperty("upgradeDescription", out var upgP) ? upgP.GetString() ?? "" : "";
                    var maxLevel = el.TryGetProperty("maxLevel", out var maxLvlP) && maxLvlP.TryGetInt32(out var maxLvl) ? maxLvl : 0;
                    var costBase = el.TryGetProperty("costBase", out var costBaseP) && costBaseP.TryGetInt32(out var cBase) ? cBase : 0;
                    var costPerLevel = el.TryGetProperty("costPerLevel", out var costPerLvlP) && costPerLvlP.TryGetInt32(out var cPerLvl) ? cPerLvl : 0;
                    var isSpecialItem = el.TryGetProperty("isSpecialItem", out var specialP) && specialP.GetBoolean();
                    icon = NormalizeIcon(id, icon);

                    if (!string.IsNullOrWhiteSpace(id) && !_artifacts.ContainsKey(id))
                        _artifacts[id] = new ArtifactRecord(id, name, desc, rarity, slot, icon, itemSetId, narrativeDescSid, upgradeDescSid, maxLevel, costBase, costPerLevel, isSpecialItem);
                }
            }
            catch { }
        }
    }
}
