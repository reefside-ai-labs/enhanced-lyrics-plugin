using MediaBrowser.Model.Lyrics;

namespace Jellyfin.Plugin.EnhancedLyrics.Lyrics;

// API times are milliseconds; Jellyfin's legacy model uses ticks.
public sealed record LyricWord(string Text, double Start, double? End, bool Background = false, string[]? SingerIds = null);
public sealed record TimedLine(string Text, double? Start, double? End, IReadOnlyList<LyricWord> Words, string[]? SingerIds = null, string[]? Roles = null, string? Key = null);
public sealed record LyricVariant(string Kind, string? Language, string Xml);
public sealed record LyricDocument(string Format, string? Language, IReadOnlyList<TimedLine> Lines,
    IReadOnlyList<LyricVariant>? Variants = null, string? SourceXml = null, IReadOnlyList<string>? Diagnostics = null)
{
    public LyricDto ToLegacy() => new()
    {
        Metadata = new LyricMetadata { IsSynced = Lines.Any(l => l.Start.HasValue) },
        Lyrics = Lines.Select(line =>
        {
            var position = 0;
            var cues = line.Words.Select(word =>
            {
                // Words retain whitespace and concatenate to the exact line text.
                var start = position;
                position += word.Text.Length;
                return new LyricLineCue(start, position, ToTicks(word.Start), word.End is { } end ? ToTicks(end) : null);
            }).ToArray();
            return new MediaBrowser.Model.Lyrics.LyricLine(line.Text, line.Start is { } time ? ToTicks(time) : null, cues.Length > 0 ? cues : null);
        }).ToArray()
    };

    private static long ToTicks(double milliseconds) => checked((long)Math.Round(milliseconds * TimeSpan.TicksPerMillisecond));
}
