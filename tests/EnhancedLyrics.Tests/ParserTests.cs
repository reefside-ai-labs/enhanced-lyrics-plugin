using Jellyfin.Plugin.EnhancedLyrics.Lyrics;
using Xunit;

namespace EnhancedLyrics.Tests;
public class ParserTests
{
    private readonly LyricParser _parser = new();
    [Fact]
    public void TtmlResolvesParentRelativeWordsAndProjectsLegacyTicks()
    {
        var doc = _parser.Parse("track.ttml", File.ReadAllText("fixtures/Synthetic.ttml"))!;
        Assert.Equal("Hello Jellyfin", doc.Lines[0].Text);
        Assert.Equal(2000, doc.Lines[0].Words[0].Start);
        Assert.Equal(3000, doc.Lines[0].Words[0].End);
        Assert.Equal(3000, doc.Lines[0].Words[1].Start);
        var legacy = doc.ToLegacy();
        Assert.Equal(20000000, legacy.Lyrics[0].Start);
        Assert.Equal(6, legacy.Lyrics[0].Cues![0].EndPosition);
        Assert.Equal(14, legacy.Lyrics[0].Cues![1].EndPosition);
        Assert.Equal(30000000, legacy.Lyrics[0].Cues![1].Start);
    }
    [Fact]
    public void AppleAbsoluteTimingBackgroundAndVariantSourcesAreRetained()
    {
        const string xml = """
            <tt xmlns="http://www.w3.org/ns/ttml" xmlns:ttm="http://www.w3.org/ns/ttml#metadata"
                xmlns:am="http://music.apple.com/lyric-ttml-internal" am:timing="Word" xml:lang="en">
              <head><metadata><ttm:agent xml:id="singer" type="person"><ttm:name type="full">Test</ttm:name></ttm:agent>
                <am:translations><am:translation xml:lang="fr"><am:text>Bonjour</am:text></am:translation></am:translations>
              </metadata></head>
              <body><div><p begin="00:02.000" end="00:06.000" ttm:agent="singer">
                <span begin="00:02.000" end="00:03.000">Hello</span><span begin="00:03.000" end="00:05.000" ttm:role="x-bg"> echo</span>
              </p></div></body>
            </tt>
            """;
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(xml));
        var parsed = new TtmlLyricParser.TtmlParser().Parse(stream);
        Assert.True(parsed.Success, string.Join("; ", parsed.Diagnostics.Select(d => d.Message)));
        var doc = _parser.Parse("track.ttml", xml)!;
        Assert.NotNull(doc);
        Assert.Equal(2000, doc.Lines[0].Words.First(w => w.Text == "Hello").Start);
        Assert.True(doc.Lines[0].Words.First(w => w.Text == " echo").Background);
        Assert.Equal("singer", doc.Lines[0].SingerIds![0]);
        Assert.Equal("fr", doc.Variants![0].Language);
        Assert.Contains("Bonjour", doc.Variants[0].Xml, StringComparison.Ordinal);
        Assert.Equal(xml, doc.SourceXml);
    }
    [Fact]
    public void ElrcRetainsWhitespaceAndExplicitFinalWordEnd()
    {
        var doc = _parser.Parse("track.ELRC", File.ReadAllText("fixtures/Synthetic.elrc"))!;
        Assert.Equal("Hello Jellyfin", doc.Lines[0].Text);
        Assert.Equal(6000, doc.Lines[0].Words[1].End);
        Assert.Equal(6000, doc.Lines[0].End);
        Assert.Equal("Hello ", doc.Lines[0].Words[0].Text);
    }
    [Fact]
    public void RepeatedLineTagsShiftWordTimingAndApplyOffsetOnce()
    {
        var doc = _parser.Parse("track.lrc", "[offset:200]\n[00:01.00][00:05.00]<00:01.00>A <00:02.00>B<00:03.00>")!;
        Assert.Equal(800, doc.Lines[0].Start);
        Assert.Equal(4800, doc.Lines[1].Words[0].Start);
        Assert.Equal(6800, doc.Lines[1].Words[1].End);
    }
    [Fact]
    public void OrdinaryLrcDoesNotInventWordTiming()
    {
        var doc = _parser.Parse("track.lrc", "[00:10.25]Whole line")!;
        Assert.Equal(10250, doc.Lines[0].Start);
        Assert.Empty(doc.Lines[0].Words);
        Assert.Null(doc.ToLegacy().Lyrics[0].Cues);
    }
    [Theory]
    [InlineData("[00:99.00]Bad")]
    [InlineData("[00:01.00]<bad>Word")]
    [InlineData("[00:01.00]<00:03.00>A<00:02.00>B")]
    [InlineData("[00:01.00]Good\n[invalid]Bad")]
    public void InvalidLrcIsRejectedForFallback(string text) => Assert.Null(_parser.Parse("track.elrc", text));
    [Theory]
    [InlineData("<tt>bad</tt>")]
    [InlineData("<!DOCTYPE tt [<!ENTITY x SYSTEM 'file:///etc/passwd'>]><tt xmlns='http://www.w3.org/ns/ttml'>&x;</tt>")]
    public void InvalidOrUnsafeXmlIsRejected(string text) => Assert.Null(_parser.Parse("track.ttml", text));
    [Fact]
    public void PlainTextHasNoTiming()
    {
        var doc = _parser.Parse("track.txt", "First\r\nSecond")!;
        Assert.All(doc.Lines, line => Assert.Null(line.Start));
        Assert.Equal("First", doc.Lines[0].Text);
    }
    [Fact]
    public void UnknownFormatAndEmptyContentAreRejected()
    {
        Assert.Null(_parser.Parse("track.json", "Hello"));
        Assert.Null(_parser.Parse("track.lrc", ""));
    }
}
