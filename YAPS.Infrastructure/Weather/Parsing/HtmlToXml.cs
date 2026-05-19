using System;
using System.Text.RegularExpressions;
using System.Xml;

namespace Yaps.Infrastructure.Weather.Parsing;

/// <summary>
/// Provider sites emit non-XHTML markup; <see cref="XmlDocument"/>
/// rejects open <c>&lt;img&gt;</c> and HTML entities. The legacy
/// extractors did the same close/replace dance inline; consolidate it
/// here so the scraper providers can keep using XPath against an
/// XmlDocument view of the snippet.
/// </summary>
public static class HtmlToXml
{
    private static readonly Regex _imgRegex = new(
        @"<img\s[^>]*?src\s*=\s*['""]([^ '""]*?)['""][^>]*?>",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static string Normalize(string html)
    {
        if (string.IsNullOrEmpty(html))
            return html;

        var matches = _imgRegex.Matches(html);
        for (int i = matches.Count - 1; i >= 0; i--)
        {
            var match = matches[i];
            var replacement = match.Value.Substring(0, match.Value.Length - 1) + "/>";
            html = html.Remove(match.Index, match.Length).Insert(match.Index, replacement);
        }

        return html.Replace("<br>", " ")
                   .Replace("</br>", " ")
                   .Replace("&nbsp;", " ");
    }

    public static XmlDocument Parse(string html)
    {
        var doc = new XmlDocument();
        doc.LoadXml(Normalize(html));
        return doc;
    }
}
