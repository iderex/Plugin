using MediaBrowser.Controller.Net;
using MediaBrowser.Model.Services;

namespace Emby.Plugins.Moonfin.Api
{
    [Route("/Moonfin/Collections/{CollectionId}/Order", "GET")]
    [Authenticated]
    public class GetCollectionOrderRequest : IReturn<object>
    {
        public string CollectionId { get; set; } = string.Empty;
    }

    [Route("/Moonfin/Collections/{CollectionId}/Order", "POST")]
    [Authenticated]
    public class SaveCollectionOrderRequest : IReturn<object>, IRequiresRequestStream
    {
        public string CollectionId { get; set; } = string.Empty;
        public System.IO.Stream RequestStream { get; set; } = null!;
    }
}
