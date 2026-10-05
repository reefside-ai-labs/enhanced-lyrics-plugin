using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using MediaBrowser.Controller.Lyrics;
using MediaBrowser.Controller.Resolvers;
using MediaBrowser.Model.Lyrics;
using TtmlLyricParser;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.EnhancedLyrics.Lyrics;

public sealed partial class LyricParser(ILogger<LyricParser>? logger = null) : ILyricParser
{
    public string Name => "Enhanced Lyrics";
    public ResolverPriority Priority => ResolverPriority.First;
    public LyricDto? ParseLyrics(LyricFile lyrics) => Plugin.Instance?.Configuration.Enabled == false ? null : Parse(lyrics.Name, lyrics.Content)?.ToLegacy();

    public LyricDocument? Parse(string name, string content)
    {
        if (string.IsNullOrWhiteSpace(content)) return null;
        return Path.GetExtension(name).ToLowerInvariant() switch
        {
            ".ttml" => ParseTtml(content, Path.GetFileName(name)),
            ".lrc" or ".elrc" => ParseLrc(content, Path.GetExtension(name)[1..].ToLowerInvariant()),
            ".txt" => new("txt", null, content.Split('\n').Select(l => new TimedLine(l.TrimEnd('\r'), null, null, [])).ToArray()),
            _ => null
        };
    }

    private LyricDocument? ParseTtml(string content, string name)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
        var result = new TtmlParser().Parse(stream, new ParserOptions { MaxCharacters = LocalLyricReader.MaxBytes });
        if (!result.Success)
        {
            logger?.LogWarning("Invalid TTML {File}: {Diagnostics}", name,
                string.Join("; ", result.Diagnostics.Select(d => $"{d.Code} at {d.Location.Line}:{d.Location.Column}: {d.Message}")));
            return null;
        }
        var song = result.Lyrics!;
        var lines = song.Lines.Select(line =>
        {
            var words = new List<LyricWord>();
            var timed = HasWordTiming(line.Content);
            if (timed) ProjectWords(line.Content, line.Timing, false, line.SingerIds.ToArray(), words);
            return new TimedLine(line.Text, line.Timing.Begin.TotalMilliseconds, line.Timing.End?.TotalMilliseconds,
                words, line.SingerIds.ToArray(), line.Roles.ToArray(), line.Key);
        }).OrderBy(l => l.Start).ToArray();
        if (lines.Length == 0 || lines.All(l => string.IsNullOrWhiteSpace(l.Text))) return null;
        return new("ttml", song.Language, lines,
            song.Variants.Select(v => new LyricVariant(v.Kind.ToString(), v.Language, ToXml(v.Source).ToString(SaveOptions.DisableFormatting))).ToArray(),
            content, result.Diagnostics.Select(d => $"{d.Code}: {d.Message}").ToArray());
    }

    private static bool HasWordTiming(IEnumerable<LyricContent> content) => content.OfType<LyricSpan>()
        .Any(s => s.Timing.SourceBegin is not null || s.Timing.SourceEnd is not null || s.Timing.SourceDuration is not null || HasWordTiming(s.Content));

    private static void ProjectWords(IEnumerable<LyricContent> content, LyricTiming timing, bool background, string[] singers, List<LyricWord> words)
    {
        foreach (var node in content)
        {
            switch (node)
            {
                case LyricText text:
                    words.Add(new(text.Value, timing.Begin.TotalMilliseconds, timing.End?.TotalMilliseconds, background, singers));
                    break;
                case LyricBreak:
                    words.Add(new("\n", timing.Begin.TotalMilliseconds, timing.End?.TotalMilliseconds, background, singers));
                    break;
                case LyricSpan span:
                    ProjectWords(span.Content, span.Timing, background || span.IsBackground,
                        span.SingerIds.IsEmpty ? singers : span.SingerIds.ToArray(), words);
                    break;
            }
        }
    }

    private static XElement ToXml(SourceElement source) => new(source.Name,
        source.Attributes.Select(a => new XAttribute(a.Key, a.Value)),
        source.Children.Select<SourceNode, object>(n => n is SourceElement element ? ToXml(element) : new XText(((SourceText)n).Value)));

    private static LyricDocument? ParseLrc(string content, string format)
    {
        var offsetMatch = OffsetRegex().Match(content);
        var offset = offsetMatch.Success && double.TryParse(offsetMatch.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : 0;
        var lines = new List<TimedLine>();
        foreach (var raw in content.Split('\n'))
        {
            var input = raw.TrimEnd('\r').TrimStart('\uFEFF');
            if (string.IsNullOrWhiteSpace(input) || MetadataRegex().IsMatch(input)) continue;
            var stamps = LineTimeRegex().Matches(input);
            if (stamps.Count == 0) return null;
            // All line timestamps must be a prefix. Never silently discard malformed time tags.
            var prefixEnd = 0;
            foreach (Match stamp in stamps)
            {
                if (stamp.Index != prefixEnd) return null;
                prefixEnd = stamp.Index + stamp.Length;
            }
            var text = input[prefixEnd..];
            var tags = WordTimeRegex().Matches(text);
            if (text.Contains('<', StringComparison.Ordinal) && tags.Count == 0) return null;
            var baseTime = Time(stamps[0]);
            if (baseTime is null) return null;
            var words = new List<LyricWord>();
            var plain = new StringBuilder();
            if (tags.Count > 0)
            {
                var before = text[..tags[0].Index];
                if (before.Length > 0) { plain.Append(before); words.Add(new(before, baseTime.Value - offset, null)); }
                for (var i = 0; i < tags.Count; i++)
                {
                    var start = Time(tags[i]);
                    var end = i + 1 < tags.Count ? Time(tags[i + 1]) : null;
                    if (start is null || (end.HasValue && end < start)) return null;
                    var from = tags[i].Index + tags[i].Length;
                    var to = i + 1 < tags.Count ? tags[i + 1].Index : text.Length;
                    var word = text[from..to];
                    if (word.Contains('<', StringComparison.Ordinal) || word.Contains('>', StringComparison.Ordinal)) return null;
                    plain.Append(word);
                    if (word.Length > 0) words.Add(new(word, start.Value - offset, end - offset));
                }
                if (words.Count > 1 && words[0].End is null) words[0] = words[0] with { End = words[1].Start };
            }
            else plain.Append(text);
            foreach (Match stamp in stamps)
            {
                var start = Time(stamp);
                if (start is null) return null;
                var shift = start.Value - baseTime.Value;
                var shifted = words.Select(w => w with { Start = w.Start + shift, End = w.End + shift }).ToArray();
                lines.Add(new(plain.ToString(), start.Value - offset, shifted.LastOrDefault()?.End, shifted));
            }
        }
        return lines.Count == 0 ? null : new(format, null, lines.OrderBy(l => l.Start).ToArray());
    }

    private static double? Time(Match match)
    {
        if (!double.TryParse(match.Groups[1].Value, CultureInfo.InvariantCulture, out var minutes) ||
            !double.TryParse(match.Groups[2].Value, CultureInfo.InvariantCulture, out var seconds) || seconds >= 60) return null;
        var fraction = match.Groups[3].Success ? double.Parse("0." + match.Groups[3].Value, CultureInfo.InvariantCulture) : 0;
        return (minutes * 60 + seconds + fraction) * 1000;
    }

    [GeneratedRegex(@"\[(\d{1,6}):(\d{2})(?:[.:](\d{1,3}))?\]")]
    private static partial Regex LineTimeRegex();
    [GeneratedRegex(@"<(\d{1,6}):(\d{2})(?:[.:](\d{1,3}))?>")]
    private static partial Regex WordTimeRegex();
    [GeneratedRegex(@"^\[(?:ar|al|ti|au|length|by|offset|re|ve|id):[^\]]*\]$", RegexOptions.IgnoreCase)]
    private static partial Regex MetadataRegex();
    [GeneratedRegex(@"\[offset:([+-]?\d+)\]", RegexOptions.IgnoreCase)]
    private static partial Regex OffsetRegex();
}
