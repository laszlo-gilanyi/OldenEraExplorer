using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Localization.Indexing;

/// <summary>Loads text tokens from Lang/{locale}/texts/*.json files.</summary>
public sealed partial class LangIndex
{
    public sealed record ResolvedEntry(
        string Sid,
        string Text,
        string SourceFile,
        string Category,
        bool HasPlaceholders
    );

    private readonly string _streamingAssetsRoot;
    private readonly string _locale;
    private readonly bool _fallbackToEnglish;

    public string Locale => _locale;
    public string StreamingAssetsRoot => _streamingAssetsRoot;

    private readonly Dictionary<string, ResolvedEntry> _bySid = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string[]> _argsBySid = new(StringComparer.Ordinal);

    [GeneratedRegex(@"\{[^}]+\}", RegexOptions.Compiled)]
    private static partial Regex PlaceholderRx();

    public LangIndex(string streamingAssetsRoot, string locale, bool fallbackToEnglish = false)
    {
        _streamingAssetsRoot = streamingAssetsRoot ?? throw new ArgumentNullException(nameof(streamingAssetsRoot));
        _locale = string.IsNullOrWhiteSpace(locale) ? "english" : locale;
        _fallbackToEnglish = fallbackToEnglish;
    }

    public int Count => _bySid.Count;

    public void Load()
    {
        _bySid.Clear();
        _argsBySid.Clear();

        LoadTextsForLocale(_locale, overwriteExisting: true);

        if (_fallbackToEnglish && !string.Equals(_locale, "english", StringComparison.OrdinalIgnoreCase))
            LoadTextsForLocale("english", overwriteExisting: false);

        var argsDir = Path.Combine(_streamingAssetsRoot, "Lang", "args");
        if (Directory.Exists(argsDir))
        {
            foreach (var jsonPath in Directory.EnumerateFiles(argsDir, "*.json"))
            {
                using var fs = File.OpenRead(jsonPath);
                LoadArgsFromStream(fs);
            }
        }
        else
        {
            // EA build: args are inside Core.zip
            var coreZipPath = Path.Combine(_streamingAssetsRoot, "Core.zip");
            if (File.Exists(coreZipPath))
            {
                using var zip = ZipFile.OpenRead(coreZipPath);
                foreach (var entry in zip.Entries.Where(e =>
                    e.FullName.StartsWith("Lang/args/", StringComparison.OrdinalIgnoreCase) &&
                    e.FullName.EndsWith(".json", StringComparison.OrdinalIgnoreCase)))
                {
                    using var stream = entry.Open();
                    LoadArgsFromStream(stream);
                }
            }
        }
    }

    private void LoadArgsFromStream(Stream stream)
    {
        using var doc = JsonDocument.Parse(stream);
        if (!doc.RootElement.TryGetProperty("tokensArgs", out var arr) || arr.ValueKind != JsonValueKind.Array)
            return;

        foreach (var a in arr.EnumerateArray())
        {
            var sid = a.TryGetProperty("sid", out var sidEl) ? sidEl.GetString() ?? "" : "";
            if (string.IsNullOrWhiteSpace(sid)) continue;

            var args = a.TryGetProperty("args", out var argsEl) && argsEl.ValueKind == JsonValueKind.Array
                ? argsEl.EnumerateArray().Select(x => x.GetString() ?? "").ToArray()
                : Array.Empty<string>();

            _argsBySid[sid] = args;
        }
    }

    private void LoadTextsForLocale(string locale, bool overwriteExisting = true)
    {
        var textsDir = Path.Combine(_streamingAssetsRoot, "Lang", locale, "texts");
        if (Directory.Exists(textsDir))
        {
            foreach (var jsonPath in Directory.EnumerateFiles(textsDir, "*.json"))
            {
                using var fs = File.OpenRead(jsonPath);
                LoadTextsFromStream(fs, Path.GetFileNameWithoutExtension(jsonPath), overwriteExisting);
            }
            return;
        }

        // EA build: texts are inside Core.zip
        var coreZipPath = Path.Combine(_streamingAssetsRoot, "Core.zip");
        if (!File.Exists(coreZipPath)) return;

        var prefix = $"Lang/{locale}/texts/";
        using var zip = ZipFile.OpenRead(coreZipPath);
        foreach (var entry in zip.Entries.Where(e =>
            e.FullName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
            e.FullName.EndsWith(".json", StringComparison.OrdinalIgnoreCase)))
        {
            using var stream = entry.Open();
            LoadTextsFromStream(stream, Path.GetFileNameWithoutExtension(entry.Name), overwriteExisting);
        }
    }

    private void LoadTextsFromStream(Stream stream, string fileName, bool overwriteExisting)
    {
        using var doc = JsonDocument.Parse(stream);
        if (!doc.RootElement.TryGetProperty("tokens", out var arr) || arr.ValueKind != JsonValueKind.Array)
            return;

        foreach (var t in arr.EnumerateArray())
        {
            var sid = t.TryGetProperty("sid", out var sidEl) ? sidEl.GetString() ?? "" : "";
            if (string.IsNullOrWhiteSpace(sid)) continue;

            var text = t.TryGetProperty("text", out var txtEl) ? txtEl.GetString() ?? "" : "";

            var entry = new ResolvedEntry(
                sid,
                text,
                fileName,
                Category: fileName,
                HasPlaceholders: PlaceholderRx().IsMatch(text ?? "")
            );

            if (overwriteExisting || !_bySid.ContainsKey(sid))
                _bySid[sid] = entry;
        }
    }

    public string? ResolveText(string sid)
        => _bySid.TryGetValue(sid, out var e) ? e.Text : null;

    public string[] GetArgs(string sid)
    {
        if (_argsBySid.TryGetValue(sid, out var args))
            return args;

        var baseSid = TryGetBaseSid(sid);
        if (baseSid != null && _argsBySid.TryGetValue(baseSid, out var baseArgs))
            return baseArgs;

        if (TryGenerateAbilityDurationArgs(sid, out var generatedArgs))
            return generatedArgs;

        if (TryGenerateFactionLawArgs(sid, out var lawArgs))
            return lawArgs;

        return Array.Empty<string>();
    }

    private bool TryGenerateAbilityDurationArgs(string sid, out string[] args)
    {
        args = Array.Empty<string>();

        if (!Regex.IsMatch(sid, @"_ability_\d+_description$", RegexOptions.IgnoreCase))
            return false;

        var text = ResolveText(sid);
        if (string.IsNullOrEmpty(text))
            return false;

        if (!text.Contains("{0}"))
            return false;

        var hasDurationKeyword = text.IndexOf("Duration", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                 text.IndexOf("round", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                 text.IndexOf("turn", StringComparison.OrdinalIgnoreCase) >= 0;
        if (!hasDurationKeyword)
            return false;

        args = new[] { "current_unit_ability_buff_duration|alt_text_ability_buff_duration_1" };
        return true;
    }

    private bool TryGenerateFactionLawArgs(string sid, out string[] args)
    {
        args = Array.Empty<string>();

        if (!Regex.IsMatch(sid, @"^fraction_law_\w+_\d+_desc$", RegexOptions.IgnoreCase))
            return false;

        var text = ResolveText(sid);
        if (string.IsNullOrEmpty(text))
            return false;

        if (!text.Contains("{0}"))
            return false;

        var placeholderCount = Regex.Matches(text, @"\{\d+\}").Cast<Match>()
            .Select(m => int.Parse(m.Value.Trim('{', '}')))
            .DefaultIfEmpty(-1)
            .Max() + 1;

        if (placeholderCount <= 0)
            return false;

        var generatedArgs = new string[placeholderCount];
        for (int i = 0; i < placeholderCount; i++)
        {
            generatedArgs[i] = $"current_law_modInt_bonuses_{i}_parameters_1";
        }

        args = generatedArgs;
        return true;
    }

    private static string? TryGetBaseSid(string sid)
    {
        var suffixPairs = new (string variant, string base_)[]
        {
            ("_alt_2_desc", "_desc"),
            ("_alt_2_name", "_name"),
            ("_alt_desc", "_desc"),
            ("_alt_name", "_name"),
            ("_description_1", "_description"),
            ("_description", "_desc"),
            ("_desc_alt", "_desc"),
            ("_name_alt", "_name"),
        };

        foreach (var (variant, base_) in suffixPairs)
        {
            if (sid.EndsWith(variant, StringComparison.OrdinalIgnoreCase))
            {
                var baseSid = sid.Substring(0, sid.Length - variant.Length) + base_;
                return baseSid;
            }
        }

        return null;
    }

    public IEnumerable<(string Sid, ResolvedEntry Entry)> AllEntries()
        => _bySid.Select(kv => (kv.Key, kv.Value));

    public bool TryGet(string sid, out ResolvedEntry entry)
    {
        if (_bySid.TryGetValue(sid, out var e)) { entry = e; return true; }
        entry = default!; return false;
    }

    public bool TryGetArgs(string sid, out string[] args)
    {
        if (_argsBySid.TryGetValue(sid, out var a))
        {
            args = a;
            return true;
        }

        var baseSid = TryGetBaseSid(sid);
        if (baseSid != null && _argsBySid.TryGetValue(baseSid, out var baseArgs))
        {
            args = baseArgs;
            return true;
        }

        args = Array.Empty<string>();
        return false;
    }

    public string? TryFormatWithArgsResolvingSids(string? text, string sidForArgs)
    {
        if (string.IsNullOrEmpty(text)) return text;
        var rawArgs = GetArgs(sidForArgs);
        if (rawArgs.Length == 0) return text;

        var resolvedArgs = rawArgs.Select(a => ResolveText(a) ?? a).Cast<object>().ToArray();
        try { return string.Format(text, resolvedArgs); }
        catch { return text; }
    }
}
