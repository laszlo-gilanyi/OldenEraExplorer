using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using GameData.Shared.Utils;

namespace GameData.Indexing;

public sealed class BuildingsIndex
{
    public sealed class BuildingCost
    {
        public string ResourceName { get; init; } = "";
        public int Amount { get; init; }
    }

    public sealed class RequiredBuilding
    {
        public string Sid { get; init; } = "";
        public int Level { get; init; }
    }

    public sealed class OptionalEffect
    {
        public string Sid { get; init; } = "";
        public string Icon { get; init; } = "";
        public string DescSid { get; init; } = "";
    }

    public sealed record BuildingRecord(
        string Sid,
        string Category,
        string Faction,
        int MaxLevel,
        string[] Names,
        string[] Descriptions,
        bool IsConstructedOnStart,
        int LevelOnStart,
        string[] Icons,
        string SourceFile,
        string[][] EffectsPerLevel,
        BuildingCost[][] CostsPerLevel,
        /// <summary>
        /// Units that can be recruited from this building (for dwelling buildings).
        /// Contains all unit variants (base + upgrades).
        /// </summary>
        string[] RecruitableUnits,
        /// <summary>
        /// Units recruitable at each building level. Index 0 = level 1 units, etc.
        /// Level 1 typically has base unit, Level 2+ has upgraded variants.
        /// </summary>
        string[][] RecruitableUnitsPerLevel,
        RequiredBuilding[][] RequiredBuildingsPerLevel,
        /// <summary>
        /// Per-level selectable upgrade options (player picks one). Index 0 = level 1.
        /// Each inner list contains the mutually-exclusive options for that level.
        /// </summary>
        OptionalEffect[][] OptionalEffectsPerLevel
    );

    private readonly Dictionary<string, BuildingRecord> _buildings = new();
    public IReadOnlyDictionary<string, BuildingRecord> Buildings => _buildings;

    public void Scan(string streamingAssetsRoot)
    {
        _buildings.Clear();
        var zipPath = Path.Combine(streamingAssetsRoot, "Core.zip");
        if (!File.Exists(zipPath)) return;

        using var zip = ZipFile.OpenRead(zipPath);

        var entries = zip.Entries.Where(e =>
            e.FullName.StartsWith("DB/objects_logic/cities/", StringComparison.OrdinalIgnoreCase) &&
            e.FullName.EndsWith(".json", StringComparison.OrdinalIgnoreCase) &&
            e.Length > 0).ToList();

        foreach (var entry in entries)
        {
            try
            {
                ProcessCityEntry(entry);
            }
            catch (Exception ex)
            {
                DiagnosticsLog.Trace($"[BuildingsIndex] Error processing {entry.FullName}", ex);
            }
        }

        DiagnosticsLog.Trace($"[BuildingsIndex] Loaded {_buildings.Count} buildings");
    }

    /// <summary>
    /// Supplement buildings with placeholders from Lang files for factions that don't have DB data.
    /// This allows displaying buildings like nature_Build_Main even if DB/objects_logic/cities/nature_city.json doesn't exist.
    /// </summary>
    public void SupplementFromLang(Localization.Indexing.LangIndex langIndex)
    {
        if (langIndex == null)
            return;

        // Pattern: {Faction}_Build_{BuildingSid}_name_level_{level}
        // Example: Nature_Build_Main_name_level_1
        var buildingNamePattern = new Regex(
            @"^([A-Z][a-z]+)_Build_([A-Za-z_]+)_name_level_(\d+)$",
            RegexOptions.None);

        var buildingDescPattern = new Regex(
            @"^([A-Z][a-z]+)_Build_([A-Za-z_]+)_description_level_(\d+)$",
            RegexOptions.None);

        var buildingsByKey = new Dictionary<string, BuildingPlaceholder>(StringComparer.OrdinalIgnoreCase);

        foreach (var (sid, _) in langIndex.AllEntries())
        {
            var match = buildingNamePattern.Match(sid);
            if (match.Success)
            {
                var faction = match.Groups[1].Value.ToLowerInvariant(); // "Nature" -> "nature"
                var buildingSid = match.Groups[2].Value; // "Main", "Wall", etc.
                var level = int.Parse(match.Groups[3].Value);

                var key = $"{faction}_Build_{buildingSid}";

                if (!buildingsByKey.ContainsKey(key))
                    buildingsByKey[key] = new BuildingPlaceholder { Faction = faction, Sid = $"Build_{buildingSid}" };

                buildingsByKey[key].AddName(level, sid);
                continue;
            }

            match = buildingDescPattern.Match(sid);
            if (match.Success)
            {
                var faction = match.Groups[1].Value.ToLowerInvariant();
                var buildingSid = match.Groups[2].Value;
                var level = int.Parse(match.Groups[3].Value);

                var key = $"{faction}_Build_{buildingSid}";

                if (!buildingsByKey.ContainsKey(key))
                    buildingsByKey[key] = new BuildingPlaceholder { Faction = faction, Sid = $"Build_{buildingSid}" };

                buildingsByKey[key].AddDescription(level, sid);
            }
        }

        int addedCount = 0;
        foreach (var (key, placeholder) in buildingsByKey)
        {
            if (_buildings.ContainsKey(key))
                continue;

            var names = placeholder.GetNamesArray();
            var descriptions = placeholder.GetDescriptionsArray();
            var maxLevel = Math.Max(names.Length, descriptions.Length);
            if (maxLevel == 0) continue;

            _buildings[key] = new BuildingRecord(
                Sid: placeholder.Sid,
                Category: "", // No category data for supplemented buildings
                Faction: placeholder.Faction,
                MaxLevel: maxLevel,
                Names: names,
                Descriptions: descriptions,
                IsConstructedOnStart: false,
                LevelOnStart: 0,
                Icons: new[] { "buildings_wip" }, // WIP icon for supplemented buildings
                SourceFile: "Lang (placeholder)",
                EffectsPerLevel: Array.Empty<string[]>(),
                CostsPerLevel: Array.Empty<BuildingCost[]>(),
                RecruitableUnits: Array.Empty<string>(),
                RecruitableUnitsPerLevel: Array.Empty<string[]>(),
                RequiredBuildingsPerLevel: Array.Empty<RequiredBuilding[]>(),
                OptionalEffectsPerLevel: Array.Empty<OptionalEffect[]>()
            );

            addedCount++;
        }

        if (addedCount > 0)
            DiagnosticsLog.Trace($"[BuildingsIndex] Added {addedCount} placeholder buildings from Lang");
    }

    private class BuildingPlaceholder
    {
        public string Faction { get; set; } = "";
        public string Sid { get; set; } = "";
        private readonly Dictionary<int, string> _names = new();
        private readonly Dictionary<int, string> _descriptions = new();

        public void AddName(int level, string sid)
        {
            _names[level] = sid;
        }

        public void AddDescription(int level, string sid)
        {
            _descriptions[level] = sid;
        }

        public string[] GetNamesArray()
        {
            if (_names.Count == 0) return Array.Empty<string>();
            var maxLevel = _names.Keys.Max();
            var result = new string[maxLevel];
            for (int i = 1; i <= maxLevel; i++)
            {
                result[i - 1] = _names.ContainsKey(i) ? _names[i] : "";
            }
            return result;
        }

        public string[] GetDescriptionsArray()
        {
            if (_descriptions.Count == 0) return Array.Empty<string>();
            var maxLevel = _descriptions.Keys.Max();
            var result = new string[maxLevel];
            for (int i = 1; i <= maxLevel; i++)
            {
                result[i - 1] = _descriptions.ContainsKey(i) ? _descriptions[i] : "";
            }
            return result;
        }
    }

    private void ProcessCityEntry(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        using var reader = new StreamReader(stream, Encoding.UTF8);
        using var doc = JsonDocument.Parse(reader.ReadToEnd());

        if (!doc.RootElement.TryGetProperty("array", out var array))
            return;

        foreach (var cityElement in array.EnumerateArray())
        {
            var faction = cityElement.TryGetProperty("fraction", out var factionP) ? factionP.GetString() ?? "" : "";
            var fileName = Path.GetFileName(entry.FullName);

            // Dynamically discover and process all building categories
            foreach (var property in cityElement.EnumerateObject())
            {
                // Skip known non-building properties
                if (property.Name == "id" ||
                    property.Name == "fraction" ||
                    property.Name == "fortificationLevel" ||
                    property.Name == "dwellingsIncreaseCycle" ||
                    property.Value.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                // Check if this array contains building objects (has "sid" property)
                var isBuildingCategory = false;
                foreach (var item in property.Value.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.Object && item.TryGetProperty("sid", out _))
                    {
                        isBuildingCategory = true;
                        break;
                    }
                }

                if (isBuildingCategory)
                {
                    ProcessBuildingCategory(cityElement, property.Name, faction, fileName);
                }
            }
        }
    }

    private void ProcessBuildingCategory(JsonElement cityElement, string category, string faction, string sourceFile)
    {
        if (!cityElement.TryGetProperty(category, out var buildingsArray))
            return;

        foreach (var building in buildingsArray.EnumerateArray())
        {
            var sid = building.TryGetProperty("sid", out var sidP) ? sidP.GetString() ?? "" : "";
            if (string.IsNullOrWhiteSpace(sid)) continue;

            var isConstructedOnStart = building.TryGetProperty("isConstructedOnStart", out var constructedP) &&
                                      constructedP.ValueKind == JsonValueKind.True;

            var levelOnStart = 1;
            if (building.TryGetProperty("levelOnStart", out var levelP) && levelP.ValueKind == JsonValueKind.Number)
                levelOnStart = levelP.GetInt32();

            var names = Array.Empty<string>();
            if (building.TryGetProperty("names", out var namesP) && namesP.ValueKind == JsonValueKind.Array)
            {
                names = namesP.EnumerateArray()
                    .Select(e => e.GetString() ?? "")
                    .ToArray();
            }

            var descriptions = Array.Empty<string>();
            if (building.TryGetProperty("descriptions", out var descsP) && descsP.ValueKind == JsonValueKind.Array)
            {
                descriptions = descsP.EnumerateArray()
                    .Select(e => e.GetString() ?? "")
                    .ToArray();
            }

            var icons = Array.Empty<string>();
            if (building.TryGetProperty("icons", out var iconsP) && iconsP.ValueKind == JsonValueKind.Array)
            {
                icons = iconsP.EnumerateArray()
                    .Select(e => e.GetString() ?? "")
                    .Where(i => !string.IsNullOrWhiteSpace(i))
                    .Select(i => i.ToLowerInvariant())
                    .ToArray();
            }

            var maxLevel = Math.Max(names.Length, descriptions.Length);
            if (maxLevel == 0) maxLevel = 1;

            var effectsPerLevel = Array.Empty<string[]>();

            var optionalEffectsPerLevel = Array.Empty<OptionalEffect[]>();
            if (building.TryGetProperty("optionalEffectsPerLevel", out var optEffectsP) && optEffectsP.ValueKind == JsonValueKind.Array)
            {
                var optList = new List<OptionalEffect[]>();
                foreach (var levelEntry in optEffectsP.EnumerateArray())
                {
                    if (levelEntry.TryGetProperty("effects", out var effectsArr) && effectsArr.ValueKind == JsonValueKind.Array)
                    {
                        var options = effectsArr.EnumerateArray()
                            .Select(e => new OptionalEffect
                            {
                                Sid = e.TryGetProperty("sid", out var s) ? s.GetString() ?? "" : "",
                                Icon = e.TryGetProperty("icon", out var ic) ? ic.GetString() ?? "" : "",
                                DescSid = e.TryGetProperty("desc", out var d) ? d.GetString() ?? "" : "",
                            })
                            .Where(o => !string.IsNullOrWhiteSpace(o.Sid))
                            .ToArray();
                        optList.Add(options);
                    }
                    else
                    {
                        optList.Add(Array.Empty<OptionalEffect>());
                    }
                }
                optionalEffectsPerLevel = optList.ToArray();
            }

            var costsPerLevel = Array.Empty<BuildingCost[]>();
            var requiredBuildingsPerLevel = Array.Empty<RequiredBuilding[]>();
            if (building.TryGetProperty("parametersPerLevel", out var paramsP) && paramsP.ValueKind == JsonValueKind.Array)
            {
                var costsList = new List<BuildingCost[]>();
                var requiredBuildingsList = new List<RequiredBuilding[]>();

                foreach (var levelParams in paramsP.EnumerateArray())
                {
                    if (levelParams.TryGetProperty("costs", out var costsP) && costsP.ValueKind == JsonValueKind.Array)
                    {
                        var levelCosts = new List<BuildingCost>();
                        foreach (var costEntry in costsP.EnumerateArray())
                        {
                            // EA format uses "name"/"cost"; legacy used "resName"/"value"
                            var resName = costEntry.TryGetProperty("name", out var nameP) ? nameP.GetString() ?? ""
                                : costEntry.TryGetProperty("resName", out var resNameP) ? resNameP.GetString() ?? "" : "";
                            var value = costEntry.TryGetProperty("cost", out var costP) && costP.ValueKind == JsonValueKind.Number ? costP.GetInt32()
                                : costEntry.TryGetProperty("value", out var valueP) && valueP.ValueKind == JsonValueKind.Number ? valueP.GetInt32() : 0;

                            if (!string.IsNullOrWhiteSpace(resName) && value > 0)
                            {
                                levelCosts.Add(new BuildingCost { ResourceName = resName, Amount = value });
                            }
                        }
                        costsList.Add(levelCosts.ToArray());
                    }
                    else
                    {
                        costsList.Add(Array.Empty<BuildingCost>());
                    }

                    if (levelParams.TryGetProperty("prevBuildings", out var prevP) && prevP.ValueKind == JsonValueKind.Array)
                    {
                        var levelRequirements = new List<RequiredBuilding>();
                        foreach (var prevBuilding in prevP.EnumerateArray())
                        {
                            var prevSid = prevBuilding.TryGetProperty("sid", out var prevSidP) ? prevSidP.GetString() ?? "" : "";
                            var prevLevel = prevBuilding.TryGetProperty("level", out var prevLevelP) && prevLevelP.ValueKind == JsonValueKind.Number
                                ? prevLevelP.GetInt32()
                                : 1;

                            if (!string.IsNullOrWhiteSpace(prevSid))
                            {
                                levelRequirements.Add(new RequiredBuilding { Sid = prevSid, Level = prevLevel });
                            }
                        }
                        requiredBuildingsList.Add(levelRequirements.ToArray());
                    }
                    else
                    {
                        requiredBuildingsList.Add(Array.Empty<RequiredBuilding>());
                    }
                }
                costsPerLevel = costsList.ToArray();
                requiredBuildingsPerLevel = requiredBuildingsList.ToArray();
            }

            var recruitableUnits = Array.Empty<string>();
            var recruitableUnitsPerLevel = Array.Empty<string[]>();
            if (building.TryGetProperty("unitsHire", out var unitsHireP) && unitsHireP.ValueKind == JsonValueKind.Object)
            {
                if (unitsHireP.TryGetProperty("units", out var unitsArrayP) && unitsArrayP.ValueKind == JsonValueKind.Array)
                {
                    var allUnits = new List<string>();
                    var level1Units = new List<string>();
                    var level2Units = new List<string>();

                    foreach (var unitEntry in unitsArrayP.EnumerateArray())
                    {
                        if (unitEntry.TryGetProperty("sids", out var sidsP) && sidsP.ValueKind == JsonValueKind.Array)
                        {
                            var sids = sidsP.EnumerateArray()
                                .Select(e => e.GetString())
                                .Where(s => !string.IsNullOrWhiteSpace(s))
                                .Cast<string>()
                                .ToArray();

                            allUnits.AddRange(sids);

                            // Game stores base unit first, upgrades after
                            if (sids.Length > 0)
                            {
                                level1Units.Add(sids[0]);

                                if (sids.Length > 1)
                                {
                                    level2Units.AddRange(sids.Skip(1));
                                }
                            }
                        }
                    }

                    recruitableUnits = allUnits.ToArray();

                    if (level1Units.Count > 0 || level2Units.Count > 0)
                    {
                        var perLevel = new List<string[]>();
                        perLevel.Add(level1Units.ToArray());
                        if (level2Units.Count > 0)
                        {
                            perLevel.Add(level2Units.ToArray());
                        }
                        recruitableUnitsPerLevel = perLevel.ToArray();
                    }
                }
            }

            var key = $"{faction}_{sid}";
            if (!_buildings.ContainsKey(key))
            {
                _buildings[key] = new BuildingRecord(
                    sid,
                    category,
                    faction,
                    maxLevel,
                    names,
                    descriptions,
                    isConstructedOnStart,
                    levelOnStart,
                    icons,
                    sourceFile,
                    effectsPerLevel,
                    costsPerLevel,
                    recruitableUnits,
                    recruitableUnitsPerLevel,
                    requiredBuildingsPerLevel,
                    optionalEffectsPerLevel
                );
            }
        }
    }
}
