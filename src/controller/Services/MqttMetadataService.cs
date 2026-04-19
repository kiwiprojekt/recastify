using System.Text;
using MQTTnet;
using MQTTnet.Client;
using Recastify.Models;

namespace Recastify.Services;

public class MqttMetadataService : BackgroundService
{
    private readonly IMqttClient _client;
    private readonly BridgeManager _bridges;
    private readonly string _mqttHost;
    private readonly int _mqttPort;
    private readonly ILogger<MqttMetadataService> _logger;

    public MqttMetadataService(BridgeManager bridges, string mqttHost, int mqttPort, ILogger<MqttMetadataService> logger)
    {
        _bridges = bridges;
        _mqttHost = mqttHost;
        _mqttPort = mqttPort;
        _logger = logger;
        _client = new MqttFactory().CreateMqttClient();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _client.ApplicationMessageReceivedAsync += OnMessageReceived;
        _client.DisconnectedAsync += async e =>
        {
            _logger.LogWarning("MQTT disconnected. Reconnecting in 5s...");
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            await ConnectAsync(stoppingToken);
        };

        await ConnectAsync(stoppingToken);

        // Keep alive until cancellation
        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // Shutting down
        }
    }

    private async Task ConnectAsync(CancellationToken ct)
    {
        try
        {
            var options = new MqttClientOptionsBuilder()
                .WithTcpServer(_mqttHost, _mqttPort)
                .WithClientId("recastify-controller")
                .Build();

            await _client.ConnectAsync(options, ct);
            _logger.LogInformation("Connected to MQTT broker at {Host}:{Port}", _mqttHost, _mqttPort);

            // Subscribe to all bridges: airplay/+/# (+ = single-level wildcard)
            var subOptions = new MqttFactory().CreateSubscribeOptionsBuilder()
                .WithTopicFilter("airplay/+/#")
                .Build();
            await _client.SubscribeAsync(subOptions, ct);
            _logger.LogInformation("Subscribed to airplay/+/#");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to connect to MQTT broker");
        }
    }

    private Task OnMessageReceived(MqttApplicationMessageReceivedEventArgs e)
    {
        // Topic format: airplay/{bridgeId}/{field}
        var parts = e.ApplicationMessage.Topic.Split('/');
        if (parts.Length < 3)
            return Task.CompletedTask;

        var bridgeId = parts[1];
        var field = parts[2];
        var payload = e.ApplicationMessage.PayloadSegment;

        var bridge = _bridges.GetOrCreate(bridgeId);

        if (bridge.NowPlaying == null)
            bridge.NowPlaying = new NowPlaying();

        switch (field)
        {
            case "title":
                bridge.NowPlaying.Title = Encoding.UTF8.GetString(payload);
                break;
            case "artist":
                bridge.NowPlaying.Artist = Encoding.UTF8.GetString(payload);
                break;
            case "album":
                bridge.NowPlaying.Album = Encoding.UTF8.GetString(payload);
                break;
            case "genre":
                bridge.NowPlaying.Genre = Encoding.UTF8.GetString(payload);
                break;
            case "cover":
                bridge.CoverArt = payload.ToArray();
                bridge.NowPlaying.ArtworkUrl = $"/api/bridges/{bridgeId}/art";
                break;
            case "songtime":
                // Format: "elapsed_s duration_s" (seconds as floats, e.g. "42.5 180.0")
                var timeParts = Encoding.UTF8.GetString(payload).Split(' ');
                if (timeParts.Length >= 2 &&
                    double.TryParse(timeParts[0], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var elapsedSec) &&
                    double.TryParse(timeParts[1], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var durationSec))
                {
                    bridge.NowPlaying.ElapsedMs = (long)(elapsedSec * 1000);
                    bridge.NowPlaying.DurationMs = (long)(durationSec * 1000);
                }
                break;
            case "dacp_id":
                var dacpId = Encoding.UTF8.GetString(payload);
                bridge.NowPlaying.DacpId = string.IsNullOrEmpty(dacpId) || dacpId == "--" ? null : dacpId;
                break;
            case "active_remote":
                var activeRemote = Encoding.UTF8.GetString(payload);
                bridge.NowPlaying.ActiveRemote = string.IsNullOrEmpty(activeRemote) || activeRemote == "--" ? null : activeRemote;
                break;
            case "client_ip":
                var clientIp = Encoding.UTF8.GetString(payload);
                bridge.NowPlaying.DacpHost = string.IsNullOrEmpty(clientIp) || clientIp == "--" ? null : clientIp;
                break;
            case "play_start":
                bridge.State = "playing";
                bridge.LastStateChange = DateTime.UtcNow;
                break;
            case "play_end":
                bridge.State = "paused";
                bridge.LastStateChange = DateTime.UtcNow;
                break;
        }

        bridge.NowPlaying.UpdatedAt = DateTime.UtcNow;
        return Task.CompletedTask;
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_client.IsConnected)
        {
            await _client.DisconnectAsync(cancellationToken: cancellationToken);
        }

        _client.Dispose();
        await base.StopAsync(cancellationToken);
    }
}
