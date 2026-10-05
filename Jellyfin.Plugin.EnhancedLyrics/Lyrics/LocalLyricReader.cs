using System.Globalization;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.EnhancedLyrics.Lyrics;

public sealed class LocalLyricReader(LyricParser parser, ILogger<LocalLyricReader> logger)
{
    public const int MaxBytes = 4 * 1024 * 1024;
    private static readonly string[] Formats = [".ttml", ".elrc", ".lrc", ".txt"];

    public async Task<LyricDocument?> ReadAsync(string audioPath, string? language, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(audioPath);
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory)) return null;
        try
        {
            foreach (var candidate in Discover(audioPath, language))
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    // Do not follow sidecar symlinks outside the track directory.
                    var info = new FileInfo(candidate.Path);
                    if (info.LinkTarget is not null || info.Length > MaxBytes) { logger.LogWarning("Skipped oversized or linked lyric {File}", info.Name); continue; }
                    await using var stream = new FileStream(candidate.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 4096, true);
                    using var reader = new StreamReader(stream);
                    var buffer = new char[MaxBytes + 1];
                    var count = await reader.ReadBlockAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
                    if (count > MaxBytes) continue;
                    var result = parser.Parse(candidate.Path, new string(buffer, 0, count));
                    if (result is not null) return result with { Language = result.Language ?? candidate.Language };
                    logger.LogWarning("Unable to parse lyric {File}; trying next sidecar", info.Name);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or FormatException or OverflowException)
                { logger.LogWarning(exception, "Unable to read lyric sidecar; trying next candidate"); }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        { logger.LogWarning(exception, "Unable to discover lyric sidecars"); }
        return null;
    }

    public static IReadOnlyList<Sidecar> Discover(string audioPath, string? language)
    {
        var directory = Path.GetDirectoryName(audioPath)!;
        var stem = Path.GetFileNameWithoutExtension(audioPath);
        var preferred = Normalize(language);
        return Directory.EnumerateFiles(directory).Select(path =>
        {
            var extension = Path.GetExtension(path).ToLowerInvariant();
            var rank = Array.IndexOf(Formats, extension);
            var name = Path.GetFileNameWithoutExtension(path);
            string? lang = null;
            if (!name.Equals(stem, StringComparison.OrdinalIgnoreCase))
            {
                if (!name.StartsWith(stem + ".", StringComparison.OrdinalIgnoreCase)) return null;
                lang = name[(stem.Length + 1)..];
                if (!IsLanguage(lang)) return null;
            }
            return rank < 0 ? null : new Sidecar(path, lang, rank);
        }).OfType<Sidecar>()
          .OrderBy(s => LanguageRank(Normalize(s.Language), preferred))
          .ThenBy(s => s.FormatRank).ThenBy(s => s.Path, StringComparer.Ordinal).ToArray();
    }

    private static bool IsLanguage(string language) => CultureInfo.GetCultures(CultureTypes.AllCultures)
        .Any(c => c.Name.Equals(language, StringComparison.OrdinalIgnoreCase) || c.ThreeLetterISOLanguageName.Equals(language, StringComparison.OrdinalIgnoreCase));
    private static string? Normalize(string? language)
    {
        if (string.IsNullOrWhiteSpace(language)) return null;
        var culture = CultureInfo.GetCultures(CultureTypes.AllCultures).FirstOrDefault(c =>
            c.Name.Equals(language, StringComparison.OrdinalIgnoreCase) || c.ThreeLetterISOLanguageName.Equals(language, StringComparison.OrdinalIgnoreCase));
        return culture?.Name.ToLowerInvariant() ?? language.ToLowerInvariant();
    }
    private static int LanguageRank(string? language, string? preferred) => language == preferred ? 0 :
        language is not null && preferred is not null && language.Split('-')[0] == preferred.Split('-')[0] ? 1 : language is null ? 2 : 3;
}
public sealed record Sidecar(string Path, string? Language, int FormatRank);
