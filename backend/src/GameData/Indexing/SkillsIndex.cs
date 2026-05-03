using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using GameData.Shared.Utils;

namespace GameData.Indexing;

public sealed class SkillsIndex
{
    public sealed record SkillLevelParam(
        string Icon,
        string NameSid,
        string DescSid,
        List<string> SubSkills,
        List<string> DirectMagicIds
    );

    public sealed record SkillRecord(
        string SkillId,
        string NameSid,
        string DescSid,
        string SkillType,
        int MaxLevel,
        List<string> AllSubSkills,
        List<SkillLevelParam> LevelParams,
        string SourceFile,
        bool IsPseudoSkill,
        List<string> DirectMagicIds
    );

    public sealed record SubSkillRecord(
        string SubSkillId,
        string NameSid,
        string DescSid,
        string Icon,
        string SourceFile,
        string? GrantedBattleAbilityId = null
    );

    public sealed record HeroAbilityRecord(
        string AbilityId,
        string NameSid,
        string DescSid
    );

    private readonly Dictionary<string, SkillRecord> _skills = new();
    private readonly Dictionary<string, SubSkillRecord> _subSkills = new();
    private readonly Dictionary<string, List<string>> _subSkillToMagics = new();
    private readonly Dictionary<string, string> _spellToSkill = new();
    private readonly Dictionary<string, HeroAbilityRecord> _heroAbilities = new();

    public IReadOnlyDictionary<string, SkillRecord> Skills => _skills;
    public IReadOnlyDictionary<string, SubSkillRecord> SubSkills => _subSkills;
    public IReadOnlyDictionary<string, string> SpellToSkill => _spellToSkill;
    public IReadOnlyDictionary<string, List<string>> SubSkillToMagics => _subSkillToMagics;
    public IReadOnlyDictionary<string, HeroAbilityRecord> HeroAbilities => _heroAbilities;

    public void ScanSkills(string streamingAssetsRoot)
    {
        _skills.Clear();
        _subSkills.Clear();
        _subSkillToMagics.Clear();
        _spellToSkill.Clear();
        _heroAbilities.Clear();

        var coreZipPath = Path.Combine(streamingAssetsRoot, "Core.zip");
        if (!File.Exists(coreZipPath))
            return;

        using var zip = ZipFile.OpenRead(coreZipPath);

        var skillEntries = zip.Entries.Where(e =>
            e.FullName.StartsWith("DB/heroes_skills/skills/", StringComparison.OrdinalIgnoreCase) &&
            e.FullName.EndsWith(".json", StringComparison.OrdinalIgnoreCase) &&
            e.Length > 0).ToList();

        var subSkillEntries = zip.Entries.Where(e =>
            e.FullName.StartsWith("DB/heroes_skills/sub_skills/", StringComparison.OrdinalIgnoreCase) &&
            e.FullName.EndsWith(".json", StringComparison.OrdinalIgnoreCase) &&
            e.Length > 0).ToList();

        var heroAbilityEntry = zip.Entries.FirstOrDefault(e =>
            e.FullName.Equals("DB/heroes_abilities/heroes_abilities_base/hero_abilities.json", StringComparison.OrdinalIgnoreCase) &&
            e.Length > 0);

        foreach (var entry in skillEntries)
        {
            try
            {
                ProcessSkillEntry(entry);
            }
            catch (Exception ex)
            {
                DiagnosticsLog.Trace($"[SkillsIndex] Error processing {entry.FullName}", ex);
            }
        }

        foreach (var entry in subSkillEntries)
        {
            try
            {
                ProcessSubSkillEntry(entry);
            }
            catch (Exception ex)
            {
                DiagnosticsLog.Trace($"[SkillsIndex] Error processing {entry.FullName}", ex);
            }
        }

        if (heroAbilityEntry != null)
        {
            try
            {
                ScanHeroAbilities(heroAbilityEntry);
            }
            catch (Exception ex)
            {
                DiagnosticsLog.Trace("[SkillsIndex] Error processing hero_abilities.json", ex);
            }
        }

        BuildSpellToSkillIndex();
    }

    private void BuildSpellToSkillIndex()
    {
        foreach (var skill in _skills.Values)
        {
            // Via sub_skill heroMagicAddition bonuses
            foreach (var subSkillId in skill.AllSubSkills)
            {
                if (!_subSkillToMagics.TryGetValue(subSkillId, out var magicIds))
                    continue;

                foreach (var spellId in magicIds)
                {
                    if (!_spellToSkill.ContainsKey(spellId))
                        _spellToSkill[spellId] = skill.SkillId;
                }
            }

            // Via skill-level heroMagicAddition bonuses (e.g. skill_summoner → bonus_magic_astral_summon_*)
            foreach (var spellId in skill.DirectMagicIds)
            {
                if (!_spellToSkill.ContainsKey(spellId))
                    _spellToSkill[spellId] = skill.SkillId;
            }
        }
    }

    private void ProcessSkillEntry(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var json = reader.ReadToEnd();

        using var doc = JsonDocument.Parse(json);

        if (!doc.RootElement.TryGetProperty("array", out var array))
            return;

        foreach (var skillElement in array.EnumerateArray())
        {
            if (!skillElement.TryGetProperty("id", out var idProp))
                continue;

            var skillId = idProp.GetString() ?? "";

            string nameSid = "";
            if (skillElement.TryGetProperty("name", out var nameProp))
                nameSid = nameProp.GetString() ?? "";

            string descSid = "";
            if (skillElement.TryGetProperty("desc", out var descProp))
                descSid = descProp.GetString() ?? "";

            string skillType = "";
            if (skillElement.TryGetProperty("skillType", out var typeProp))
                skillType = typeProp.GetString() ?? "";

            bool isPseudoSkill = false;
            if (skillElement.TryGetProperty("isPseudoSkill", out var isPseudoProp))
                isPseudoSkill = isPseudoProp.GetBoolean();

            var allSubSkills = new HashSet<string>();
            var levelParams = new List<SkillLevelParam>();
            int maxLevel = 0;

            if (skillElement.TryGetProperty("parametersPerLevel", out var paramsArray) &&
                paramsArray.ValueKind == JsonValueKind.Array)
            {
                maxLevel = paramsArray.GetArrayLength();

                foreach (var levelParam in paramsArray.EnumerateArray())
                {
                    var icon = "";
                    if (levelParam.TryGetProperty("icon", out var iconProp))
                        icon = iconProp.GetString() ?? "";

                    var levelNameSid = "";
                    if (levelParam.TryGetProperty("name", out var levelNameProp))
                        levelNameSid = levelNameProp.GetString() ?? "";

                    var levelDescSid = "";
                    if (levelParam.TryGetProperty("desc", out var levelDescProp))
                        levelDescSid = levelDescProp.GetString() ?? "";

                    var subSkillsList = new List<string>();
                    if (levelParam.TryGetProperty("subSkills", out var subSkillsArr) &&
                        subSkillsArr.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var subSkill in subSkillsArr.EnumerateArray())
                        {
                            var subSkillId = subSkill.GetString();
                            if (!string.IsNullOrWhiteSpace(subSkillId))
                            {
                                allSubSkills.Add(subSkillId);
                                subSkillsList.Add(subSkillId);
                            }
                        }
                    }

                    var directMagicsForLevel = new List<string>();
                    if (levelParam.TryGetProperty("bonuses", out var bonusesArrL) &&
                        bonusesArrL.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var bonus in bonusesArrL.EnumerateArray())
                        {
                            if (!bonus.TryGetProperty("type", out var bTypeProp)) continue;
                            if (!string.Equals(bTypeProp.GetString(), "heroMagicAddition", StringComparison.OrdinalIgnoreCase)) continue;
                            if (!bonus.TryGetProperty("parameters", out var bParams) ||
                                bParams.ValueKind != JsonValueKind.Array ||
                                bParams.GetArrayLength() == 0) continue;
                            var spellId = bParams[0].GetString();
                            if (!string.IsNullOrWhiteSpace(spellId))
                                directMagicsForLevel.Add(spellId);
                        }
                    }

                    levelParams.Add(new SkillLevelParam(icon, levelNameSid, levelDescSid, subSkillsList, directMagicsForLevel));
                }
            }

            var allDirectMagics = levelParams.SelectMany(lp => lp.DirectMagicIds).Distinct().ToList();

            if (!_skills.ContainsKey(skillId))
            {
                _skills[skillId] = new SkillRecord(
                    skillId,
                    nameSid,
                    descSid,
                    skillType,
                    maxLevel,
                    allSubSkills.ToList(),
                    levelParams,
                    Path.GetFileName(entry.FullName),
                    isPseudoSkill,
                    allDirectMagics
                );
            }
        }
    }

    private void ProcessSubSkillEntry(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var json = reader.ReadToEnd();

        using var doc = JsonDocument.Parse(json);

        if (!doc.RootElement.TryGetProperty("array", out var array))
            return;

        foreach (var subSkillElement in array.EnumerateArray())
        {
            if (!subSkillElement.TryGetProperty("id", out var idProp))
                continue;

            var subSkillId = idProp.GetString() ?? "";

            string nameSid = "";
            if (subSkillElement.TryGetProperty("name", out var nameProp))
                nameSid = nameProp.GetString() ?? "";

            string descSid = "";
            if (subSkillElement.TryGetProperty("desc", out var descProp))
                descSid = descProp.GetString() ?? "";

            string icon = "";
            if (subSkillElement.TryGetProperty("icon", out var iconProp))
                icon = iconProp.GetString() ?? "";

            string? grantedBattleAbilityId = null;

            if (subSkillElement.TryGetProperty("bonuses", out var bonusesArr) &&
                bonusesArr.ValueKind == JsonValueKind.Array)
            {
                foreach (var bonus in bonusesArr.EnumerateArray())
                {
                    if (!bonus.TryGetProperty("type", out var typeProp2)) continue;
                    var bonusType = typeProp2.GetString() ?? "";

                    if (!bonus.TryGetProperty("parameters", out var paramsEl) ||
                        paramsEl.ValueKind != JsonValueKind.Array ||
                        paramsEl.GetArrayLength() == 0) continue;

                    var paramValue = paramsEl[0].GetString();
                    if (string.IsNullOrWhiteSpace(paramValue)) continue;

                    if (string.Equals(bonusType, "heroMagicAddition", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!_subSkillToMagics.TryGetValue(subSkillId, out var list))
                        {
                            list = new List<string>();
                            _subSkillToMagics[subSkillId] = list;
                        }
                        list.Add(paramValue);
                    }
                    else if (string.Equals(bonusType, "heroBattleAbility", StringComparison.OrdinalIgnoreCase))
                    {
                        grantedBattleAbilityId ??= paramValue;
                    }
                }
            }

            if (!_subSkills.ContainsKey(subSkillId))
            {
                _subSkills[subSkillId] = new SubSkillRecord(
                    subSkillId,
                    nameSid,
                    descSid,
                    icon,
                    Path.GetFileName(entry.FullName),
                    grantedBattleAbilityId
                );
            }
        }
    }

    private void ScanHeroAbilities(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var json = reader.ReadToEnd();

        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("array", out var array))
            return;

        foreach (var element in array.EnumerateArray())
        {
            if (!element.TryGetProperty("id", out var idProp))
                continue;

            var abilityId = idProp.GetString() ?? "";

            string nameSid = "";
            string descSid = "";

            if (element.TryGetProperty("levels", out var levels) &&
                levels.ValueKind == JsonValueKind.Array &&
                levels.GetArrayLength() > 0)
            {
                var firstLevel = levels[0];
                if (firstLevel.TryGetProperty("name", out var nameProp))
                    nameSid = nameProp.GetString() ?? "";
                if (firstLevel.TryGetProperty("description", out var descProp))
                    descSid = descProp.GetString() ?? "";
            }

            if (!string.IsNullOrEmpty(abilityId) && !_heroAbilities.ContainsKey(abilityId))
            {
                _heroAbilities[abilityId] = new HeroAbilityRecord(abilityId, nameSid, descSid);
            }
        }
    }
}
