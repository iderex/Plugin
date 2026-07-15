using MediaBrowser.Controller;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Logging;

namespace Emby.Plugins.Moonfin
{
    /// <summary>Initializes plugin-owned service singletons once at server start.</summary>
    public class ServerEntryPoint : IServerEntryPoint
    {
        private readonly ILogManager _logManager;
        private readonly ILibraryManager _libraryManager;
        private readonly IUserManager _userManager;
        private readonly ISessionManager _sessionManager;
        private readonly IServerApplicationHost _appHost;

        public ServerEntryPoint(ILogManager logManager, ILibraryManager libraryManager, IUserManager userManager, ISessionManager sessionManager, IServerApplicationHost appHost)
        {
            _logManager = logManager;
            _libraryManager = libraryManager;
            _userManager = userManager;
            _sessionManager = sessionManager;
            _appHost = appHost;
        }

        public void Run()
        {
            var plugin = Plugin.Instance;
            if (plugin == null) return;

            PluginServices.LibraryManager = _libraryManager;
            PluginServices.UserManager = _userManager;
            PluginServices.SessionManager = _sessionManager;

            plugin.MigrateConfiguration();
            plugin.InitializeServices(_logManager, _appHost);
        }

        public void Dispose() { }
    }

    /// <summary>Holds server-level services that stateless API request handlers access statically.</summary>
    internal static class PluginServices
    {
        public static ILibraryManager? LibraryManager { get; set; }
        public static IUserManager? UserManager { get; set; }
        public static ISessionManager? SessionManager { get; set; }
    }
}
