using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Emby.Plugins.Moonfin.Services;
using MediaBrowser.Common;
using MediaBrowser.Controller.Net;
using MediaBrowser.Model.Services;

namespace Emby.Plugins.Moonfin.Api
{
    public class ThemesService : IService, IRequiresRequest, IHasResultFactory
    {
        private readonly IAuthorizationContext _authContext;

        public IRequest Request { get; set; } = null!;
        public IHttpResultFactory ResultFactory { get; set; } = null!;

        private MoonfinThemeStore ThemeStore => Plugin.Instance?.ThemeStore
            ?? throw new InvalidOperationException("MoonfinThemeStore not initialized");

        private MoonfinSettingsService Settings => Plugin.Instance?.SettingsService
            ?? throw new InvalidOperationException("MoonfinSettingsService not initialized");

        public ThemesService(IApplicationHost appHost)
        {
            _authContext = appHost.Resolve<IAuthorizationContext>();
            ResultFactory = appHost.Resolve<IHttpResultFactory>();
        }

        private object Json(object? body) => MoonfinJson.Result(Request, ResultFactory, body);
        private object Json(int statusCode, object? body) { Request.Response.StatusCode = statusCode; return Json(body); }

        public async Task<object> Get(GetThemesRequest request)
        {
            if (Plugin.Instance?.Configuration?.EnableSettingsSync != true)
                return Json(503, new { error = "Settings sync is disabled" });

            var themes = await ThemeStore.ListThemesAsync().ConfigureAwait(false);
            return Json(themes);
        }

        public async Task<object> Get(GetThemeByIdRequest request)
        {
            if (Plugin.Instance?.Configuration?.EnableSettingsSync != true)
                return Json(503, new { error = "Settings sync is disabled" });

            var theme = await ThemeStore.GetThemeAsync(request.ThemeId ?? string.Empty).ConfigureAwait(false);
            if (!theme.HasValue) return Json(404, new { error = "Theme not found" });
            return Json(theme.Value);
        }

        public object Get(GetAdminThemesRequest request)
        {
            return Json(new { items = ThemeStore.GetThemeIndex() });
        }

        public async Task<object> Post(UploadThemeRequest request)
        {
            JsonElement payload;
            try
            {
                var inputStream = request.RequestStream;
                if (inputStream == null) return Json(400, new { error = "No body provided." });

                using var ms = new MemoryStream();
                await inputStream.CopyToAsync(ms).ConfigureAwait(false);
                if (ms.Length == 0) return Json(400, new { error = "No body provided." });

                using var doc = JsonDocument.Parse(ms.ToArray());
                payload = doc.RootElement.Clone();
            }
            catch (JsonException)
            {
                return Json(400, new { error = "Invalid JSON body." });
            }

            if (payload.ValueKind != JsonValueKind.Object)
                return Json(400, new { error = "Theme payload must be a JSON object." });

            var userId = AuthHelpers.GetCurrentUserId(Request, _authContext);
            var saveResult = await ThemeStore.SaveThemeAsync(payload, userId).ConfigureAwait(false);

            if (saveResult.Entry == null)
                return Json(400, new { error = "Theme validation failed.", errors = saveResult.Errors });

            Settings.BroadcastSystemEvent("themesChanged");
            return Json(new { success = true, item = saveResult.Entry });
        }

        public async Task<object> Delete(DeleteThemeRequest request)
        {
            var removed = await ThemeStore.DeleteThemeAsync(request.ThemeId ?? string.Empty).ConfigureAwait(false);
            if (!removed) return Json(404, new { error = "Theme not found" });

            Settings.BroadcastSystemEvent("themesChanged");
            return Json(new { success = true });
        }
    }
}
