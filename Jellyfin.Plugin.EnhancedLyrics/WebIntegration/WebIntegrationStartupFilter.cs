using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.EnhancedLyrics.WebIntegration;

public sealed class WebIntegrationStartupFilter(IServerConfigurationManager configuration, ILogger<WebIntegrationStartupFilter> logger) : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.Use(downstream => new WebIntegrationMiddleware(downstream,
            () => Plugin.Instance?.Configuration.Enabled == true,
            () => configuration.GetNetworkConfiguration().BaseUrl, logger).InvokeAsync);
        next(app);
    };
}
