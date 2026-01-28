using Localization.Indexing;
using Localization.Resolution;

namespace API.Helpers;

public static class LocalizationHelper
{
    public static string? TryResolveText(ITextResolver resolver, string? sid, string? locale)
    {
        if (string.IsNullOrWhiteSpace(sid) || string.IsNullOrWhiteSpace(locale))
            return null;

        try
        {
            var ctx = new ResolutionContext(locale);
            var result = resolver.Resolve(sid, ctx, out _);
            if (!string.IsNullOrWhiteSpace(result) && result != sid)
                return result;
        }
        catch { }

        return null;
    }

    public static string? TryResolveText(ITextResolver resolver, string? sid, ResolutionContext ctx)
    {
        if (string.IsNullOrWhiteSpace(sid))
            return null;

        try
        {
            var result = resolver.Resolve(sid, ctx, out _);
            if (!string.IsNullOrWhiteSpace(result) && result != sid)
                return result;
        }
        catch { }

        return null;
    }

    public static string? GetLocalizedUnitName(ITextResolver resolver, string unitId, string locale)
    {
        var patterns = new[]
        {
            $"unit.{unitId}.name",
            $"unit_{unitId}_name",
            $"{unitId}_name",
            $"units.{unitId}.name"
        };

        foreach (var pattern in patterns)
        {
            var result = TryResolveText(resolver, pattern, locale);
            if (!string.IsNullOrWhiteSpace(result) && !result.StartsWith("{") && result != pattern)
                return result;
        }

        return $"{unitId}_name";
    }

    public static string? GetLocalizedText(
        ITextResolver resolver,
        LangIndex lang,
        string? primarySid,
        string objectId,
        string textType,
        string locale)
    {
        if (!string.IsNullOrWhiteSpace(primarySid))
        {
            var result = TryResolveText(resolver, primarySid, locale);
            if (!string.IsNullOrWhiteSpace(result)) return result;
            var langResult = lang.ResolveText(primarySid);
            if (!string.IsNullOrWhiteSpace(langResult)) return langResult;
        }

        var patterns = new[] { $"{objectId}_{textType}", $"mapobject.{objectId}.{textType}", $"object.{objectId}.{textType}" };
        foreach (var pattern in patterns)
        {
            var result = TryResolveText(resolver, pattern, locale);
            if (!string.IsNullOrWhiteSpace(result)) return result;
            var langResult = lang.ResolveText(pattern);
            if (!string.IsNullOrWhiteSpace(langResult)) return langResult;
        }

        return null;
    }
}
