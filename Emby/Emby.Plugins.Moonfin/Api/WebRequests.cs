using MediaBrowser.Controller.Net;
using MediaBrowser.Model.Services;

namespace Emby.Plugins.Moonfin.Api
{
    // Emby asks for a token on every plugin route that is not marked [Unauthenticated], and the web
    // frontend is the sign-in page itself, so a browser opening it has no token to send yet.

    [Route("/Moonfin/Web/loader.js", "GET")]
    [Unauthenticated]
    public class GetLoaderJsRequest : IReturn<object> { }

    // config.json has no dedicated route: Emby strips the .json suffix before matching, so it
    // could never win, and the /Moonfin/Web/{Path*} catch-all serves it instead.

    [Route("/Moonfin/Web/plugin.js", "GET")]
    [Unauthenticated]
    public class GetPluginJsRequest : IReturn<object> { }

    [Route("/Moonfin/Web/plugin.css", "GET")]
    [Unauthenticated]
    public class GetPluginCssRequest : IReturn<object> { }

    [Route("/Moonfin/Web/{Path*}", "GET")]
    [Unauthenticated]
    public class GetWebAssetRequest : IReturn<object>
    {
        public string? Path { get; set; }
    }
}
