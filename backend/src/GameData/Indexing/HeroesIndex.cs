using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Localization.Indexing;
using GameData.Shared.Utils;

namespace GameData.Indexing;

public sealed class HeroesIndex
{
    public sealed record SkillWithLevel(string Sid, int Level);
    public sealed record StartSquadUnit(string Sid, int Min, int Max);

    public sealed record HeroRecord(
        string HeroId,
        string Fraction,
        string ClassType,
        string? SpecializationSid,
        string Mesh,
        string Icon,
        string ClassIcon,
        string SpecializationIcon,
        int CostGold,
        int StartLevel,
        Dictionary<string, int> BaseStats,
        List<SkillWithLevel> StartSkills,
        List<string> StartMagics,
        List<StartSquadUnit> StartSquad,
        string SourceFile
    )
    {
        /// <summary>Matches tutorial_*, campaign_*, and cm_* patterns.</summary>
        public bool IsTutorialOrCampaignHero =>
            HeroId.StartsWith("tutorial_", StringComparison.OrdinalIgnoreCase) ||
            HeroId.StartsWith("campaign_", StringComparison.OrdinalIgnoreCase) ||
            HeroId.StartsWith("cm_", StringComparison.OrdinalIgnoreCase);
    }

    private readonly Dictionary<string, HeroRecord> _heroes = new();

    public IReadOnlyDictionary<string, HeroRecord> Heroes => _heroes;

    public void ScanHeroes(string streamingAssetsRoot)
    {
        _heroes.Clear();

        var coreZipPath = Path.Combine(streamingAssetsRoot, "Core.zip");
        if (!File.Exists(coreZipPath))
        {
            DiagnosticsLog.Trace($"[HeroesIndex] Core.zip NOT FOUND at: {coreZipPath}");
            return;
        }

        using var zip = ZipFile.OpenRead(coreZipPath);

        var heroEntries = zip.Entries.Where(e =>
            e.FullName.StartsWith("DB/heroes/", StringComparison.OrdinalIgnoreCase) &&
            e.FullName.EndsWith(".json", StringComparison.OrdinalIgnoreCase) &&
            e.Length > 0).ToList();

        DiagnosticsLog.Trace($"[HeroesIndex] Found {heroEntries.Count} hero files in Core.zip");

        foreach (var entry in heroEntries)
        {
            try
            {
                ProcessHeroEntry(entry);
            }
            catch (Exception ex)
            {
                DiagnosticsLog.Trace($"[HeroesIndex] ERROR processing {entry.FullName}", ex);
            }
        }

        DiagnosticsLog.Trace($"[HeroesIndex] Successfully loaded {_heroes.Count} heroes");

        var mightCount = _heroes.Values.Count(h => h.ClassType == "might");
        var magicCount = _heroes.Values.Count(h => h.ClassType == "magic");
        DiagnosticsLog.Trace($"[HeroesIndex] Class distribution: Might={mightCount}, Magic={magicCount}");
    }

    /// <summary>
    /// Supplement heroes with placeholders from Lang files for factions that don't have DB data.
    /// This allows displaying heroes like nature_hero_1 even if DB/heroes/nature/ doesn't exist.
    /// </summary>
    public void SupplementFromLang(LangIndex langIndex)
    {
        if (langIndex == null)
            return;

        var heroPattern = new Regex(
            @"^([a-z]+)_hero_(\d+)$",
            RegexOptions.IgnoreCase);

        int addedCount = 0;
        foreach (var (sid, _) in langIndex.AllEntries())
        {
            var match = heroPattern.Match(sid);
            if (!match.Success)
                continue;

            var heroId = sid;
            var faction = match.Groups[1].Value;
            var heroNumberStr = match.Groups[2].Value;

            if (_heroes.ContainsKey(heroId))
                continue;

            // Heuristic: Heroes 1-9 are might, Heroes 10-18 are magic (when no DB data exists)
            var classType = "";
            if (int.TryParse(heroNumberStr, out var heroNumber))
            {
                if (heroNumber >= 1 && heroNumber <= 9)
                    classType = "might";
                else if (heroNumber >= 10 && heroNumber <= 18)
                    classType = "magic";
            }

            var iconPattern = $"hero_{faction}_{heroNumberStr}_*";
            var placeholder = new HeroRecord(
                HeroId: heroId,
                Fraction: faction,
                ClassType: classType,
                SpecializationSid: $"{heroId}_specialization",
                Mesh: "",
                Icon: $"{iconPattern}_large",
                ClassIcon: string.IsNullOrEmpty(classType) ? "" : $"{classType.ToLower()}_{faction.ToLower()}_icon",
                SpecializationIcon: $"{heroId}_specialization_icon",
                CostGold: 0,
                StartLevel: 1,
                BaseStats: new Dictionary<string, int>(),
                StartSkills: new List<SkillWithLevel>(),
                StartMagics: new List<string>(),
                StartSquad: new List<StartSquadUnit>(),
                SourceFile: "Lang (placeholder)"
            );

            _heroes[heroId] = placeholder;
            addedCount++;
        }
    }

    private void ProcessHeroEntry(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var json = reader.ReadToEnd();

        using var doc = JsonDocument.Parse(json);

        if (!doc.RootElement.TryGetProperty("array", out var array))
            return;

        foreach (var heroElement in array.EnumerateArray())
        {
            if (!heroElement.TryGetProperty("id", out var idProp))
                continue;

            var heroId = idProp.GetString() ?? "";

            string fraction = "";
            if (heroElement.TryGetProperty("fraction", out var fractionProp))
                fraction = fractionProp.GetString() ?? "";

            string classType = "";
            if (heroElement.TryGetProperty("classType", out var classTypeProp))
                classType = classTypeProp.GetString() ?? "";

            // If classType is missing (older game versions), infer from hero number
            if (string.IsNullOrEmpty(classType))
            {
                var heroPattern = new Regex(@"^[a-z]+_hero_(\d+)$", RegexOptions.IgnoreCase);
                var match = heroPattern.Match(heroId);
                if (match.Success && int.TryParse(match.Groups[1].Value, out var heroNumber))
                {
                    if (heroNumber >= 1 && heroNumber <= 9)
                        classType = "might";
                    else if (heroNumber >= 10 && heroNumber <= 18)
                        classType = "magic";
                }
            }

            string? specializationSid = null;
            if (heroElement.TryGetProperty("specialization", out var specProp))
                specializationSid = specProp.GetString();

            string mesh = "";
            if (heroElement.TryGetProperty("mesh", out var meshProp))
                mesh = meshProp.GetString() ?? "";

            string iconFromJson = "";
            if (heroElement.TryGetProperty("icon", out var iconProp))
                iconFromJson = iconProp.GetString() ?? "";

            string icon;
            if (!string.IsNullOrEmpty(iconFromJson))
            {
                icon = iconFromJson.EndsWith("_large") ? iconFromJson.ToLower() : $"{iconFromJson}_large".ToLower();
            }
            else
            {
                var fallbackPattern = new Regex(@"^([a-z]+)_hero_(\d+)$", RegexOptions.IgnoreCase);
                var fallbackMatch = fallbackPattern.Match(heroId);
                if (fallbackMatch.Success)
                {
                    var factionName = fallbackMatch.Groups[1].Value;
                    var heroNum = fallbackMatch.Groups[2].Value;
                    icon = $"hero_{factionName}_{heroNum}_*_large".ToLower();
                }
                else
                {
                    icon = $"{heroId}_large".ToLower();
                }
            }

            string classIcon = string.IsNullOrEmpty(classType)
                ? ""
                : $"{classType.ToLower()}_{fraction.ToLower()}_icon";

            string specializationIcon = $"{heroId}_specialization_icon";

            int costGold = 0;
            if (heroElement.TryGetProperty("costGold", out var costProp) && costProp.ValueKind == JsonValueKind.Number)
                costGold = costProp.GetInt32();

            int startLevel = 1;
            if (heroElement.TryGetProperty("startLevel", out var levelProp) && levelProp.ValueKind == JsonValueKind.Number)
                startLevel = levelProp.GetInt32();

            var baseStats = new Dictionary<string, int>();
            if (heroElement.TryGetProperty("stats", out var statsProp) && statsProp.ValueKind == JsonValueKind.Object)
            {
                if (statsProp.TryGetProperty("offence", out var offProp) && offProp.ValueKind == JsonValueKind.Number)
                    baseStats["offence"] = offProp.GetInt32();
                if (statsProp.TryGetProperty("defence", out var defProp) && defProp.ValueKind == JsonValueKind.Number)
                    baseStats["defence"] = defProp.GetInt32();
                if (statsProp.TryGetProperty("spellPower", out var spProp) && spProp.ValueKind == JsonValueKind.Number)
                    baseStats["spellPower"] = spProp.GetInt32();
                if (statsProp.TryGetProperty("intelligence", out var intProp) && intProp.ValueKind == JsonValueKind.Number)
                    baseStats["intelligence"] = intProp.GetInt32();
                if (statsProp.TryGetProperty("luck", out var luckProp) && luckProp.ValueKind == JsonValueKind.Number)
                    baseStats["luck"] = luckProp.GetInt32();
                if (statsProp.TryGetProperty("moral", out var moralProp) && moralProp.ValueKind == JsonValueKind.Number)
                    baseStats["moral"] = moralProp.GetInt32();
            }

            var startSkills = new List<SkillWithLevel>();
            if (heroElement.TryGetProperty("startSkills", out var skillsProp) && skillsProp.ValueKind == JsonValueKind.Array)
            {
                foreach (var skill in skillsProp.EnumerateArray())
                {
                    if (skill.TryGetProperty("sid", out var sidProp))
                    {
                        var sid = sidProp.GetString();
                        if (!string.IsNullOrWhiteSpace(sid))
                        {
                            int skillLevel = 1;
                            if (skill.TryGetProperty("skillLevel", out var skillLevelProp) && skillLevelProp.ValueKind == JsonValueKind.Number)
                                skillLevel = skillLevelProp.GetInt32();

                            startSkills.Add(new SkillWithLevel(sid, skillLevel));
                        }
                    }
                }
            }

            var startMagics = new List<string>();
            if (heroElement.TryGetProperty("startMagics", out var magicsProp) && magicsProp.ValueKind == JsonValueKind.Array)
            {
                foreach (var magic in magicsProp.EnumerateArray())
                {
                    if (magic.ValueKind == JsonValueKind.Object)
                    {
                        if (magic.TryGetProperty("sidConfig", out var sidProp))
                        {
                            var sid = sidProp.GetString();
                            if (!string.IsNullOrWhiteSpace(sid))
                                startMagics.Add(sid);
                        }
                    }
                    else if (magic.ValueKind == JsonValueKind.String)
                    {
                        var magicStr = magic.GetString();
                        if (!string.IsNullOrWhiteSpace(magicStr))
                            startMagics.Add(magicStr);
                    }
                }
            }

            var startSquad = new List<StartSquadUnit>();
            if (heroElement.TryGetProperty("startSquad", out var squadProp) && squadProp.ValueKind == JsonValueKind.Array)
            {
                foreach (var unit in squadProp.EnumerateArray())
                {
                    if (unit.TryGetProperty("sid", out var sidProp))
                    {
                        var sid = sidProp.GetString();
                        if (!string.IsNullOrWhiteSpace(sid))
                        {
                            int min = 0;
                            int max = 0;

                            if (unit.TryGetProperty("min", out var minProp) && minProp.ValueKind == JsonValueKind.Number)
                                min = minProp.GetInt32();
                            if (unit.TryGetProperty("max", out var maxProp) && maxProp.ValueKind == JsonValueKind.Number)
                                max = maxProp.GetInt32();

                            startSquad.Add(new StartSquadUnit(sid, min, max));
                        }
                    }
                }
            }

            if (!_heroes.ContainsKey(heroId))
            {
                _heroes[heroId] = new HeroRecord(
                    heroId,
                    fraction,
                    classType,
                    specializationSid,
                    mesh,
                    icon,
                    classIcon,
                    specializationIcon,
                    costGold,
                    startLevel,
                    baseStats,
                    startSkills,
                    startMagics,
                    startSquad,
                    Path.GetFileName(entry.FullName)
                );
            }
        }
    }

    /// <summary>
    /// Override SpecializationIcon for each hero using the icon declared in the
    /// specialization JSON (handles campaign heroes whose icon differs from the hero ID).
    /// Must be called after HeroSpecializationsIndex is fully built.
    /// </summary>
    public void ApplySpecializationIcons(HeroSpecializationsIndex specIndex)
    {
        foreach (var (id, hero) in _heroes.ToList())
        {
            if (string.IsNullOrWhiteSpace(hero.SpecializationSid))
                continue;

            if (specIndex.Specializations.TryGetValue(hero.SpecializationSid, out var spec) &&
                !string.IsNullOrWhiteSpace(spec.Icon))
            {
                _heroes[id] = hero with { SpecializationIcon = spec.Icon };
            }
        }
    }
}
