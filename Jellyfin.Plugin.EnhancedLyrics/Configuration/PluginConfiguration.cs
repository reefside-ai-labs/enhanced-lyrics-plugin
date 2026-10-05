using MediaBrowser.Model.Plugins;
namespace Jellyfin.Plugin.EnhancedLyrics.Configuration;
public class PluginConfiguration : BasePluginConfiguration
{
    public bool Enabled { get; set; } = true;
    public bool AnimatedBackground { get; set; } = true;
}
