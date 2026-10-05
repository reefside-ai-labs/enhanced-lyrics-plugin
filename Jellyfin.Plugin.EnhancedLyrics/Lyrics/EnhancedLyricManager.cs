using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Lyrics;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Lyrics;
using MediaBrowser.Model.Providers;
using Microsoft.AspNetCore.Http;

namespace Jellyfin.Plugin.EnhancedLyrics.Lyrics;

// Preserve all built-in management/provider behavior; override local reads only.
public sealed class EnhancedLyricManager(ILyricManager inner, LocalLyricReader reader, IHttpContextAccessor context, IUserManager users) : ILyricManager
{
    public event EventHandler<LyricDownloadFailureEventArgs> LyricDownloadFailure
    {
        add => inner.LyricDownloadFailure += value;
        remove => inner.LyricDownloadFailure -= value;
    }
    public async Task<LyricDto?> GetLyricsAsync(Audio audio, CancellationToken cancellationToken)
    {
        if (Plugin.Instance?.Configuration.Enabled != false)
        {
            var userId = context.HttpContext?.User.FindFirst("Jellyfin-UserId")?.Value;
            var language = Guid.TryParse(userId, out var id) ? users.GetUserById(id)?.AudioLanguagePreference : null;
            language ??= context.HttpContext?.Request.GetTypedHeaders().AcceptLanguage?.OrderByDescending(h => h.Quality ?? 1).FirstOrDefault()?.Value.Value;
            var lyrics = await reader.ReadAsync(audio.Path, language, cancellationToken).ConfigureAwait(false);
            if (lyrics is not null) return lyrics.ToLegacy();
        }
        return await inner.GetLyricsAsync(audio, cancellationToken).ConfigureAwait(false);
    }
    public Task<IReadOnlyList<RemoteLyricInfoDto>> SearchLyricsAsync(Audio audio, bool isAutomated, CancellationToken cancellationToken) => inner.SearchLyricsAsync(audio, isAutomated, cancellationToken);
    public Task<IReadOnlyList<RemoteLyricInfoDto>> SearchLyricsAsync(LyricSearchRequest request, CancellationToken cancellationToken) => inner.SearchLyricsAsync(request, cancellationToken);
    public Task<LyricDto?> DownloadLyricsAsync(Audio audio, string lyricId, CancellationToken cancellationToken) => inner.DownloadLyricsAsync(audio, lyricId, cancellationToken);
    public Task<LyricDto?> DownloadLyricsAsync(Audio audio, LibraryOptions libraryOptions, string lyricId, CancellationToken cancellationToken) => inner.DownloadLyricsAsync(audio, libraryOptions, lyricId, cancellationToken);
    public Task<LyricDto?> SaveLyricAsync(Audio audio, string format, string lyrics) => inner.SaveLyricAsync(audio, format, lyrics);
    public Task<LyricDto?> SaveLyricAsync(Audio audio, string format, Stream lyrics) => inner.SaveLyricAsync(audio, format, lyrics);
    public Task<LyricDto?> GetRemoteLyricsAsync(string id, CancellationToken cancellationToken) => inner.GetRemoteLyricsAsync(id, cancellationToken);
    public Task DeleteLyricsAsync(Audio audio) => inner.DeleteLyricsAsync(audio);
    public IReadOnlyList<LyricProviderInfo> GetSupportedProviders(BaseItem item) => inner.GetSupportedProviders(item);
}
