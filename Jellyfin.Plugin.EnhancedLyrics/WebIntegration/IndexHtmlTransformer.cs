using System.Net;
using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.EnhancedLyrics.WebIntegration;

internal static partial class IndexHtmlTransformer
{
    internal const string LoaderPath = "../EnhancedLyrics/Assets/enhanced-lyrics.js";

    internal static bool TryTransform(string html, out string transformed)
    {
        // Relocate legacy/manual loaders too, so upgrades cannot execute two bridges.
        var withoutLoaders = LoaderScript().Replace(html, string.Empty);
        var script = ScriptsAndComments().Matches(withoutLoaders).FirstOrDefault(match => !match.Value.StartsWith("<!--", StringComparison.Ordinal));
        if (script is null) { transformed = html; return false; }
        var nonce = NonceAttribute().Match(script.Value);
        var nonceAttribute = nonce.Success ? " nonce=\"" + WebUtility.HtmlEncode(WebUtility.HtmlDecode(nonce.Groups[2].Value)) + "\"" : string.Empty;
        transformed = withoutLoaders.Insert(script.Index, "<script src=\"" + LoaderPath + "\"" + nonceAttribute + "></script>");
        return true;
    }

    [GeneratedRegex("<script\\b[^>]*\\bsrc\\s*=\\s*([\"'])[^\"']*EnhancedLyrics/Assets/enhanced-lyrics\\.js(?:\\?[^\"']*)?\\1[^>]*>\\s*</script\\s*>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LoaderScript();

    [GeneratedRegex("<!--[\\s\\S]*?-->|<script\\b[^>]*>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ScriptsAndComments();

    [GeneratedRegex("\\bnonce\\s*=\\s*([\"'])(.*?)\\1", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NonceAttribute();
}
