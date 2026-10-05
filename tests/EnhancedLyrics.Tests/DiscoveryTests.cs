using Jellyfin.Plugin.EnhancedLyrics.Lyrics;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EnhancedLyrics.Tests;
public sealed class DiscoveryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
    public DiscoveryTests() => Directory.CreateDirectory(_root);
    private void Write(string name, string content = "[00:01]Valid") => File.WriteAllText(Path.Combine(_root, name), content);
    private LocalLyricReader Reader => new(new LyricParser(), NullLogger<LocalLyricReader>.Instance);
    [Fact]
    public void LanguageWinsThenFormatAndUnrelatedFilesAreExcluded()
    {
        Write("Song.ttml"); Write("Song.en.lrc"); Write("Song.en.elrc"); Write("Song.fr.ttml");
        Write("Song remix.en.ttml"); Write("Song.notes.txt"); Write("Song.mp3");
        var candidates = LocalLyricReader.Discover(Path.Combine(_root, "Song.flac"), "en");
        Assert.Equal(new[] { "Song.en.elrc", "Song.en.lrc", "Song.ttml", "Song.fr.ttml" }, candidates.Select(s => Path.GetFileName(s.Path)));
    }
    [Fact]
    public async Task MalformedPreferredFileFallsBackWithoutChangingSources()
    {
        Write("Song.ttml", "<broken>"); Write("Song.elrc");
        var doc = await Reader.ReadAsync(Path.Combine(_root, "Song.flac"), null, default);
        Assert.Equal("elrc", doc!.Format);
        Assert.Equal("<broken>", await File.ReadAllTextAsync(Path.Combine(_root, "Song.ttml")));
    }
    [Fact]
    public void ThreeLetterLanguagePreferenceMatchesTwoLetterSidecar()
    {
        Write("Song.en.lrc"); Write("Song.ttml");
        Assert.EndsWith("Song.en.lrc", LocalLyricReader.Discover(Path.Combine(_root, "Song.flac"), "eng")[0].Path, StringComparison.Ordinal);
    }
    [Fact]
    public async Task OversizedAndLinkedFilesAreSkipped()
    {
        Write("Song.ttml", new string('x', LocalLyricReader.MaxBytes + 1));
        Write("other.lrc");
        File.CreateSymbolicLink(Path.Combine(_root, "Song.elrc"), Path.Combine(_root, "other.lrc"));
        Write("Song.txt", "Fallback");
        Assert.Equal("txt", (await Reader.ReadAsync(Path.Combine(_root, "Song.flac"), null, default))!.Format);
    }
    [Fact]
    public async Task CancellationIsNotSwallowed()
    {
        Write("Song.lrc");
        await Assert.ThrowsAsync<OperationCanceledException>(() => Reader.ReadAsync(Path.Combine(_root, "Song.flac"), null, new CancellationToken(true)));
    }
    public void Dispose() => Directory.Delete(_root, true);
}
