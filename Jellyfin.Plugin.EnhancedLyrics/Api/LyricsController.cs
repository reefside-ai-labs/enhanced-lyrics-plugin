using System.Text.Json;
using Jellyfin.Plugin.EnhancedLyrics.Lyrics;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.EnhancedLyrics.Api;

[ApiController]
[Route("EnhancedLyrics")]
public sealed class LyricsController(ILibraryManager library, IUserManager users, LocalLyricReader reader) : ControllerBase
{
    [Authorize]
    [HttpGet("Audio/{itemId:guid}")]
    public async Task<IActionResult> GetLyrics(Guid itemId, [FromQuery] string? language, CancellationToken cancellationToken)
    {
        if (Plugin.Instance?.Configuration.Enabled == false) return NotFound();
        var userId = User.FindFirst("Jellyfin-UserId")?.Value;
        if (!Guid.TryParse(userId, out var id)) return Forbid();
        var audio = library.GetItemById<Audio>(itemId, id);
        if (audio is null) return NotFound();
        language ??= users.GetUserById(id)?.AudioLanguagePreference;
        language ??= Request.GetTypedHeaders().AcceptLanguage?.OrderByDescending(h => h.Quality ?? 1).FirstOrDefault()?.Value.Value;
        var result = await reader.ReadAsync(audio.Path, language, cancellationToken).ConfigureAwait(false);
        return result is null ? NotFound() : new JsonResult(result, new JsonSerializerOptions(JsonSerializerDefaults.Web));
    }

    // Public static assets contain no credentials or library data.
    [AllowAnonymous]
    [HttpGet("Assets/{name}")]
    public IActionResult Asset(string name)
    {
        if (Plugin.Instance?.Configuration.Enabled == false) return NotFound();
        if (name is not ("enhanced-lyrics.js" or "enhanced-lyrics.css")) return NotFound();
        var stream = typeof(Plugin).Assembly.GetManifestResourceStream($"Jellyfin.Plugin.EnhancedLyrics.Web.dist.{name}");
        if (stream is null) return NotFound();
        Response.Headers.CacheControl = "no-cache";
        return File(stream, name.EndsWith(".js", StringComparison.Ordinal) ? "text/javascript" : "text/css");
    }

    [AllowAnonymous]
    [HttpGet("Status")]
    public IActionResult Status() => new JsonResult(new
    {
        enabled = Plugin.Instance?.Configuration.Enabled ?? false,
        animatedBackground = Plugin.Instance?.Configuration.AnimatedBackground ?? false,
        webVersion = "12.1",
        version = typeof(Plugin).Assembly.GetName().Version?.ToString()
    });
}
