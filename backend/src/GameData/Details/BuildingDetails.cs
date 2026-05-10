using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using GameData.Indexing;
using Localization.DbAccess;
using Localization.Indexing;
using Localization.Resolution;

namespace GameData.Details;

public sealed record BuildingCostInfo(string ResourceName, int Amount);

public sealed record BuildingUpgradeInfo(string Sid, string Icon, string Description);

public sealed record BuildingDetails(
    string Name,
    string Icon,
    string Description,
    IReadOnlyList<BuildingCostInfo> Costs,
    IReadOnlyList<BuildingUpgradeInfo> Upgrades,
    string PlainTextSummary
);

public sealed class BuildingDetailsService
{
    private static readonly Dictionary<string, int> ResourceOrder = new()
    {
        ["gold"] = 1,
        ["wood"] = 2,
        ["ore"] = 3,
        ["gemstones"] = 4,
        ["crystals"] = 5,
        ["mercury"] = 6,
        ["dust"] = 7,
        ["graal"] = 8
    };

    private readonly DbAccessor _dbAccessor;
    private readonly ITextResolver _textResolver;
    private readonly ILogger<BuildingDetailsService> _logger;

    public BuildingDetailsService(
        DbAccessor dbAccessor,
        ITextResolver textResolver,
        ILogger<BuildingDetailsService> logger)
    {
        _dbAccessor = dbAccessor ?? throw new ArgumentNullException(nameof(dbAccessor));
        _textResolver = textResolver ?? throw new ArgumentNullException(nameof(textResolver));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task<BuildingDetails> GetDetailsAsync(
        BuildingsIndex.BuildingRecord buildingRecord,
        int level,
        string locale,
        LangIndex langIndex,
        string factionDisplay,
        CancellationToken cancellationToken = default)
    {
        if (buildingRecord == null)
            throw new ArgumentNullException(nameof(buildingRecord));
        if (string.IsNullOrWhiteSpace(locale))
            throw new ArgumentException("Locale cannot be null or empty.", nameof(locale));
        if (langIndex == null)
            throw new ArgumentNullException(nameof(langIndex));

        try
        {
            var ctx = new ResolutionContext(locale)
            {
                FractionId = buildingRecord.Faction
            };

            var levelIndex = level - 1;
            var nameSid = levelIndex < buildingRecord.Names.Length ? buildingRecord.Names[levelIndex] : "";
            var name = !string.IsNullOrWhiteSpace(nameSid)
                ? (_textResolver.Resolve(nameSid, ctx, out _) ?? langIndex.ResolveText(nameSid) ?? buildingRecord.Sid)
                : $"{buildingRecord.Sid} (Level {level})";

            var descSid = levelIndex < buildingRecord.Descriptions.Length ? buildingRecord.Descriptions[levelIndex] : "";
            var description = !string.IsNullOrWhiteSpace(descSid)
                ? (_textResolver.Resolve(descSid, ctx, out _) ?? langIndex.ResolveText(descSid) ?? "")
                : "";

            var iconValue = "";
            if (buildingRecord.Icons != null && buildingRecord.Icons.Length > 0)
            {
                // Use level-specific icon (level is 1-indexed, array is 0-indexed)
                var icon = levelIndex >= 0 && levelIndex < buildingRecord.Icons.Length
                    ? buildingRecord.Icons[levelIndex]
                    : buildingRecord.Icons[0]; // Fallback to first icon

                if (!string.IsNullOrWhiteSpace(icon))
                {
                    var factionSegment = string.IsNullOrWhiteSpace(buildingRecord.Faction)
                        ? ""
                        : buildingRecord.Faction.ToLowerInvariant();
                    iconValue = !string.IsNullOrWhiteSpace(factionSegment)
                        ? $"{factionSegment}/{icon}"
                        : icon;
                }
            }

            var costs = new List<BuildingCostInfo>();
            if (buildingRecord.CostsPerLevel != null && levelIndex < buildingRecord.CostsPerLevel.Length)
            {
                var levelCosts = buildingRecord.CostsPerLevel[levelIndex] ?? Array.Empty<BuildingsIndex.BuildingCost>();
                costs = levelCosts
                    .Select(c => new BuildingCostInfo(c.ResourceName, c.Amount))
                    .OrderBy(c => ResourceOrder.TryGetValue(c.ResourceName.ToLowerInvariant(), out var order) ? order : 999)
                    .ToList();
            }

            var upgrades = new List<BuildingUpgradeInfo>();
            if (buildingRecord.EffectsPerLevel != null && levelIndex < buildingRecord.EffectsPerLevel.Length)
            {
                var levelEffects = buildingRecord.EffectsPerLevel[levelIndex] ?? Array.Empty<string>();
                foreach (var upgradeSid in levelEffects)
                {
                    if (string.IsNullOrWhiteSpace(upgradeSid)) continue;

                    var upgradeDescription = _textResolver.Resolve(upgradeSid, ctx, out _)
                        ?? langIndex.ResolveText(upgradeSid)
                        ?? upgradeSid;

                    var upgradeIcon = upgradeSid;

                    upgrades.Add(new BuildingUpgradeInfo(
                        upgradeSid,
                        upgradeIcon,
                        upgradeDescription
                    ));
                }
            }

            var plainTextSummary = BuildPlainTextSummary(name, description, costs, upgrades, langIndex, locale);

            var details = new BuildingDetails(
                name,
                iconValue,
                description,
                costs,
                upgrades,
                plainTextSummary
            );

            return Task.FromResult(details);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting building details for {BuildingSid} level {Level}", buildingRecord.Sid, level);
            throw;
        }
    }

    private string BuildPlainTextSummary(
        string name,
        string description,
        IReadOnlyList<BuildingCostInfo> costs,
        IReadOnlyList<BuildingUpgradeInfo> upgrades,
        LangIndex langIndex,
        string locale)
    {
        var summary = $"{name}\n\n{description}";

        if (costs.Count > 0)
        {
            var costParts = costs.Select(c =>
            {
                var resourceKey = $"resource_{c.ResourceName}";
                var localizedResourceName = langIndex.ResolveText(resourceKey) ?? c.ResourceName;
                return $"{c.Amount} {localizedResourceName}";
            });
            summary += $"\n\nCost: {string.Join(", ", costParts)}";
        }

        if (upgrades.Count > 0)
        {
            summary += "\n";
            foreach (var upgrade in upgrades)
            {
                var plainDescription = StripMarkupTags(upgrade.Description);
                summary += $"\n{plainDescription}";
            }
        }

        return summary;
    }

    private string StripMarkupTags(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return text;

        var result = System.Text.RegularExpressions.Regex.Replace(text, @"<[^>]+>", "");
        return result;
    }

    public string GetLocalizedResourceName(LangIndex langIndex, string resourceName)
    {
        if (langIndex == null || string.IsNullOrWhiteSpace(resourceName))
            return resourceName;

        var resourceKey = $"resource_{resourceName.ToLowerInvariant()}";
        return langIndex.ResolveText(resourceKey) ?? resourceName;
    }
}
