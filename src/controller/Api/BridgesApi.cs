using System.Linq;
using Recastify.Models;
using Recastify.Services;

namespace Recastify.Api;

public static class BridgesApi
{
    private static readonly HttpClient _httpClient = new() { Timeout = Timeout.InfiniteTimeSpan };

    public static void MapBridgesApi(this WebApplication app)
    {
        app.MapGet("/api/bridges", (HttpContext http, BridgeManager bridges, ConfigService config) =>
        {
            var requestHost = http.Request.Host.Host;
            var all = bridges.GetAll();
            
            var responseBridges = all.Select(b => {
                var rewrittenStreamUrl = b.StreamUrl;
                if (rewrittenStreamUrl != null)
                {
                    var uri = new Uri(rewrittenStreamUrl);
                    rewrittenStreamUrl = $"{uri.Scheme}://{requestHost}:{uri.Port}{uri.AbsolutePath}";
                }
                return new Bridge
                {
                    Id = b.Id,
                    Name = b.Name,
                    Mount = b.Mount,
                    StreamUrl = rewrittenStreamUrl,
                    State = b.State,
                    Listeners = b.Listeners,
                    Ip = b.Ip,
                    Bitrate = b.Bitrate,
                    Enabled = b.Enabled,
                    LastStateChange = b.LastStateChange,
                    NowPlaying = b.NowPlaying
                };
            }).ToList();

            var response = new BridgesResponse 
            { 
                Bridges = responseBridges,
                DisableStreamProxy = config.Config.WebUi.DisableStreamProxy
            };
            return Results.Json(response, AppJsonContext.Default.BridgesResponse);
        });

        app.MapPost("/api/bridges", (BridgeCreateRequest request, BridgeManager bridges, ConfigService config) =>
        {
            if (string.IsNullOrEmpty(request.Name) || string.IsNullOrEmpty(request.Mount))
                return Results.BadRequest("name and mount are required");

            var id = request.Mount.TrimStart('/');
            if (string.IsNullOrEmpty(id)) id = request.Name.ToLowerInvariant().Replace(' ', '-');

            var bridge = bridges.GetOrCreate(id);
            bridge.Name = request.Name;
            bridge.Mount = request.Mount;
            bridge.Ip = request.Ip;
            bridge.Bitrate = request.Bitrate ?? "320k";
            bridge.Enabled = request.Enabled ?? true;

            config.SyncFromBridges(bridges.GetAll());

            return Results.Json(bridge, AppJsonContext.Default.Bridge);
        });

        app.MapPut("/api/bridges/{id}", (string id, BridgeCreateRequest request, BridgeManager bridges, ConfigService config) =>
        {
            var bridge = bridges.Get(id);
            if (bridge == null)
                return Results.NotFound("bridge not found");

            if (!string.IsNullOrEmpty(request.Name)) bridge.Name = request.Name;
            if (!string.IsNullOrEmpty(request.Mount)) bridge.Mount = request.Mount;
            if (request.Ip != null) bridge.Ip = request.Ip;
            if (!string.IsNullOrEmpty(request.Bitrate)) bridge.Bitrate = request.Bitrate;
            if (request.Enabled.HasValue) bridge.Enabled = request.Enabled.Value;

            config.SyncFromBridges(bridges.GetAll());

            return Results.Json(bridge, AppJsonContext.Default.Bridge);
        });

        app.MapDelete("/api/bridges/{id}", (string id, BridgeManager bridges, ConfigService config) =>
        {
            var removed = bridges.Remove(id);
            if (!removed)
                return Results.NotFound("bridge not found");

            config.SyncFromBridges(bridges.GetAll());

            return Results.Ok();
        });

        app.MapPost("/api/bridges/{id}/start", (string id, BridgeManager bridges) =>
        {
            var bridge = bridges.Get(id);
            if (bridge == null)
                return Results.NotFound("bridge not found");

            bridge.State = "starting";
            bridge.Enabled = true;
            bridge.LastStateChange = DateTime.UtcNow;
            return Results.Ok();
        });

        app.MapPost("/api/bridges/{id}/stop", (string id, BridgeManager bridges) =>
        {
            var bridge = bridges.Get(id);
            if (bridge == null)
                return Results.NotFound("bridge not found");

            bridge.State = "offline";
            bridge.Enabled = false;
            bridge.LastStateChange = DateTime.UtcNow;
            return Results.Ok();
        });

        app.MapPost("/api/status", (StatusHookRequest request, BridgeManager bridges) =>
        {
            if (string.IsNullOrEmpty(request.Bridge) || string.IsNullOrEmpty(request.State))
                return Results.BadRequest("bridge and state are required");

            bridges.UpdateState(request.Bridge, request.State);
            return Results.Ok();
        });

        app.MapGet("/api/bridges/{id}/art", (string id, BridgeManager bridges) =>
        {
            var bridge = bridges.Get(id);
            if (bridge?.CoverArt is not { Length: > 0 })
                return Results.NotFound("no artwork");

            var contentType = bridge.CoverArt[0] == 0xFF ? "image/jpeg" : "image/png";
            return Results.File(bridge.CoverArt, contentType);
        });

        // Proxy the Icecast stream through the controller so the <audio> element
        // can load it from the same origin. iOS standalone (PWA) mode silently
        // mutes cross-origin audio even though an audio session is created.
        app.MapGet("/api/bridges/{id}/stream", async (string id, HttpContext http, BridgeManager bridges, ConfigService config) =>
        {
            if (config.Config.WebUi.DisableStreamProxy)
            {
                http.Response.StatusCode = 400;
                await http.Response.WriteAsync("Stream proxy is disabled in configuration.");
                return;
            }

            var bridge = bridges.Get(id);
            if (bridge?.StreamUrl == null)
            {
                http.Response.StatusCode = 404;
                return;
            }

            try
            {
                using var upstream = await _httpClient.GetAsync(bridge.StreamUrl, HttpCompletionOption.ResponseHeadersRead, http.RequestAborted);
                if (!upstream.IsSuccessStatusCode)
                {
                    http.Response.StatusCode = (int)upstream.StatusCode;
                    return;
                }

                http.Response.StatusCode = 200;
                http.Response.ContentType = upstream.Content.Headers.ContentType?.ToString() ?? "audio/mpeg";
                http.Response.Headers["Cache-Control"] = "no-cache";

                using var source = await upstream.Content.ReadAsStreamAsync(http.RequestAborted);
                var buffer = new byte[4096];
                int bytesRead;
                while ((bytesRead = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), http.RequestAborted)) > 0)
                {
                    await http.Response.Body.WriteAsync(buffer.AsMemory(0, bytesRead), http.RequestAborted);
                    await http.Response.Body.FlushAsync(http.RequestAborted);
                }
            }
            catch (OperationCanceledException) { }
            catch (IOException) { }
        });

        // DACP remote control: playpause | nextitem | previtem | volumeup | volumedown
        app.MapPost("/api/bridges/{id}/command/{command}", async (string id, string command, BridgeManager bridges) =>
        {
            var bridge = bridges.Get(id);
            if (bridge == null) return Results.NotFound("bridge not found");

            var np = bridge.NowPlaying;
            if (np == null || !np.HasRemoteControl)
                return Results.BadRequest("no remote control session available");

            var allowed = new[] { "playpause", "nextitem", "previtem", "volumeup", "volumedown" };
            if (!Array.Exists(allowed, c => c == command))
                return Results.BadRequest("unknown command");

            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
            http.DefaultRequestHeaders.Add("Active-Remote", np.ActiveRemote!);
            var url = $"http://{np.DacpHost}:3689/ctrl-int/1/{command}";
            try
            {
                await http.GetAsync(url);
                return Results.Ok();
            }
            catch
            {
                return Results.StatusCode(502);
            }
        });

        app.MapPost("/api/config", (AppConfigUpdateRequest request, ConfigService config) =>
        {
            config.Config.WebUi.DisableStreamProxy = request.DisableStreamProxy;
            var configPath = Environment.GetEnvironmentVariable("CONFIG_PATH") ?? "/app/config.yaml";
            try
            {
                config.SaveToFile(configPath);
            }
            catch { }
            return Results.Json(request, AppJsonContext.Default.AppConfigUpdateRequest);
        });
    }
}
