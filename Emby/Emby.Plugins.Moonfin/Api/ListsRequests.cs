using MediaBrowser.Controller.Net;
using MediaBrowser.Model.Services;

namespace Emby.Plugins.Moonfin.Api
{
    [Route("/Moonfin/MdbList/Lists", "GET")]
    [Authenticated]
    public class GetMdbListsRequest : IReturn<object>
    {
    }

    [Route("/Moonfin/MdbList/Lists/{Slug}/Items", "GET")]
    [Authenticated]
    public class GetMdbListItemsRequest : IReturn<object>
    {
        public string? Slug { get; set; }
        public string? Mediatype { get; set; }
    }
}
