using MediaBrowser.Model.Services;

namespace Emby.Plugins.Moonfin.Api
{
    [Route("/Moonfin/Web/loader.js", "GET")]
    public class GetLoaderJsRequest : IReturn<object> { }

    [Route("/Moonfin/Web/config.json", "GET")]
    public class GetWebConfigRequest : IReturn<object> { }

    [Route("/Moonfin/Web/plugin.js", "GET")]
    public class GetPluginJsRequest : IReturn<object> { }

    [Route("/Moonfin/Web/plugin.css", "GET")]
    public class GetPluginCssRequest : IReturn<object> { }

    [Route("/Moonfin/Web/{Path*}", "GET")]
    public class GetWebAssetRequest : IReturn<object>
    {
        public string? Path { get; set; }
    }
}
