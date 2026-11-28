namespace Felix.Bootstrap;

public static class VersionHelper
{
    /// <summary>Lit « v1.2.3 », « 1.2.3-beta.1 » ou « 2 » comme une version.</summary>
    public static bool TryParse(string? text, out Version version)
    {
        version = new Version(0, 0);
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var s = text.Trim().TrimStart('v', 'V');
        var cut = s.IndexOfAny(new[] { '-', '+' });
        if (cut >= 0)
            s = s[..cut];
        if (!s.Contains('.'))
            s += ".0";

        if (!Version.TryParse(s, out var parsed))
            return false;
        version = parsed;
        return true;
    }
}
