using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Common;
using MediaBrowser.Controller.Net;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Services;
using MediaBrowser.Model.Session;

namespace Emby.Plugins.Moonfin.Api
{
    /// <summary>
    /// Exposes the server's active transcode sessions so the admin dashboard can show Moonfin
    /// client downloads in progress, read from Emby's public <see cref="SessionInfo.TranscodingInfo"/>.
    /// Progressive transcodes are the downloads clients start. HLS transcodes are ordinary playback
    /// and stay hidden unless asked for.
    /// </summary>
    public class TranscodesService : IService, IRequiresRequest, IHasResultFactory
    {
        public IRequest Request { get; set; } = null!;
        public IHttpResultFactory ResultFactory { get; set; } = null!;

        public TranscodesService(IApplicationHost appHost)
        {
            ResultFactory = appHost.Resolve<IHttpResultFactory>();
        }

        private object Json(object? body) => MoonfinJson.Result(Request, ResultFactory, body);

        public object Get(GetActiveTranscodesRequest request)
        {
            var sessionManager = PluginServices.SessionManager;
            if (sessionManager == null) return Json(Array.Empty<object>());

            var jobs = sessionManager.Sessions
                .Where(s => s?.TranscodingInfo != null)
                .Where(s => (s.TranscodingInfo.CompletionPercentage ?? 0) < 100)
                .Where(s => request.IncludePlayback || !IsHls(s.TranscodingInfo))
                .Select(s =>
                {
                    var ti = s.TranscodingInfo;
                    return new
                    {
                        Id = s.Id,
                        MediaSource = s.NowPlayingItem?.Name,
                        Framerate = ti.Framerate,
                        CompletionPercentage = ti.CompletionPercentage,
                        BitRate = ti.Bitrate,
                        PositionTicks = ti.TranscodingPositionTicks ?? s.PlayState?.PositionTicks,
                        RuntimeTicks = s.NowPlayingItem?.RunTimeTicks,
                        IsHardwareAccelerated = ti.VideoEncoderIsHardware || ti.VideoDecoderIsHardware,
                        Type = string.IsNullOrEmpty(ti.SubProtocol) ? "Progressive" : ti.SubProtocol,
                        UserName = s.UserName,
                        DeviceName = s.DeviceName,
                        Client = s.Client
                    };
                })
                .ToList();

            return Json(jobs);
        }

        public async Task<object> Delete(StopTranscodeRequest request)
        {
            var sessionManager = PluginServices.SessionManager;
            if (sessionManager == null)
            {
                Request.Response.StatusCode = 204;
                return null!;
            }

            // Emby has no force-kill for a transcode process, so send the session a Stop command,
            // which ends the client's playback and lets the server tear the transcode down.
            var session = sessionManager.Sessions.FirstOrDefault(s =>
                string.Equals(s?.Id, request.JobId, StringComparison.OrdinalIgnoreCase));

            if (session != null)
            {
                try
                {
                    await sessionManager.SendPlaystateCommand(
                        string.Empty,
                        session.Id,
                        new PlaystateRequest { Command = PlaystateCommand.Stop },
                        CancellationToken.None).ConfigureAwait(false);
                }
                catch
                {
                    // Best effort: a background download may have no controllable session.
                }
            }

            Request.Response.StatusCode = 204;
            return null!;
        }

        private static bool IsHls(TranscodingInfo ti) =>
            string.Equals(ti.SubProtocol, "hls", StringComparison.OrdinalIgnoreCase);
    }
}
