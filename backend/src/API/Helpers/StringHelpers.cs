namespace API.Helpers;

public static class StringHelpers
{
    public static string CapitalizeFirst(string str)
    {
        if (string.IsNullOrEmpty(str)) return str;
        return char.ToUpper(str[0]) + str.Substring(1);
    }

    // Localization labels reused as headings sometimes carry a trailing section colon
    // (English ":", French " :", Japanese/Chinese "："); strip it for that use.
    public static string? TrimTrailingColon(string? text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        var trimmed = text.TrimEnd();
        if (trimmed.EndsWith(':') || trimmed.EndsWith('：'))
        {
            return trimmed[..^1].TrimEnd();
        }
        return text;
    }
}
