using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.EnhancedLyrics.WebIntegration;

internal sealed class WebIntegrationMiddleware(RequestDelegate next, Func<bool> enabled, Func<string> baseUrl, ILogger logger, int bufferLimit = 1024 * 1024)
{
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private static readonly string[] RequestHeaders = ["Accept-Encoding", "If-None-Match", "If-Modified-Since", "If-Match", "If-Unmodified-Since", "Range", "If-Range"];

    public async Task InvokeAsync(HttpContext context)
    {
        if (!enabled() || !(HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method)) || !IsIndex(context.Request, baseUrl()))
        {
            await next(context).ConfigureAwait(false);
            return;
        }
        var originalFeature = context.Features.Get<IHttpResponseBodyFeature>()!;
        var originalMethod = context.Request.Method;
        var head = HttpMethods.IsHead(originalMethod);
        var headers = RequestHeaders.ToDictionary(name => name, name => (Present: context.Request.Headers.ContainsKey(name), Value: context.Request.Headers[name]));
        await using var capture = new BoundedResponseStream(originalFeature.Stream, head, bufferLimit);
        var bufferedFeature = new StreamResponseBodyFeature(capture);
        try
        {
            foreach (var name in RequestHeaders) context.Request.Headers.Remove(name);
            // Derive HEAD headers from the same representation as GET, then discard its body.
            if (head) context.Request.Method = HttpMethods.Get;
            context.Features.Set<IHttpResponseBodyFeature>(bufferedFeature);
            await next(context).ConfigureAwait(false);
            await bufferedFeature.CompleteAsync().ConfigureAwait(false);
            context.Features.Set(originalFeature);
            if (capture.Overflowed)
            {
                logger.LogWarning("Web index exceeded the {Limit} byte integration buffer; serving unchanged HTML.", bufferLimit);
                return;
            }
            var bytes = capture.ToArray();
            if (!context.Response.HasStarted && context.Response.StatusCode == StatusCodes.Status200OK &&
                context.Response.ContentType?.StartsWith("text/html", StringComparison.OrdinalIgnoreCase) == true &&
                !context.Response.Headers.ContainsKey("Content-Encoding"))
            {
                try
                {
                    if (IndexHtmlTransformer.TryTransform(Utf8.GetString(bytes), out var html))
                    {
                        bytes = Utf8.GetBytes(html);
                        foreach (var name in new[] { "ETag", "Last-Modified", "Content-Range", "Accept-Ranges" }) context.Response.Headers.Remove(name);
                        context.Response.Headers.CacheControl = "no-store";
                        context.Response.ContentLength = bytes.Length;
                    }
                    else logger.LogWarning("Web index has no script anchor; Enhanced Lyrics could not add its loader.");
                }
                catch (DecoderFallbackException)
                {
                    logger.LogWarning("Web index is not valid UTF-8; serving unchanged HTML.");
                }
            }
            if (!head) await originalFeature.Stream.WriteAsync(bytes, context.RequestAborted).ConfigureAwait(false);
        }
        finally
        {
            context.Features.Set(originalFeature);
            context.Request.Method = originalMethod;
            foreach (var (name, saved) in headers)
            {
                if (saved.Present) context.Request.Headers[name] = saved.Value;
                else context.Request.Headers.Remove(name);
            }
        }
    }

    internal static bool IsIndex(HttpRequest request, string configuredBaseUrl)
    {
        var prefix = configuredBaseUrl.TrimEnd('/');
        if (request.PathBase.HasValue && prefix.StartsWith(request.PathBase.Value!, StringComparison.OrdinalIgnoreCase))
            prefix = prefix[request.PathBase.Value!.Length..];
        return request.Path.Equals(new PathString(prefix + "/web/"), StringComparison.OrdinalIgnoreCase) || request.Path.Equals(new PathString(prefix + "/web/index.html"), StringComparison.OrdinalIgnoreCase);
    }
}
