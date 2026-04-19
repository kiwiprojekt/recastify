using Recastify.Api;
using Recastify.Models;
using Recastify.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonContext.Default);
    options.SerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.SnakeCaseLower;
});

var icecastHost = Environment.GetEnvironmentVariable("ICECAST_HOST") ?? "localhost";
var icecastPort = int.Parse(Environment.GetEnvironmentVariable("ICECAST_PORT") ?? "8100");
var airplayName = Environment.GetEnvironmentVariable("AIRPLAY_NAME") ?? "Recastify";
var icecastMount = Environment.GetEnvironmentVariable("ICECAST_MOUNT") ?? "/stream";
var audioBitrate = Environment.GetEnvironmentVariable("AUDIO_BITRATE") ?? "320k";
var webUiPort = Environment.GetEnvironmentVariable("WEB_UI_PORT") ?? "3000";
var mqttHost = Environment.GetEnvironmentVariable("MQTT_HOST") ?? "localhost";
var mqttPort = int.Parse(Environment.GetEnvironmentVariable("MQTT_PORT") ?? "1883");
var configPath = Environment.GetEnvironmentVariable("CONFIG_PATH") ?? "/app/config.yaml";

// Load config from file or environment
var configService = new ConfigService();
if (File.Exists(configPath))
{
    configService.LoadFromFile(configPath);
}
else
{
    configService.LoadFromEnvironment();
}

builder.Services.AddSingleton(configService);

var bridgeManager = new BridgeManager(
    configService.Config.Icecast.Host != "icecast" ? configService.Config.Icecast.Host : icecastHost,
    configService.Config.Icecast.Port != 8000 ? configService.Config.Icecast.Port : icecastPort);

// Initialize bridges from config
if (configService.Config.Bridges.Count > 0)
{
    foreach (var bc in configService.Config.Bridges)
    {
        var id = bc.Mount.TrimStart('/');
        if (string.IsNullOrEmpty(id)) id = "default";
        var bridge = bridgeManager.GetOrCreate(id);
        bridge.Name = bc.Name;
        bridge.Mount = bc.Mount;
        bridge.Ip = bc.Ip;
        bridge.Bitrate = bc.Bitrate;
        bridge.Enabled = bc.Enabled;
    }
}
else
{
    bridgeManager.EnsureDefault(airplayName, icecastMount, audioBitrate);
}

builder.Services.AddSingleton(bridgeManager);
builder.Services.AddHostedService(sp =>
    new Recastify.Services.IcecastPoller(bridgeManager, icecastHost, icecastPort));
builder.Services.AddHostedService(sp =>
    new Recastify.Services.MqttMetadataService(
        bridgeManager, mqttHost, mqttPort,
        sp.GetRequiredService<ILogger<Recastify.Services.MqttMetadataService>>()));

builder.WebHost.UseUrls($"http://0.0.0.0:{webUiPort}");

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapBridgesApi();
app.MapHealthApi();

app.Run();
