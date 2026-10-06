using System.Net;
using System.Text;
using Jellyfin.Plugin.EnhancedLyrics.WebIntegration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Xunit;

namespace EnhancedLyrics.Tests;

public sealed class WebIntegrationTests
{
    private const string Stock = "<html><head><!-- <script>fake</script> --><script nonce=\"abc&amp;123\" defer src=\"runtime.js\"></script></head><body>original</body></html>";

    [Theory]
    [InlineData("", "/web/")]
    [InlineData("", "/web/index.html?cold=1")]
    [InlineData("/jf", "/jf/web/")]
    [InlineData("/jf", "/jf/web/index.html")]
    public async Task CapturesStaticSendFileAndInsertsBeforeBoot(string prefix, string path)
    {
        using var fixture = new Fixture(prefix);
        var response = await fixture.Client.GetAsync(path);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, html.Split(IndexHtmlTransformer.LoaderPath).Length - 1);
        Assert.True(html.IndexOf(IndexHtmlTransformer.LoaderPath, StringComparison.Ordinal) < html.IndexOf("runtime.js", StringComparison.Ordinal));
        Assert.Contains("nonce=\"abc&amp;123\"", html, StringComparison.Ordinal);
        Assert.Equal(Stock, await File.ReadAllTextAsync(fixture.Index));
    }

    [Fact]
    public async Task HeadMatchesGetAndConditionalRangesDoNotLoseLoader()
    {
        using var fixture = new Fixture();
        var request = new HttpRequestMessage(HttpMethod.Get, "/web/index.html");
        foreach (var (name, value) in new[] { ("Accept-Encoding", "gzip, br"), ("Range", "bytes=0-20"), ("If-None-Match", "\"old\""),
            ("If-Modified-Since", "Fri, 01 Jan 2100 00:00:00 GMT"), ("If-Match", "\"different\""), ("If-Unmodified-Since", "Sat, 01 Jan 2000 00:00:00 GMT") })
            request.Headers.TryAddWithoutValidation(name, value);
        var get = await fixture.Client.SendAsync(request);
        var bytes = await get.Content.ReadAsByteArrayAsync();
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        Assert.Contains(IndexHtmlTransformer.LoaderPath, Encoding.UTF8.GetString(bytes), StringComparison.Ordinal);
        Assert.Null(get.Headers.ETag);
        Assert.Null(get.Content.Headers.LastModified);
        Assert.Empty(get.Content.Headers.ContentEncoding);
        Assert.Empty(get.Headers.AcceptRanges);
        Assert.Null(get.Content.Headers.ContentRange);
        Assert.True(get.Headers.CacheControl!.NoStore);
        var head = await fixture.Client.SendAsync(new HttpRequestMessage(HttpMethod.Head, "/web/index.html"));
        Assert.Empty(await head.Content.ReadAsByteArrayAsync());
        Assert.Equal(bytes.Length, head.Content.Headers.ContentLength);
        Assert.Null(head.Headers.ETag);
    }

    [Fact]
    public async Task DisabledAndNonIndexResponsesPassThrough()
    {
        using var fixture = new Fixture();
        fixture.Enabled = false;
        Assert.Equal(Stock, await fixture.Client.GetStringAsync("/web/index.html"));
        fixture.Enabled = true;
        Assert.Equal("unchanged", await fixture.Client.GetStringAsync("/api"));
        Assert.Equal("unchanged", await fixture.Client.GetStringAsync("/other/web/index.html"));
        Assert.Equal("unchanged", await fixture.Client.GetStringAsync("/web/app.js"));
        var post = await fixture.Client.PostAsync("/web/index.html", new StringContent(""));
        Assert.Equal("unchanged", await post.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task OversizedIndexPassesThroughWithBoundedBuffer()
    {
        var large = Stock + new string('x', 10000);
        using var fixture = new Fixture(html: large, limit: 128);
        Assert.Equal(large, await fixture.Client.GetStringAsync("/web/index.html"));
        var head = await fixture.Client.SendAsync(new HttpRequestMessage(HttpMethod.Head, "/web/index.html"));
        Assert.Empty(await head.Content.ReadAsByteArrayAsync());
        Assert.Equal(Encoding.UTF8.GetByteCount(large), head.Content.Headers.ContentLength);
    }

    [Fact]
    public void ExistingManualLoadersAreRelocatedOnceAndMissingAnchorDoesNotChangeHtml()
    {
        var html = Stock + "<script async src=\"../EnhancedLyrics/Assets/enhanced-lyrics.js\"></script><script src='../EnhancedLyrics/Assets/enhanced-lyrics.js?old=1'></script>";
        Assert.True(IndexHtmlTransformer.TryTransform(html, out var transformed));
        Assert.Equal(1, transformed.Split(IndexHtmlTransformer.LoaderPath).Length - 1);
        Assert.True(IndexHtmlTransformer.TryTransform(transformed, out var again));
        Assert.Equal(transformed, again);
        Assert.False(IndexHtmlTransformer.TryTransform("<html>no scripts</html>", out var unchanged));
        Assert.Equal("<html>no scripts</html>", unchanged);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task BodyWriterAndAnotherTransformerComposeInEitherOrder(bool inside)
    {
        using var fixture = new Fixture(otherTransformerInside: inside);
        var html = await fixture.Client.GetStringAsync("/web/index.html");
        Assert.Contains("other-plugin", html, StringComparison.Ordinal);
        Assert.Contains(IndexHtmlTransformer.LoaderPath, html, StringComparison.Ordinal);
        Assert.Equal(Encoding.UTF8.GetByteCount(html), (await fixture.Client.GetAsync("/web/index.html")).Content.Headers.ContentLength);
    }

    [Fact]
    public async Task RestoresMethodHeadersAndResponseFeatureForOuterMiddleware()
    {
        using var fixture = new Fixture();
        var request = new HttpRequestMessage(HttpMethod.Head, "/web/index.html");
        request.Headers.TryAddWithoutValidation("Accept-Encoding", "gzip");
        await fixture.Client.SendAsync(request);
        Assert.Equal("HEAD", fixture.ObservedMethod);
        Assert.Equal("gzip", fixture.ObservedEncoding);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "el-web-tests-" + Guid.NewGuid().ToString("N"));
        private readonly TestServer _server;
        private readonly IHost _host;
        public string Index { get; }
        public HttpClient Client { get; }
        public bool Enabled { get; set; } = true;
        public string? ObservedMethod { get; private set; }
        public string? ObservedEncoding { get; private set; }

        public Fixture(string prefix = "", string html = Stock, int limit = 1024 * 1024, bool? otherTransformerInside = null)
        {
            Directory.CreateDirectory(_directory);
            Index = Path.Combine(_directory, "index.html");
            File.WriteAllText(Index, html);
            _host = Host.CreateDefaultBuilder().ConfigureLogging(logging => logging.ClearProviders()).ConfigureWebHostDefaults(web => web.UseTestServer().ConfigureServices(services => services.AddResponseCompression()).Configure(app =>
            {
                app.Use(async (context, next) =>
                {
                    await next(context);
                    ObservedMethod = context.Request.Method;
                    ObservedEncoding = context.Request.Headers.AcceptEncoding.ToString();
                });
                void Other(IApplicationBuilder builder) => builder.Use(async (context, next) =>
                {
                    var original = context.Response.Body;
                    await using var buffer = new MemoryStream();
                    context.Response.Body = buffer;
                    await next(context);
                    context.Response.Body = original;
                    var text = Encoding.UTF8.GetString(buffer.ToArray()).Replace("original", "other-plugin", StringComparison.Ordinal);
                    var bytes = Encoding.UTF8.GetBytes(text);
                    context.Response.ContentLength = bytes.Length;
                    await context.Response.BodyWriter.WriteAsync(bytes);
                });
                if (otherTransformerInside == false) Other(app);
                app.Use(next => new WebIntegrationMiddleware(next, () => Enabled, () => prefix, NullLogger.Instance, limit).InvokeAsync);
                if (otherTransformerInside == true) Other(app);
                app.UseResponseCompression();
                void Web(IApplicationBuilder branch)
                {
                    branch.UseDefaultFiles(new DefaultFilesOptions { FileProvider = new PhysicalFileProvider(_directory), RequestPath = "/web" });
                    branch.UseStaticFiles(new StaticFileOptions { FileProvider = new PhysicalFileProvider(_directory), RequestPath = "/web" });
                    branch.Run(context => context.Response.WriteAsync("unchanged"));
                }
                if (prefix.Length > 0) app.Map(prefix, Web);
                else Web(app);
            })).Build();
            _host.Start();
            _server = _host.GetTestServer();
            Client = _server.CreateClient();
        }

        public void Dispose()
        {
            Client.Dispose();
            _host.Dispose();
            Directory.Delete(_directory, true);
        }
    }
}
