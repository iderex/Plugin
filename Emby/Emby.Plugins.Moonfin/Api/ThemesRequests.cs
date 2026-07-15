using MediaBrowser.Controller.Net;
using MediaBrowser.Model.Services;

namespace Emby.Plugins.Moonfin.Api
{
    [Route("/Moonfin/Themes", "GET")]
    [Authenticated]
    public class GetThemesRequest : IReturn<object> { }

    [Route("/Moonfin/Themes/{ThemeId}", "GET")]
    [Authenticated]
    public class GetThemeByIdRequest : IReturn<object>
    {
        public string? ThemeId { get; set; }
    }

    [Route("/Moonfin/Admin/Themes", "GET")]
    [Authenticated(Roles = "Admin")]
    public class GetAdminThemesRequest : IReturn<object> { }

    [Route("/Moonfin/Admin/Themes", "POST")]
    [Authenticated(Roles = "Admin")]
    public class UploadThemeRequest : IReturn<object>, IRequiresRequestStream
    {
        public System.IO.Stream RequestStream { get; set; } = null!;
    }

    [Route("/Moonfin/Admin/Themes/{ThemeId}", "DELETE")]
    [Authenticated(Roles = "Admin")]
    public class DeleteThemeRequest : IReturn<object>
    {
        public string? ThemeId { get; set; }
    }
}
