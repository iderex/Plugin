using MediaBrowser.Model.Services;

namespace Emby.Plugins.Moonfin.Api
{
    [Route("/Moonfin/Assets/{FileName}", "GET")]
    public class GetAssetRequest : IReturn<object>
    {
        public string? FileName { get; set; }
    }
}
