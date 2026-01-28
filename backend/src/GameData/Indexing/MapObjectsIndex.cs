using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using GameData.Shared.Utils;

namespace GameData.Indexing;

#region Bank Data Classes

public sealed class GuardUnit
{
    public required string Sid { get; init; }
    public int Amount { get; init; }
}

public sealed class Reward
{
    public required string RewardType { get; init; }
    public required string RewardShowType { get; init; }
    public bool ApplyRewardFloating { get; init; }
    public required string RewardIcon { get; init; }
    public required string RewardName { get; init; }
    public required string RewardDesc { get; init; }
    public required string RewardNotificationDesc { get; init; }
    public required List<string> Parameters { get; init; }
}

public sealed class RewardSet
{
    public required string RewardSetShowType { get; init; }
    public required string RewardSetApplyType { get; init; }
    public required string RewardSetCancelType { get; init; }
    public required List<Reward> Rewards { get; init; }
}

public sealed class BankVariant
{
    public int RollChance { get; init; }
    public int Value { get; init; }
    public int? CustomGuardValue { get; init; }
    public required List<GuardUnit> GuardUnits { get; init; }
    public required RewardSet RewardSet { get; init; }
}

public sealed class BankData
{
    public bool ApplyDifficultyModifier { get; init; }
    public required string VariantRerollType { get; init; }
    public required string VisitorsResetType { get; init; }
    public required string VisitType { get; init; }
    public required string SourceFolder { get; init; }
    public required List<BankVariant> Variants { get; init; }
}

#endregion

public sealed class MapObjectsIndex
{
    public sealed record MapObjectRecord(
        string Id,
        string Tag,
        bool IsInteractable,
        int SizeX,
        int SizeZ,
        string PrefabPath,
        string SourceFile,
        string NameSid,
        string DescriptionSid,
        string NarrativeDescriptionSid,
        BankData? BankData = null
    );

    private readonly Dictionary<string, MapObjectRecord> _mapObjects = new();
    public IReadOnlyDictionary<string, MapObjectRecord> MapObjects => _mapObjects;

    public void Scan(string streamingAssetsRoot)
    {
        _mapObjects.Clear();
        var zipPath = Path.Combine(streamingAssetsRoot, "Core.zip");
        if (!File.Exists(zipPath)) return;

        using var zip = ZipFile.OpenRead(zipPath);

        ScanMapObjects(zip);
        ScanAndMergeBankData(zip);

        DiagnosticsLog.Trace($"[MapObjectsIndex] Loaded {_mapObjects.Count} map objects");
    }

    private void ScanMapObjects(ZipArchive zip)
    {
        var entries = zip.Entries.Where(e =>
            e.FullName.StartsWith("DB/map/objects/", StringComparison.OrdinalIgnoreCase) &&
            e.FullName.EndsWith(".json", StringComparison.OrdinalIgnoreCase) &&
            (e.FullName.EndsWith("3_resources.json", StringComparison.OrdinalIgnoreCase) ||
             e.FullName.EndsWith("4_interactables.json", StringComparison.OrdinalIgnoreCase) ||
             e.FullName.EndsWith("6_artifacts.json", StringComparison.OrdinalIgnoreCase)) &&
            e.Length > 0).ToList();

        foreach (var entry in entries)
        {
            try
            {
                ProcessMapObjectEntry(entry);
            }
            catch (Exception ex)
            {
                DiagnosticsLog.Trace($"[MapObjectsIndex] Error processing {entry.FullName}", ex);
            }
        }
    }

    private static List<(string Path, string SourceFolder)> DiscoverFolders(ZipArchive zip, string basePath)
    {
        var result = new List<(string Path, string SourceFolder)>();

        var subfolders = zip.Entries
            .Where(e => e.FullName.StartsWith(basePath, StringComparison.OrdinalIgnoreCase))
            .Select(e => e.FullName.Substring(basePath.Length))
            .Where(relative => relative.Contains('/'))
            .Select(relative => relative.Split('/')[0])
            .Distinct()
            .OrderBy(f => f)
            .ToList();

        foreach (var folder in subfolders)
        {
            var folderPath = $"{basePath}{folder}/";

            // event_banks subfolders take priority for sourceFolder naming
            if (folder.Equals("event_banks", StringComparison.OrdinalIgnoreCase))
            {
                var eventBankSubfolders = DiscoverFolders(zip, folderPath);
                result.AddRange(eventBankSubfolders);
            }

            result.Add((folderPath, folder));
        }

        return result;
    }

    private void ScanAndMergeBankData(ZipArchive zip)
    {
        // Dynamically discover all folders under DB/objects_logic/
        var objectsLogicFolders = DiscoverFolders(zip, "DB/objects_logic/");

        foreach (var (path, sourceFolder) in objectsLogicFolders)
        {
            var entries = zip.Entries.Where(e =>
                e.FullName.StartsWith(path, StringComparison.OrdinalIgnoreCase) &&
                e.FullName.EndsWith(".json", StringComparison.OrdinalIgnoreCase) &&
                !e.FullName.Substring(path.Length).Contains('/') && // Only direct children, not nested
                e.Length > 0);

            foreach (var entry in entries)
            {
                try
                {
                    ProcessBankEntry(entry, sourceFolder);
                }
                catch (Exception ex)
                {
                    DiagnosticsLog.Trace($"[MapObjectsIndex] Error processing bank {entry.FullName}", ex);
                }
            }
        }

    }

    private void ProcessMapObjectEntry(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        using var reader = new StreamReader(stream, Encoding.UTF8);
        using var doc = JsonDocument.Parse(reader.ReadToEnd());

        if (!doc.RootElement.TryGetProperty("array", out var array))
            return;

        foreach (var el in array.EnumerateArray())
        {
            var id = el.TryGetProperty("id", out var idP) ? idP.GetString() ?? "" : "";
            if (string.IsNullOrWhiteSpace(id)) continue;

            if (id.EndsWith("_city", StringComparison.OrdinalIgnoreCase))
                continue;

            if (id.Equals("portal_magic", StringComparison.OrdinalIgnoreCase))
                continue;

            var lowerId = id.ToLowerInvariant();
            if (lowerId.StartsWith("custom_", StringComparison.Ordinal) ||
                lowerId.StartsWith("campaign_", StringComparison.Ordinal) ||
                lowerId.EndsWith("_campaign", StringComparison.Ordinal) ||
                lowerId.StartsWith("pvp_promo_", StringComparison.Ordinal))
                continue;

            var tag = el.TryGetProperty("tag", out var tagP) ? tagP.GetString() ?? "" : "";

            if (id.StartsWith("mine_", StringComparison.OrdinalIgnoreCase))
            {
                tag = "Mine";
            }

            var isInteractable = el.TryGetProperty("isInteractable", out var interP) && interP.ValueKind == JsonValueKind.True;

            var sizeX = 0;
            if (el.TryGetProperty("sizeX", out var sxP) && sxP.ValueKind == JsonValueKind.Number)
                sizeX = sxP.GetInt32();

            var sizeZ = 0;
            if (el.TryGetProperty("sizeZ", out var szP) && szP.ValueKind == JsonValueKind.Number)
                sizeZ = szP.GetInt32();

            var prefabPath = "";
            if (el.TryGetProperty("prefs", out var prefsP) && prefsP.ValueKind == JsonValueKind.Array)
            {
                prefabPath = prefsP.EnumerateArray()
                    .Select(p => p.GetString() ?? "")
                    .FirstOrDefault(p => !string.IsNullOrWhiteSpace(p)) ?? "";
            }

            if (!string.IsNullOrEmpty(prefabPath) && prefabPath.Contains("debug_objects/", StringComparison.OrdinalIgnoreCase))
                continue;

            if (!string.IsNullOrEmpty(prefabPath))
            {
                var lowerPath = prefabPath.ToLowerInvariant().Replace('\\', '/');
                if (!lowerPath.StartsWith("interactive/") &&
                    !lowerPath.StartsWith("resource/") &&
                    !lowerPath.StartsWith("barracks/") &&
                    !lowerPath.StartsWith("artifact/"))
                    continue;
            }

            var nameSid = el.TryGetProperty("name", out var nameP) ? nameP.GetString() ?? "" : "";
            var descriptionSid = el.TryGetProperty("description", out var descP) ? descP.GetString() ?? "" : "";
            var narrativeDescSid = el.TryGetProperty("narrativeDescription", out var narrativeP) ? narrativeP.GetString() ?? "" : "";

            if (!_mapObjects.ContainsKey(id))
            {
                _mapObjects[id] = new MapObjectRecord(
                    id,
                    tag,
                    isInteractable,
                    sizeX,
                    sizeZ,
                    prefabPath,
                    Path.GetFileName(entry.FullName),
                    nameSid,
                    descriptionSid,
                    narrativeDescSid
                );
            }
        }
    }

    private void ProcessBankEntry(ZipArchiveEntry entry, string sourceFolder)
    {
        using var stream = entry.Open();
        using var reader = new StreamReader(stream, Encoding.UTF8);
        using var doc = JsonDocument.Parse(reader.ReadToEnd());

        if (!doc.RootElement.TryGetProperty("array", out var array))
            return;

        foreach (var el in array.EnumerateArray())
        {
            var id = el.TryGetProperty("id", out var idP) ? idP.GetString() ?? "" : "";
            if (string.IsNullOrWhiteSpace(id)) continue;

            if (id.Contains("campaign", StringComparison.OrdinalIgnoreCase) ||
                id.StartsWith("custom_", StringComparison.OrdinalIgnoreCase))
                continue;

            var bankData = ParseBankData(el, sourceFolder);
            if (bankData == null) continue;

            MergeBankDataToMapObject(id, bankData, sourceFolder);
        }
    }

    private void MergeBankDataToMapObject(string id, BankData bankData, string sourceFolder)
    {
        var tag = sourceFolder;

        if (_mapObjects.TryGetValue(id, out var existing))
        {
            _mapObjects[id] = existing with { BankData = bankData, Tag = tag };
        }
        else
        {
            // Bank exists but no corresponding map object - create a minimal record
            _mapObjects[id] = new MapObjectRecord(
                id,
                tag,
                true,
                1,
                1,
                "",
                "",
                "",
                "",
                "",
                bankData
            );
        }
    }

    private BankData? ParseBankData(JsonElement el, string sourceFolder)
    {
        var applyDifficultyModifier = el.TryGetProperty("applyDifficultyModifier", out var diffP) &&
                                      diffP.ValueKind == JsonValueKind.True;

        var variantRerollType = el.TryGetProperty("variantRerollType", out var rerollP)
            ? rerollP.GetString() ?? ""
            : "";

        var visitorsResetType = el.TryGetProperty("visitorsResetType", out var resetP)
            ? resetP.GetString() ?? ""
            : "";

        var visitType = el.TryGetProperty("visitType", out var visitP)
            ? visitP.GetString() ?? ""
            : "";

        var variants = new List<BankVariant>();
        if (el.TryGetProperty("variants", out var variantsP) && variantsP.ValueKind == JsonValueKind.Array)
        {
            foreach (var variantEl in variantsP.EnumerateArray())
            {
                var variant = ParseBankVariant(variantEl);
                if (variant != null)
                    variants.Add(variant);
            }
        }

        // Handle files without variants but with root-level guardUnits (e.g., barracks, outposts)
        if (variants.Count == 0)
        {
            var rootGuardUnits = ParseGuardUnits(el);
            // Create a single variant from root-level data
            variants.Add(new BankVariant
            {
                RollChance = 100,
                Value = 0,
                CustomGuardValue = null,
                GuardUnits = rootGuardUnits,
                RewardSet = new RewardSet
                {
                    RewardSetShowType = "",
                    RewardSetApplyType = "",
                    RewardSetCancelType = "",
                    Rewards = new List<Reward>()
                }
            });
        }

        return new BankData
        {
            ApplyDifficultyModifier = applyDifficultyModifier,
            VariantRerollType = variantRerollType,
            VisitorsResetType = visitorsResetType,
            VisitType = visitType,
            SourceFolder = sourceFolder,
            Variants = variants
        };
    }

    private BankVariant? ParseBankVariant(JsonElement el)
    {
        var rollChance = el.TryGetProperty("rollChance", out var chanceP) && chanceP.ValueKind == JsonValueKind.Number
            ? chanceP.GetInt32()
            : 0;

        var value = el.TryGetProperty("value", out var valueP) && valueP.ValueKind == JsonValueKind.Number
            ? valueP.GetInt32()
            : 0;

        int? customGuardValue = null;
        if (el.TryGetProperty("customGuardValue", out var guardValP) && guardValP.ValueKind == JsonValueKind.Number)
            customGuardValue = guardValP.GetInt32();

        var guardUnits = ParseGuardUnits(el);

        RewardSet? rewardSet = null;
        if (el.TryGetProperty("rewardSet", out var rewardSetP))
        {
            rewardSet = ParseRewardSet(rewardSetP);
        }

        if (rewardSet == null)
            return null;

        return new BankVariant
        {
            RollChance = rollChance,
            Value = value,
            CustomGuardValue = customGuardValue,
            GuardUnits = guardUnits,
            RewardSet = rewardSet
        };
    }

    private List<GuardUnit> ParseGuardUnits(JsonElement el)
    {
        var guardUnits = new List<GuardUnit>();
        if (el.TryGetProperty("guardUnits", out var guardsP) && guardsP.ValueKind == JsonValueKind.Array)
        {
            foreach (var guardEl in guardsP.EnumerateArray())
            {
                var sid = guardEl.TryGetProperty("sid", out var sidP) ? sidP.GetString() ?? "" : "";
                var amount = guardEl.TryGetProperty("amount", out var amtP) && amtP.ValueKind == JsonValueKind.Number
                    ? amtP.GetInt32()
                    : 0;

                if (!string.IsNullOrEmpty(sid))
                {
                    guardUnits.Add(new GuardUnit { Sid = sid, Amount = amount });
                }
            }
        }
        return guardUnits;
    }

    private RewardSet? ParseRewardSet(JsonElement el)
    {
        var showType = el.TryGetProperty("rewardSetShowType", out var showP) ? showP.GetString() ?? "" : "";
        var applyType = el.TryGetProperty("rewardSetApplyType", out var applyP) ? applyP.GetString() ?? "" : "";
        var cancelType = el.TryGetProperty("rewardSetCancelType", out var cancelP) ? cancelP.GetString() ?? "" : "";

        var rewards = new List<Reward>();
        if (el.TryGetProperty("rewards", out var rewardsP) && rewardsP.ValueKind == JsonValueKind.Array)
        {
            foreach (var rewardEl in rewardsP.EnumerateArray())
            {
                var reward = ParseReward(rewardEl);
                if (reward != null)
                    rewards.Add(reward);
            }
        }

        return new RewardSet
        {
            RewardSetShowType = showType,
            RewardSetApplyType = applyType,
            RewardSetCancelType = cancelType,
            Rewards = rewards
        };
    }

    private Reward? ParseReward(JsonElement el)
    {
        var rewardType = el.TryGetProperty("rewardType", out var typeP) ? typeP.GetString() ?? "" : "";
        if (string.IsNullOrEmpty(rewardType))
            return null;

        var showType = el.TryGetProperty("rewardShowType", out var showP) ? showP.GetString() ?? "" : "";
        var applyFloating = el.TryGetProperty("applyRewardFloating", out var floatP) &&
                            floatP.ValueKind == JsonValueKind.True;

        var icon = el.TryGetProperty("rewardIcon", out var iconP) ? iconP.GetString() ?? "" : "";
        var name = el.TryGetProperty("rewardName", out var nameP) ? nameP.GetString() ?? "" : "";
        var desc = el.TryGetProperty("rewardDesc", out var descP) ? descP.GetString() ?? "" : "";
        var notifDesc = el.TryGetProperty("rewardNotificationDesc", out var notifP) ? notifP.GetString() ?? "" : "";

        var parameters = new List<string>();
        if (el.TryGetProperty("parameters", out var paramsP) && paramsP.ValueKind == JsonValueKind.Array)
        {
            foreach (var paramEl in paramsP.EnumerateArray())
            {
                var paramValue = paramEl.GetString() ?? "";
                parameters.Add(paramValue);
            }
        }

        return new Reward
        {
            RewardType = rewardType,
            RewardShowType = showType,
            ApplyRewardFloating = applyFloating,
            RewardIcon = icon,
            RewardName = name,
            RewardDesc = desc,
            RewardNotificationDesc = notifDesc,
            Parameters = parameters
        };
    }
}
