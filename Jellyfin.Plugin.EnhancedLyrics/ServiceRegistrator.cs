using Jellyfin.Plugin.EnhancedLyrics.Lyrics;
using Jellyfin.Plugin.EnhancedLyrics.WebIntegration;
using Microsoft.AspNetCore.Hosting;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Lyrics;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.EnhancedLyrics;

public sealed class ServiceRegistrator : IPluginServiceRegistrator
{
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddSingleton<IStartupFilter, WebIntegrationStartupFilter>();
        serviceCollection.AddSingleton<LyricParser>();
        serviceCollection.AddSingleton<LocalLyricReader>();
        serviceCollection.AddHttpContextAccessor();
        var original = serviceCollection.Last(d => d.ServiceType == typeof(ILyricManager));
        serviceCollection.Remove(original);
        serviceCollection.AddSingleton<ILyricManager>(services =>
        {
            var inner = (ILyricManager)(original.ImplementationInstance ?? original.ImplementationFactory?.Invoke(services)
                ?? ActivatorUtilities.CreateInstance(services, original.ImplementationType!));
            return ActivatorUtilities.CreateInstance<EnhancedLyricManager>(services, inner);
        });
    }
}
