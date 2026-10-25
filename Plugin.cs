using System;
using System.Collections.Generic;
using JellyPrecache.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace JellyPrecache;

public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    public static Plugin? Instance { get; private set; }

    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
        Configuration.ImageTypes = ImageSizing.Normalize(Configuration.ImageTypes);
    }

    public override string Name => "JellyPrecache";

    public override Guid Id => Guid.Parse("f4584150-00aa-4ca8-893a-1027e2802606");

    public IEnumerable<PluginPageInfo> GetPages()
    {
        return new[]
        {
            new PluginPageInfo
            {
                Name = Name,
                EmbeddedResourcePath = GetType().Namespace + ".Configuration.configPage.html",
                EnableInMainMenu = Configuration.ShowInAdminMenu
            }
        };
    }
}
