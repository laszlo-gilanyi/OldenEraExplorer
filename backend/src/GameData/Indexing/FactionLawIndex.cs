using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace GameData.Indexing;

public sealed class FactionLawIndex
{
    public sealed record BonusEffect(string Type, List<string> Parameters, List<string> Receivers, string Fraction);

    public sealed record LevelParameters(int Cost, List<BonusEffect> Bonuses);

    public sealed record FactionLawRecord(
        string Id,
        string NameSid,
        string DescSid,
        string Icon,
        string Faction,
        List<LevelParameters> ParametersPerLevel);

    public sealed record FactionLawGroup(List<string> LawIds);

    public sealed record FactionLawLine(int CountToUnlock, List<FactionLawGroup> Groups);

    private readonly Dictionary<string, FactionLawRecord> _factionLaws = new();
    public IReadOnlyDictionary<string, FactionLawRecord> FactionLaws => _factionLaws;

    private readonly Dictionary<string, List<FactionLawLine>> _layouts = new(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyDictionary<string, List<FactionLawLine>> Layouts => _layouts;

    public void Scan(string streamingAssetsRoot)
    {
        _factionLaws.Clear();
        _layouts.Clear();
        var zipPath = Path.Combine(streamingAssetsRoot, "Core.zip");
        if (!File.Exists(zipPath)) return;

        using var zip = ZipFile.OpenRead(zipPath);
        ScanLaws(zip);
        ScanLayouts(zip);
    }

    private void ScanLaws(ZipArchive zip)
    {
        var entries = zip.Entries
            .Where(e => e.FullName.StartsWith("DB/fractions_laws/fractions_laws_table_", StringComparison.OrdinalIgnoreCase)
                && e.FullName.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                && e.Length > 0);

        foreach (var entry in entries)
        {
            try
            {
                // Extract faction from filename (e.g., "fractions_laws_table_human.json" -> "human")
                var fileName = Path.GetFileNameWithoutExtension(entry.Name);
                var faction = fileName.Replace("fractions_laws_table_", "");

                using var stream = entry.Open();
                using var reader = new StreamReader(stream, Encoding.UTF8);
                using var doc = JsonDocument.Parse(reader.ReadToEnd());

                if (!doc.RootElement.TryGetProperty("array", out var array)) continue;

                foreach (var el in array.EnumerateArray())
                {
                    var id = el.TryGetProperty("id", out var idP) ? idP.GetString() ?? "" : "";
                    var icon = el.TryGetProperty("icon", out var iconP) ? iconP.GetString() ?? "" : "";
                    var name = el.TryGetProperty("name", out var nameP) ? nameP.GetString() ?? "" : "";
                    var desc = el.TryGetProperty("desc", out var descP) ? descP.GetString() ?? "" : "";

                    var parametersPerLevel = new List<LevelParameters>();
                    if (el.TryGetProperty("parametersPerLevel", out var pplArr) && pplArr.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var levelEl in pplArr.EnumerateArray())
                        {
                            var cost = levelEl.TryGetProperty("cost", out var costP) ? costP.GetInt32() : 0;

                            var bonuses = new List<BonusEffect>();
                            if (levelEl.TryGetProperty("bonuses", out var bonusesArr) && bonusesArr.ValueKind == JsonValueKind.Array)
                            {
                                foreach (var bonusEl in bonusesArr.EnumerateArray())
                                {
                                    var bonusType = bonusEl.TryGetProperty("type", out var typeP) ? typeP.GetString() ?? "" : "";

                                    var parameters = new List<string>();
                                    if (bonusEl.TryGetProperty("parameters", out var paramsArr) && paramsArr.ValueKind == JsonValueKind.Array)
                                    {
                                        foreach (var param in paramsArr.EnumerateArray())
                                        {
                                            parameters.Add(param.ToString());
                                        }
                                    }

                                    var receivers = new List<string>();
                                    if (bonusEl.TryGetProperty("receivers", out var receiversArr) && receiversArr.ValueKind == JsonValueKind.Array)
                                    {
                                        foreach (var receiver in receiversArr.EnumerateArray())
                                        {
                                            var r = receiver.GetString();
                                            if (!string.IsNullOrWhiteSpace(r))
                                                receivers.Add(r);
                                        }
                                    }

                                    var bonusFraction = bonusEl.TryGetProperty("fraction", out var fractionP) ? fractionP.GetString() ?? "" : "";

                                    if (!string.IsNullOrWhiteSpace(bonusType))
                                        bonuses.Add(new BonusEffect(bonusType, parameters, receivers, bonusFraction));
                                }
                            }

                            parametersPerLevel.Add(new LevelParameters(cost, bonuses));
                        }
                    }

                    if (!string.IsNullOrWhiteSpace(id) && !_factionLaws.ContainsKey(id))
                        _factionLaws[id] = new FactionLawRecord(id, name, desc, icon, faction, parametersPerLevel);
                }
            }
            catch
            {
                // Continue processing other entries - don't let one bad file stop the scan
            }
        }
    }

    private static readonly System.Text.RegularExpressions.Regex FractionFilePattern = new(
        @"^DB/fractions/\d+_([a-z]+)\.json$",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    private void ScanLayouts(ZipArchive zip)
    {
        foreach (var entry in zip.Entries)
        {
            if (entry.Length == 0) continue;
            var match = FractionFilePattern.Match(entry.FullName);
            if (!match.Success) continue;

            try
            {
                var faction = match.Groups[1].Value.ToLowerInvariant();

                using var stream = entry.Open();
                using var reader = new StreamReader(stream, Encoding.UTF8);
                using var doc = JsonDocument.Parse(reader.ReadToEnd());

                if (!doc.RootElement.TryGetProperty("array", out var array)) continue;

                foreach (var el in array.EnumerateArray())
                {
                    if (!el.TryGetProperty("fractionLawsLines", out var linesArr)
                        || linesArr.ValueKind != JsonValueKind.Array)
                        continue;

                    var lines = new List<FactionLawLine>();
                    foreach (var lineEl in linesArr.EnumerateArray())
                    {
                        int countToUnlock = lineEl.TryGetProperty("countToUnlock", out var cP)
                            && cP.ValueKind == JsonValueKind.Number ? cP.GetInt32() : 0;

                        var groups = new List<FactionLawGroup>();
                        if (lineEl.TryGetProperty("groups", out var groupsArr)
                            && groupsArr.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var groupEl in groupsArr.EnumerateArray())
                            {
                                var lawIds = new List<string>();
                                if (groupEl.TryGetProperty("laws", out var lawsArr)
                                    && lawsArr.ValueKind == JsonValueKind.Array)
                                {
                                    foreach (var lawEl in lawsArr.EnumerateArray())
                                    {
                                        var lid = lawEl.GetString();
                                        if (!string.IsNullOrWhiteSpace(lid))
                                            lawIds.Add(lid);
                                    }
                                }
                                groups.Add(new FactionLawGroup(lawIds));
                            }
                        }

                        lines.Add(new FactionLawLine(countToUnlock, groups));
                    }

                    if (lines.Count > 0)
                        _layouts[faction] = lines;

                    break; // single faction per file
                }
            }
            catch
            {
                // Continue on bad file
            }
        }
    }

    /// <summary>
    /// Supplement faction laws with placeholders from Lang files for factions that don't have DB data.
    /// This allows displaying laws like fraction_law_nature_1 even if fractions_laws_table_nature.json doesn't exist.
    /// </summary>
    public void SupplementFromLang(Localization.Indexing.LangIndex langIndex)
    {
        if (langIndex == null)
            return;

        // Pattern: fraction_law_{faction}_{number}_name
        // Example: fraction_law_nature_1_name
        var lawNamePattern = new System.Text.RegularExpressions.Regex(
            @"^fraction_law_([a-z]+)_(\d+)_name$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        int addedCount = 0;
        foreach (var (sid, _) in langIndex.AllEntries())
        {
            var match = lawNamePattern.Match(sid);
            if (!match.Success)
                continue;

            var faction = match.Groups[1].Value;
            var number = match.Groups[2].Value;

            var lawId = $"fraction_law_{faction}_{number}";

            if (_factionLaws.ContainsKey(lawId))
                continue;

            var nameSid = sid;
            var descSid = $"fraction_law_{faction}_{number}_desc";
            var icon = "wip_fraction_law_icon";

            // Cost -1 signals unknown/placeholder value
            var placeholderLevel = new LevelParameters(
                Cost: -1,
                Bonuses: new List<BonusEffect>()
            );

            _factionLaws[lawId] = new FactionLawRecord(
                Id: lawId,
                NameSid: nameSid,
                DescSid: descSid,
                Icon: icon,
                Faction: faction,
                ParametersPerLevel: new List<LevelParameters> { placeholderLevel }
            );
            addedCount++;
        }
    }
}
