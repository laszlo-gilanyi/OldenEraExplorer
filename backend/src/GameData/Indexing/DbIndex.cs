using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace GameData.Indexing;

/// <summary>
/// Reads and indexes unit data from Core.zip, merging VIEW files (visual data: names, icons, abilities)
/// with LOGIC files (gameplay data: stats, costs, faction). Handles unit variants (_upg, _alt suffixes)
/// and enriches units with growth data from city building files.
/// </summary>
public sealed class DbIndex
{
    public sealed record AbilityRef(
        string NameSid,
        string DescriptionSid,
        string Icon,
        string AbilityTypeSid,
        bool IsActiveAbility,
        bool IsAlternativeAttack,
        int? Rank,
        int? Energy,
        IReadOnlyList<string> ImmunitySids,
        IReadOnlyList<string> InfoDescriptionSids
    );

    public sealed record UnitRecord(
        string Id,
        string Fraction,
        int Tier,
        string? Mesh,
        float? Scale,
        IReadOnlyList<AbilityRef> Abilities,
        IReadOnlyList<AbilityRef> Passives,
        string BaseClassNameSid,
        string BaseClassDescSid,
        string BaseClassIcon,
        IReadOnlyDictionary<string, string> Stats,
        int? Growth,
        IReadOnlyList<UnitCostEntry> Cost,
        string? UpgradeSid
    );

    public sealed record UnitCostEntry(string ResourceKey, int Amount);

    private readonly string _streamingAssetsRoot;
    private IReadOnlyList<UnitRecord>? _cachedUnits;
    private readonly object _cacheLock = new object();

    public DbIndex(string streamingAssetsRoot)
        => _streamingAssetsRoot = streamingAssetsRoot ?? throw new ArgumentNullException(nameof(streamingAssetsRoot));

    public IEnumerable<UnitRecord> LoadUnits()
    {
        if (_cachedUnits != null)
            return _cachedUnits;

        lock (_cacheLock)
        {
            if (_cachedUnits != null)
                return _cachedUnits;

            var units = LoadUnitsInternal().ToList();
            EnrichWithGrowth(units);
            _cachedUnits = units;
            return _cachedUnits;
        }
    }

    private IEnumerable<UnitRecord> LoadUnitsInternal()
    {
        var coreZipPath = Path.Combine(_streamingAssetsRoot, "Core.zip");
        if (!File.Exists(coreZipPath))
            yield break;

        using var zip = ZipFile.OpenRead(coreZipPath);

        static string StripSuffix(string name, string suffix)
            => name.EndsWith(suffix, StringComparison.Ordinal)
                ? name.Substring(0, name.Length - suffix.Length)
                : name;

        var viewEntries = zip.Entries.Where(e =>
                e.FullName.StartsWith("DB/units/units_views/", StringComparison.OrdinalIgnoreCase)
                && e.FullName.EndsWith("_v.json", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var logicEntries = zip.Entries.Where(e =>
                e.FullName.StartsWith("DB/units/units_logics/", StringComparison.OrdinalIgnoreCase)
                && e.FullName.EndsWith("_l.json", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(
                e => StripSuffix(Path.GetFileNameWithoutExtension(e.FullName), "_l"),
                e => e,
                StringComparer.Ordinal
            );

        foreach (var v in viewEntries)
        {
            var key = StripSuffix(Path.GetFileNameWithoutExtension(v.FullName), "_v");

            ZipArchiveEntry? logicEntry = null;
            foreach (var candidate in BuildLogicCandidates(key))
            {
                if (logicEntries.TryGetValue(candidate, out logicEntry))
                    break;
            }

            var viewRootOpt = ParseJsonBOMTolerant(v);
            var logicRootOpt = logicEntry != null ? ParseJsonBOMTolerant(logicEntry) : (JsonElement?)null;
            if (viewRootOpt is null) continue;

            var viewRoot = viewRootOpt.Value;
            if (!viewRoot.TryGetProperty("array", out var vArr) || vArr.GetArrayLength() == 0)
                continue;

            var vUnit = vArr[0];

            // Level 1 filter: Skip unit variants (name_ override or specific test units)
            if (vUnit.TryGetProperty("name_", out _))
                continue;

            if (vUnit.TryGetProperty("id", out var idProp) &&
                idProp.GetString() == "dragon_upg_alt")
                continue;

            JsonElement? lUnit = null;
            if (logicRootOpt is JsonElement lr &&
                lr.TryGetProperty("array", out var lArr) && lArr.GetArrayLength() > 0)
            {
                lUnit = lArr[0];
            }

            var fraction = lUnit is JsonElement le && TryGetStringAny(le, out var frac, "fraction", "faction")
                ? (frac ?? "")
                : "";

            var tier = lUnit is JsonElement le2 && TryGetIntAny(le2, out var ti, "tier", "unitTier")
                ? ti
                : 0;

            string? mesh = null;
            if (vUnit.TryGetProperty("mesh", out var meshEl) && meshEl.ValueKind == JsonValueKind.String)
                mesh = meshEl.GetString();

            float? scale = null;
            if (vUnit.TryGetProperty("scale", out var scaleEl) && scaleEl.ValueKind == JsonValueKind.Number)
            {
                if (scaleEl.TryGetSingle(out var s))
                    scale = s;
            }

            string baseNameSid = "";
            string baseDescSid = "";
            string baseIcon = "";
            if (vUnit.TryGetProperty("baseClass", out var bc))
            {
                JsonElement? baseClassElement = null;

                // Game data uses inconsistent formats: both object and array[0] with single object
                if (bc.ValueKind == JsonValueKind.Object)
                {
                    baseClassElement = bc;
                }
                else if (bc.ValueKind == JsonValueKind.Array)
                {
                    var arr = bc.EnumerateArray().ToList();
                    if (arr.Count > 0 && arr[0].ValueKind == JsonValueKind.Object)
                        baseClassElement = arr[0];
                }

                if (baseClassElement.HasValue)
                {
                    if (baseClassElement.Value.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String)
                        baseNameSid = n.GetString() ?? "";
                    if (baseClassElement.Value.TryGetProperty("description", out var d) && d.ValueKind == JsonValueKind.String)
                        baseDescSid = d.GetString() ?? "";
                    if (baseClassElement.Value.TryGetProperty("icon", out var ic) && ic.ValueKind == JsonValueKind.String)
                        baseIcon = ic.GetString() ?? "";
                }
            }

            var abilities = new List<AbilityRef>();

            var viewAbilities = vUnit.TryGetProperty("abilities", out var aEl) && aEl.ValueKind == JsonValueKind.Array
                ? aEl.EnumerateArray().ToList()
                : new List<JsonElement>();

            var logicAbilities = lUnit is JsonElement lEl1 && lEl1.TryGetProperty("abilities", out var laEl) && laEl.ValueKind == JsonValueKind.Array
                ? laEl.EnumerateArray().ToList()
                : new List<JsonElement>();

            var maxA = Math.Max(viewAbilities.Count, logicAbilities.Count);
            for (int i = 0; i < maxA; i++)
            {
                var vA = i < viewAbilities.Count ? viewAbilities[i] : default;
                var lA = i < logicAbilities.Count ? logicAbilities[i] : default;

                string nameSid = vA.ValueKind == JsonValueKind.Object && vA.TryGetProperty("name", out var vn) ? (vn.GetString() ?? "") : "";
                string descSid = vA.ValueKind == JsonValueKind.Object && vA.TryGetProperty("description", out var vd) ? (vd.GetString() ?? "") : "";

                string icon = "";
                if (vA.ValueKind == JsonValueKind.Object && vA.TryGetProperty("icon", out var ic) && ic.ValueKind == JsonValueKind.String)
                    icon = ic.GetString() ?? "";

                string abilityTypeSid = "";
                if (vA.ValueKind == JsonValueKind.Object && vA.TryGetProperty("abilityType", out var at) && at.ValueKind == JsonValueKind.String)
                    abilityTypeSid = at.GetString() ?? "";

                int? rank = null;
                int? energy = null;
                if (lA.ValueKind == JsonValueKind.Object)
                {
                    if (lA.TryGetProperty("rank", out var rk) && rk.TryGetInt32(out var rki)) rank = rki;
                    if (lA.TryGetProperty("energyLevel", out var en) && en.TryGetInt32(out var eni)) energy = eni;
                }

                var immSids = CollectImmunitySidsFromView(vA);
                var infoSids = CollectInfoDescriptionSidsFromView(vA);

                if (!string.IsNullOrWhiteSpace(nameSid))
                    abilities.Add(new AbilityRef(nameSid, descSid, icon, abilityTypeSid, true, false, rank, energy, immSids, infoSids));
            }

            var viewAlts = vUnit.TryGetProperty("alternativeAttacks", out var alt) && alt.ValueKind == JsonValueKind.Array
                ? alt.EnumerateArray().ToList()
                : new List<JsonElement>();

            var logicAlts = lUnit is JsonElement lEl2 && lEl2.TryGetProperty("alternativeAttacks", out var lalt) && lalt.ValueKind == JsonValueKind.Array
                ? lalt.EnumerateArray().ToList()
                : new List<JsonElement>();

            var maxAlt = Math.Max(viewAlts.Count, logicAlts.Count);
            for (int i = 0; i < maxAlt; i++)
            {
                var vAlt = i < viewAlts.Count ? viewAlts[i] : default;
                var lAlt = i < logicAlts.Count ? logicAlts[i] : default;

                string nameSid = vAlt.ValueKind == JsonValueKind.Object && vAlt.TryGetProperty("name", out var vn) ? (vn.GetString() ?? "") : "";
                string descSid = vAlt.ValueKind == JsonValueKind.Object && vAlt.TryGetProperty("description", out var vd) ? (vd.GetString() ?? "") : "";

                string icon = "";
                if (vAlt.ValueKind == JsonValueKind.Object && vAlt.TryGetProperty("icon", out var ic) && ic.ValueKind == JsonValueKind.String)
                    icon = ic.GetString() ?? "";

                string abilityTypeSid = "";
                if (vAlt.ValueKind == JsonValueKind.Object && vAlt.TryGetProperty("abilityType", out var at) && at.ValueKind == JsonValueKind.String)
                    abilityTypeSid = at.GetString() ?? "";

                if (!ShouldIncludeAlternative(vAlt, nameSid, descSid))
                    continue;

                int? rank = null;
                int? energy = null;
                if (lAlt.ValueKind == JsonValueKind.Object)
                {
                    if (lAlt.TryGetProperty("rank", out var rk) && rk.TryGetInt32(out var rki)) rank = rki;
                    if (lAlt.TryGetProperty("dontUseEnergy", out var due) && due.ValueKind == JsonValueKind.True)
                        energy = 0;
                    else if (lAlt.TryGetProperty("energyLevel", out var en) && en.TryGetInt32(out var eni))
                        energy = eni;
                }

                var immSids = CollectImmunitySidsFromView(vAlt);
                var infoSids = CollectInfoDescriptionSidsFromView(vAlt);

                if (!string.IsNullOrWhiteSpace(nameSid))
                    abilities.Add(new AbilityRef(nameSid, descSid, icon, abilityTypeSid, true, true, rank, energy, immSids, infoSids));
            }

            var passives = new List<AbilityRef>();
            var viewPassives = vUnit.TryGetProperty("passives", out var vp) && vp.ValueKind == JsonValueKind.Array
                ? vp.EnumerateArray().ToList()
                : new List<JsonElement>();

            foreach (var vP in viewPassives)
            {
                string nameSid = vP.ValueKind == JsonValueKind.Object && vP.TryGetProperty("name", out var vn) ? (vn.GetString() ?? "") : "";
                string descSid = vP.ValueKind == JsonValueKind.Object && vP.TryGetProperty("description", out var vd) ? (vd.GetString() ?? "") : "";

                string icon = "";
                if (vP.ValueKind == JsonValueKind.Object && vP.TryGetProperty("icon", out var ic) && ic.ValueKind == JsonValueKind.String)
                    icon = ic.GetString() ?? "";

                string abilityTypeSid = "";
                if (vP.ValueKind == JsonValueKind.Object && vP.TryGetProperty("abilityType", out var at) && at.ValueKind == JsonValueKind.String)
                    abilityTypeSid = at.GetString() ?? "";

                var immSids = CollectImmunitySidsFromView(vP);
                var infoSids = CollectInfoDescriptionSidsFromView(vP);

                if (!string.IsNullOrWhiteSpace(nameSid))
                    passives.Add(new AbilityRef(nameSid, descSid, icon, abilityTypeSid, false, false, null, null, immSids, infoSids));
            }

            var stats = lUnit is JsonElement le4 ? ExtractStats(le4) : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var cost = lUnit is JsonElement le5 ? ExtractCost(le5) : Array.Empty<UnitCostEntry>();
            int? growth = null;

            string? upgradeSid = null;
            if (lUnit is JsonElement le6 && le6.TryGetProperty("upgradeSid", out var usEl) && usEl.ValueKind == JsonValueKind.String)
                upgradeSid = usEl.GetString();

            yield return new UnitRecord(key, fraction, tier, mesh, scale, abilities, passives, baseNameSid, baseDescSid, baseIcon, stats, growth, cost, upgradeSid);
        }
    }

    private static bool ShouldIncludeAlternative(JsonElement vAlt, string nameSid, string descSid)
    {
        if (vAlt.ValueKind == JsonValueKind.Object && vAlt.TryGetProperty("canShowOnUI", out var show) && show.ValueKind == JsonValueKind.False)
            return false;

        var blacklist = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "common_attack_1_name", "common_attack_2_name", "common_attack_name",
            "common_attack_1_description", "common_attack_2_description", "common_attack_description"
        };

        if (blacklist.Contains(nameSid) || blacklist.Contains(descSid))
            return false;

        return true;
    }

    private static List<string> CollectImmunitySidsFromView(JsonElement vAbility)
    {
        var list = new List<string>();
        if (vAbility.ValueKind != JsonValueKind.Object) return list;

        if (vAbility.TryGetProperty("excaptionInTooltip", out var ex1))
            AddSidToken(ex1, list);
        if (vAbility.TryGetProperty("exceptionInTooltip", out var ex2))
            AddSidToken(ex2, list);

        return list;
    }

    private static List<string> CollectInfoDescriptionSidsFromView(JsonElement vAbility)
    {
        var list = new List<string>();
        if (vAbility.ValueKind != JsonValueKind.Object) return list;

        if (vAbility.TryGetProperty("infoDescription", out var info))
            AddSidToken(info, list);

        return list;
    }

    private static void AddSidToken(JsonElement token, List<string> target)
    {
        if (token.ValueKind == JsonValueKind.String)
        {
            var s = token.GetString();
            if (!string.IsNullOrWhiteSpace(s)) target.Add(s!);
        }
        else if (token.ValueKind == JsonValueKind.Array)
        {
            foreach (var el in token.EnumerateArray())
            {
                if (el.ValueKind == JsonValueKind.String)
                {
                    var s = el.GetString();
                    if (!string.IsNullOrWhiteSpace(s)) target.Add(s!);
                }
            }
        }
    }

    private static JsonElement? ParseJsonBOMTolerant(ZipArchiveEntry entry)
    {
        using var s = entry.Open();
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        var raw = ms.ToArray();

        int offset = raw.Length >= 3 && raw[0] == 0xEF && raw[1] == 0xBB && raw[2] == 0xBF ? 3 : 0;
        var text = Encoding.UTF8.GetString(raw, offset, raw.Length - offset);

        using var doc = JsonDocument.Parse(text);
        return doc.RootElement.Clone();
    }

    private static bool TryGetStringAny(JsonElement obj, out string? value, params string[] names)
    {
        foreach (var n in names)
        {
            if (obj.TryGetProperty(n, out var el) && el.ValueKind == JsonValueKind.String)
            {
                value = el.GetString();
                return true;
            }
        }
        value = null;
        return false;
    }

    private static bool TryGetIntAny(JsonElement obj, out int value, params string[] names)
    {
        foreach (var n in names)
        {
            if (obj.TryGetProperty(n, out var el) && el.TryGetInt32(out var i))
            {
                value = i;
                return true;
            }
        }
        value = 0;
        return false;
    }

    private static IReadOnlyDictionary<string, string> ExtractStats(JsonElement logicUnit)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (logicUnit.TryGetProperty("stats", out var stats) && stats.ValueKind == JsonValueKind.Object)
        {
            foreach (var p in stats.EnumerateObject())
            {
                var key = p.Name;
                var val = p.Value;

                string? s = null;
                if (val.ValueKind == JsonValueKind.Number)
                {
                    if (val.TryGetInt32(out var i)) s = i.ToString();
                    else if (val.TryGetDouble(out var d)) s = d.ToString(CultureInfo.InvariantCulture);
                }
                else if (val.ValueKind == JsonValueKind.String)
                {
                    s = val.GetString();
                }

                if (s is null) continue;

                switch (key.ToLowerInvariant())
                {
                    case "hp": dict["health"] = s; break;
                    case "offence":
                    case "offense": dict["attack"] = s; break;
                    case "defence":
                    case "defense": dict["defence"] = s; break;
                    case "damage_min":
                    case "min":
                    case "mindamage": dict["damageMin"] = s; break;
                    case "damage_max":
                    case "max":
                    case "maxdamage": dict["damageMax"] = s; break;
                    case "initiative": dict["initiative"] = s; break;
                    case "speed": dict["speed"] = s; break;
                    case "luck": dict["luck"] = s; break;
                    case "moral":
                    case "morale": dict["morale"] = s; break;
                    case "armor":
                    case "armour": dict["armor"] = s; break;
                    default:
                        dict[key] = s;
                        break;
                }
            }
        }

        if (logicUnit.TryGetProperty("growth", out var growth))
        {
            dict["growth"] = growth.ValueKind == JsonValueKind.Number && growth.TryGetInt32(out var gi)
                ? gi.ToString()
                : growth.ToString();
        }
        if (logicUnit.TryGetProperty("cost", out var cost))
        {
            if (cost.ValueKind == JsonValueKind.Number && cost.TryGetInt32(out var ci))
                dict["cost"] = ci.ToString();
            else if (cost.ValueKind == JsonValueKind.Number && cost.TryGetDouble(out var cd))
                dict["cost"] = cd.ToString(CultureInfo.InvariantCulture);
            else if (cost.ValueKind == JsonValueKind.String)
                dict["cost"] = cost.GetString() ?? "";
        }
        if (logicUnit.TryGetProperty("squadValue", out var sv) && sv.TryGetInt32(out var svi))
            dict["squadValue"] = svi.ToString();
        if (logicUnit.TryGetProperty("expBonus", out var eb) && eb.TryGetInt32(out var ebi))
            dict["expBonus"] = ebi.ToString();

        return dict;
    }

    private static IReadOnlyList<UnitCostEntry> ExtractCost(JsonElement logicUnit)
    {
        var costs = new List<UnitCostEntry>();

        if (logicUnit.TryGetProperty("unitCost", out var costObj) &&
            costObj.TryGetProperty("costResArray", out var costArray) &&
            costArray.ValueKind == JsonValueKind.Array)
        {
            foreach (var c in costArray.EnumerateArray())
            {
                var res = c.TryGetProperty("name", out var nEl) && nEl.ValueKind == JsonValueKind.String ? nEl.GetString() ?? "" : "";
                var val = c.TryGetProperty("cost", out var vEl) && vEl.TryGetInt32(out var v) ? v : 0;

                if (!string.IsNullOrWhiteSpace(res) && val > 0)
                    costs.Add(new UnitCostEntry(res, val));
            }
        }

        return costs;
    }

    private void EnrichWithGrowth(List<UnitRecord> units)
    {
        var coreZipPath = Path.Combine(_streamingAssetsRoot, "Core.zip");
        if (!File.Exists(coreZipPath)) return;

        var unitLookup = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < units.Count; i++)
            unitLookup[units[i].Id] = i;

        var baseToUnits = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        foreach (var unit in units)
        {
            var baseId = GetBaseUnitId(unit.Id);
            if (!baseToUnits.TryGetValue(baseId, out var list))
            {
                list = new List<int>();
                baseToUnits[baseId] = list;
            }
            if (unitLookup.TryGetValue(unit.Id, out var idx))
                list.Add(idx);
        }

        var candidates = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var unit in units)
        {
            var baseId = GetBaseUnitId(unit.Id);
            foreach (var candidate in BuildGrowthCandidates(unit.Id))
            {
                if (!candidates.ContainsKey(candidate))
                    candidates[candidate] = baseId;
            }
        }

        using var zip = ZipFile.OpenRead(coreZipPath);

        foreach (var entry in zip.Entries)
        {
            var fn = entry.FullName.Replace('\\', '/');
            if (!fn.StartsWith("DB/objects_logic/cities/", StringComparison.OrdinalIgnoreCase)) continue;
            if (!fn.EndsWith("_city.json", StringComparison.OrdinalIgnoreCase)) continue;

            try
            {
                using var s = entry.Open();
                using var doc = JsonDocument.Parse(s);
                if (!doc.RootElement.TryGetProperty("array", out var arr) || arr.ValueKind != JsonValueKind.Array)
                    continue;

                foreach (var cityRoot in arr.EnumerateArray())
                {
                    var growthEntries = new List<(string candidate, int growth)>();
                    CollectAllGrowthFromCity(cityRoot, candidates, growthEntries);

                    foreach (var (matchedCandidate, growth) in growthEntries)
                    {
                        var baseId = candidates[matchedCandidate];
                        if (baseToUnits.TryGetValue(baseId, out var unitIndices))
                        {
                            foreach (var index in unitIndices)
                                units[index] = units[index] with { Growth = growth };
                        }
                    }
                }
            }
            catch { }
        }
    }

    private void CollectAllGrowthFromCity(JsonElement node, Dictionary<string, string> candidates, List<(string candidate, int growth)> results)
    {
        switch (node.ValueKind)
        {
            case JsonValueKind.Object:
                {
                    var sids = ExtractSidsFromCity(node);
                    if (sids.Count > 0 && TryGetWeekly(node, out var w))
                    {
                        foreach (var sid in sids)
                        {
                            if (candidates.ContainsKey(sid))
                                results.Add((sid, w));
                        }
                    }

                    foreach (var prop in node.EnumerateObject())
                        CollectAllGrowthFromCity(prop.Value, candidates, results);
                    break;
                }

            case JsonValueKind.Array:
                {
                    foreach (var item in node.EnumerateArray())
                        CollectAllGrowthFromCity(item, candidates, results);
                    break;
                }
        }
    }

    private bool TryGetWeekly(JsonElement obj, out int weekly)
    {
        weekly = 0;
        if (obj.ValueKind != JsonValueKind.Object) return false;

        foreach (var p in obj.EnumerateObject())
        {
            if (!p.Name.Equals("weeklyIncrement", StringComparison.OrdinalIgnoreCase))
                continue;

            var v = p.Value;
            if (v.ValueKind == JsonValueKind.Number)
            {
                if (v.TryGetInt32(out var n)) { weekly = n; return true; }
                if (v.TryGetDouble(out var d)) { weekly = (int)Math.Round(d); return true; }
            }
        }

        return false;
    }

    private HashSet<string> ExtractSidsFromCity(JsonElement obj)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);

        if (obj.ValueKind != JsonValueKind.Object) return set;
        if (!obj.TryGetProperty("sids", out var sidsEl)) return set;

        if (sidsEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var it in sidsEl.EnumerateArray())
            {
                switch (it.ValueKind)
                {
                    case JsonValueKind.String:
                        {
                            var s = it.GetString();
                            if (!string.IsNullOrWhiteSpace(s))
                                set.Add(s!);
                            break;
                        }
                    case JsonValueKind.Object:
                        {
                            if (it.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.String)
                            {
                                var s = idEl.GetString();
                                if (!string.IsNullOrWhiteSpace(s)) set.Add(s!);
                            }
                            else if (it.TryGetProperty("sid", out var sidEl) && sidEl.ValueKind == JsonValueKind.String)
                            {
                                var s = sidEl.GetString();
                                if (!string.IsNullOrWhiteSpace(s)) set.Add(s!);
                            }
                            break;
                        }
                }
            }
        }

        return set;
    }

    private static string GetBaseUnitId(string unitId)
    {
        var b = unitId;
        if (b.EndsWith("_upg_alt", StringComparison.Ordinal)) b = b[..^8];
        else if (b.EndsWith("_upg", StringComparison.Ordinal)) b = b[..^4];
        else if (b.EndsWith("_alt", StringComparison.Ordinal)) b = b[..^4];
        return b;
    }

    /// <summary>
    /// Builds candidate IDs for finding logic files (cost, stats, etc.).
    /// Matches the original UnitCostResolver.BuildCandidates logic.
    /// </summary>
    private static IEnumerable<string> BuildLogicCandidates(string unitId)
    {
        yield return unitId;
        var baseId = unitId;
        if (baseId.EndsWith("_upg_alt", StringComparison.Ordinal)) baseId = baseId[..^8];
        else if (baseId.EndsWith("_upg", StringComparison.Ordinal)) baseId = baseId[..^4];
        else if (baseId.EndsWith("_alt", StringComparison.Ordinal)) baseId = baseId[..^4];
        if (baseId != unitId)
        {
            yield return baseId;
            yield return baseId + "_upg";
            yield return baseId + "_upg_alt";
        }
    }

    private IEnumerable<string> BuildGrowthCandidates(string unitId)
    {
        var hs = new HashSet<string>(StringComparer.Ordinal);
        var baseId = GetBaseUnitId(unitId);

        void AddVariants(string core)
        {
            hs.Add(core);
            hs.Add(core + "_upg");
            hs.Add(core + "_upg_alt");
            hs.Add(core + "_alt");
        }

        AddVariants(unitId);
        AddVariants(baseId);

        var prefixes = new[] { "human_", "humans_", "undead_", "dungeon_", "unfrozen_", "nature_", "demons_", "neutral_" };
        foreach (var p in prefixes)
            AddVariants(p + baseId);

        return hs;
    }
}
