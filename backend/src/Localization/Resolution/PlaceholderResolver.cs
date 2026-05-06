using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Localization.DbAccess;
using Localization.Indexing;
using Localization.Scripting;
using Localization.Services;

namespace Localization.Resolution;

/// <summary>
/// Resolves placeholders ({0}, {1}) by evaluating game scripts and substituting calculated values.
/// Resolution pipeline: ScriptInterpreter -> InfoScriptIndex -> &lt;funcName&gt; marker
/// </summary>
public sealed partial class PlaceholderResolver : ITextResolver
{
    private const bool ENABLE_ANNOTATION = true;

    private readonly LangIndex _lang;
    private readonly InfoScriptIndex? _script;
    private readonly ScriptInterpreter? _interpreter;
    private readonly DbAccessor? _db;
    private readonly OverlayService _overlays;

    private readonly ConcurrentDictionary<string, (string text, ResolutionTrace trace)> _memo = new(StringComparer.Ordinal);

    [GeneratedRegex(@"\{(\d+)\}", RegexOptions.Compiled)]
    private static partial Regex PlaceholderRx();

    public PlaceholderResolver(LangIndex lang, InfoScriptIndex? script, ScriptInterpreter? interpreter, DbAccessor? db) : this(lang, script, interpreter, db, OverlayService.Instance) { }

    public PlaceholderResolver(LangIndex lang, InfoScriptIndex? script, ScriptInterpreter? interpreter, DbAccessor? db, OverlayService overlays)
    {
        _lang = lang ?? throw new ArgumentNullException(nameof(lang));
        _script = script;
        _interpreter = interpreter;
        _db = db;
        _overlays = overlays ?? throw new ArgumentNullException(nameof(overlays));
    }

    public string Resolve(string sid, ResolutionContext ctx, out ResolutionTrace trace)
    {
        var overlayText = _overlays.TryResolveFromOverlay(sid, ctx.Locale);
        if (!string.IsNullOrEmpty(overlayText))
        {
            trace = new ResolutionTrace(sid, Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>());
            return overlayText;
        }

        bool cacheable = ctx.UnitId is null && ctx.AbilityIndex is null && ctx.IsActiveAbility is null &&
                         ctx.HeroSpecializationId is null && ctx.SkillId is null && ctx.SubSkillId is null &&
                         ctx.BuffId is null && ctx.ItemId is null && ctx.MagicId is null &&
                         ctx.FractionId is null && ctx.LawId is null && ctx.MapObjectId is null;
        if (cacheable && _memo.TryGetValue(sid, out var hit))
        {
            trace = hit.trace;
            return hit.text;
        }

        var usedSids = new List<string>();
        var usedFns = new List<string>();
        var warns = new List<string>();
        var errs = new List<string>();

        var text = _lang.ResolveText(sid);
        if (text is null)
        {
            errs.Add($"UnknownSid:{sid}");
            trace = new(sid, usedSids, usedFns, warns, errs);
            return sid;
        }

        var indices = PlaceholderIndices(text);
        if (indices.Count == 0)
        {
            trace = new(sid, usedSids, usedFns, warns, errs);
            return text;
        }

        var args = _lang.GetArgs(sid);
        if (args.Length == 0)
        {
            if (ctx.BuffId != null && _db != null)
            {
                var extractedArgs = TryExtractBuffArgs(ctx.BuffId, sid, indices);
                if (extractedArgs != null && extractedArgs.Length > 0)
                {
                    args = extractedArgs;
                }
                else
                {
                    errs.Add($"MissingArgsForSid:{sid}");
                    trace = new(sid, usedSids, usedFns, warns, errs);
                    return text;
                }
            }
            else
            {
                errs.Add($"MissingArgsForSid:{sid}");
                trace = new(sid, usedSids, usedFns, warns, errs);
                return text;
            }
        }

        string Eval(string expr)
        {
            if (string.IsNullOrWhiteSpace(expr)) return "";

            var trimmed = expr.Trim();

            if (trimmed.Length >= 2 && trimmed[0] == '"' && trimmed[^1] == '"')
                return trimmed.Substring(1, trimmed.Length - 2);

            if (double.TryParse(trimmed, NumberStyles.Any, CultureInfo.InvariantCulture, out var numValue))
                return numValue.ToString(CultureInfo.InvariantCulture);

            var split = expr.Split('|', 2);
            if (split.Length == 2)
            {
                var leftExpr = split[0];
                var left = Eval(leftExpr);
                var altSid = split[1];
                usedSids.Add(altSid);

                var altText = _lang.ResolveText(altSid);
                if (string.IsNullOrEmpty(altText)) { errs.Add($"UnknownAltSid:{altSid}"); return ""; }

                var altPh = PlaceholderIndices(altText);
                if (altPh.Count == 0) return altText;

                var altArgs = _lang.GetArgs(altSid);
                var map = new Dictionary<int, string>();

                bool useLeftForZero = altPh.Contains(0) && (altArgs.Length < altPh.Count);

                if (useLeftForZero)
                {
                    if (altText.IndexOf('%') >= 0 && !leftExpr.EndsWith("_add", StringComparison.Ordinal))
                    {
                        var addExpr = leftExpr + "_add";
                        var addVal = Eval(addExpr);
                        if (!string.IsNullOrEmpty(addVal) && !addVal.StartsWith("<"))
                            left = addVal;
                    }
                    map[0] = left;
                }

                foreach (var j in altPh.Where(n => n > 0 || !useLeftForZero))
                {
                    var k = useLeftForZero ? j - 1 : j;
                    if (k >= altArgs.Length) { errs.Add($"AltIncomplete:{altSid} missing {j}"); return altText; }
                    map[j] = Eval(altArgs[k]);
                }
                return Substitute(altText, map, ENABLE_ANNOTATION);
            }

            usedFns.Add(expr);

            if (_interpreter != null && _interpreter.TryEvaluate(expr, ctx, out var evalVal) && evalVal != null)
                return evalVal!;

            var lit = _script?.TryGetConstant(expr);
            if (lit != null) return lit;
            return $"<{expr}>";
        }

        var topMap = new Dictionary<int, string>();
        bool skipNormalMapping = false;
        if (args.Length == 1 && indices.Count > 1)
        {
            var singleValue = Eval(args[0]);
            if (!string.IsNullOrEmpty(singleValue) && !singleValue.StartsWith("<"))
            {
                foreach (var i in indices.Distinct())
                    topMap[i] = singleValue;
                skipNormalMapping = true;
            }
        }

        if (!skipNormalMapping)
        {
            foreach (var i in indices.Distinct().OrderBy(x => x))
            {
                if (i >= args.Length) { errs.Add($"MissingArgIndex:{sid}[{i}]"); continue; }
                topMap[i] = Eval(args[i]);
            }
        }

        var result = Substitute(text, topMap, ENABLE_ANNOTATION);
        trace = new(sid, usedSids, usedFns, warns, errs);

        if (cacheable)
            _memo[sid] = (result, trace);

        return result;
    }

    private static List<int> PlaceholderIndices(string text)
        => PlaceholderRx().Matches(text).Select(m => int.Parse(m.Groups[1].Value)).ToList();

    private static string Substitute(string text, IDictionary<int, string> values, bool annotate = false)
    {
        if (!annotate)
        {
            var res = text;
            foreach (var kv in values)
                res = res.Replace("{" + kv.Key + "}", kv.Value ?? "");
            return res;
        }

        var result = text;
        foreach (var kv in values.OrderByDescending(x => x.Key))
        {
            var placeholder = "{" + kv.Key + "}";
            var value = kv.Value ?? "";

            string replacement;

            if (value.Contains("<resolved>") || value.Contains("<unresolved>"))
                replacement = value;
            else if (value.StartsWith("<") && value.EndsWith(">"))
            {
                var encoded = value.Replace("<", "&lt;").Replace(">", "&gt;");
                replacement = $"<unresolved>{encoded}</unresolved>";
            }
            else if (!string.IsNullOrWhiteSpace(value))
                replacement = $"<resolved>{value}</resolved>";
            else
                replacement = value;

            result = result.Replace(placeholder, replacement);
        }

        result = PlaceholderRx().Replace(result, match => $"<unresolved>{match.Value}</unresolved>");
        return result;
    }

    private string[]? TryExtractBuffArgs(string buffId, string sid, List<int> indices)
    {
        if (_db == null) return null;
        if (!_db.TryGetBuff(buffId, out var buffJson)) return null;

        // Workaround: black dragon's revenge_damage mechanic has missing args definition
        if (sid.Contains("black_dragon", StringComparison.OrdinalIgnoreCase))
        {
            if (buffJson.TryGetProperty("actions", out var actions) && actions.ValueKind == JsonValueKind.Array)
            {
                foreach (var action in actions.EnumerateArray())
                {
                    if (action.TryGetProperty("damageDealer", out var damageDealer) &&
                        damageDealer.TryGetProperty("targetMechanics", out var mechanics) &&
                        mechanics.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var mech in mechanics.EnumerateArray())
                        {
                            var mechNameStr = mech.TryGetProperty("mech", out var mechName) ? mechName.GetString() : null;
                            if (mechNameStr == "revenge_damage" &&
                                mech.TryGetProperty("values", out var values) &&
                                values.ValueKind == JsonValueKind.Array &&
                                values.GetArrayLength() >= 4)
                            {
                                var maxDmgValue = values[3];
                                string? maxDmg = maxDmgValue.ValueKind switch
                                {
                                    JsonValueKind.String => maxDmgValue.GetString(),
                                    JsonValueKind.Number => maxDmgValue.TryGetDouble(out var dmgNum)
                                        ? dmgNum.ToString("F0", CultureInfo.InvariantCulture)
                                        : null,
                                    _ => null
                                };
                                if (!string.IsNullOrEmpty(maxDmg)) return new[] { maxDmg };
                            }
                        }
                    }
                }
            }
        }

        // Workaround: reality distortion's take_accumulated_damage has missing args definition
        if (sid.Contains("reality_distortion", StringComparison.OrdinalIgnoreCase))
        {
            if (buffJson.TryGetProperty("actions", out var actions) && actions.ValueKind == JsonValueKind.Array)
            {
                foreach (var action in actions.EnumerateArray())
                {
                    if (action.TryGetProperty("damageDealer", out var damageDealer) &&
                        damageDealer.TryGetProperty("targetMechanics", out var mechanics) &&
                        mechanics.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var mech in mechanics.EnumerateArray())
                        {
                            if (mech.TryGetProperty("mech", out var mechName) &&
                                mechName.GetString() == "take_accumulated_damage" &&
                                mech.TryGetProperty("values", out var values) &&
                                values.ValueKind == JsonValueKind.Array)
                            {
                                var firstValue = values[0];
                                double damageRatio;

                                if (firstValue.ValueKind == JsonValueKind.Number && firstValue.TryGetDouble(out damageRatio))
                                {
                                    var percentage = (damageRatio * 100).ToString("F0", CultureInfo.InvariantCulture);
                                    return new[] { percentage };
                                }
                                else if (firstValue.ValueKind == JsonValueKind.String &&
                                    double.TryParse(firstValue.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out damageRatio))
                                {
                                    var percentage = (damageRatio * 100).ToString("F0", CultureInfo.InvariantCulture);
                                    return new[] { percentage };
                                }
                            }
                        }
                    }
                }
            }
        }

        return null;
    }
}
