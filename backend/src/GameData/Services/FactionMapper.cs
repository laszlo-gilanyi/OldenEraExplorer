using Localization.Indexing;
using Localization.Services;

namespace GameData.Services;

/// <summary>
/// Centralized service for mapping faction identifiers to localized display names.
/// </summary>
public class FactionMapper
{
    private LangIndex? _langIndex;

    public FactionMapper() { }

    public void SetLangIndex(LangIndex? langIndex)
    {
        _langIndex = langIndex;
    }

    /// <summary>
    /// Central faction mapping data. Returns (SID, iconName) tuple.
    /// Handles plural forms and icon name mappings in ONE place.
    /// </summary>
    private static (string? sid, string? iconName) GetFactionData(string? factionKey)
    {
        if (string.IsNullOrWhiteSpace(factionKey))
            return (null, null);

        var key = factionKey.Trim().ToLowerInvariant();

        // Single source of truth for faction mappings
        return key switch
        {
            "human" or "humans" => ("human_name", "human"),
            "undead" => ("undead_name", "undead"),
            "nature" => ("nature_name", "spring"),      // Nature uses "spring" for icons
            "demon" or "demons" => ("demon_name", "hive"), // Demon uses "hive" for icons
            "unfrozen" => ("unfrozen_name", "unfrozen"),
            "dungeon" => ("dungeon_name", "dungeon"),
            "neutral" => ("neutral_name", null),        // Neutral has no icon
            _ => (null, key)
        };
    }

    /// <summary>
    /// Maps a faction key to its localization SID.
    /// Handles plural forms and special cases (e.g., "demons" -> "demon_name").
    /// </summary>
    public string? GetFactionSid(string? factionKey)
    {
        var (sid, _) = GetFactionData(factionKey);
        return sid;
    }

    /// <summary>
    /// Gets the faction icon path.
    /// Returns null for neutral faction (no icon) or invalid faction keys.
    /// Handles plural forms and icon name mappings (e.g., "demon"/"demons" -> "hive_icon").
    /// </summary>
    public string? GetFactionIconPath(string? factionKey)
    {
        var (_, iconName) = GetFactionData(factionKey);

        if (string.IsNullOrWhiteSpace(iconName))
            return null;

        return $"icons/fractions/{iconName}_icon";
    }

    public string? MapFactionDisplay(string? factionKey)
    {
        if (_langIndex is null || string.IsNullOrWhiteSpace(factionKey))
            return null;

        var key = factionKey.Trim().ToLowerInvariant();
        string? sid = GetFactionSid(factionKey);

        if (!string.IsNullOrWhiteSpace(sid))
        {
            // neutral_name exists only in overlays, not the game lang files
            var overlay = OverlayService.Instance.TryResolveFromOverlay(sid, _langIndex.Locale);
            if (!string.IsNullOrWhiteSpace(overlay))
                return overlay;

            var result = _langIndex.ResolveText(sid);
            if (!string.IsNullOrWhiteSpace(result) && result != sid)
                return result;
        }

        var genericSid = $"{key}_name";
        var generic = _langIndex.ResolveText(genericSid);
        if (!string.IsNullOrWhiteSpace(generic) && generic != genericSid)
            return generic;

        var raw = _langIndex.ResolveText(key);
        if (!string.IsNullOrWhiteSpace(raw) && raw != key)
            return raw;

        var ti = System.Globalization.CultureInfo.CurrentCulture.TextInfo;
        return ti.ToTitleCase(factionKey.Replace('_', ' ').Replace('-', ' '));
    }
}
