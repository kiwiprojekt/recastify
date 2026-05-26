using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;
using YamlDotNet.Serialization.Utilities;

namespace Recastify.Services;

[YamlStaticContext]
[YamlSerializable(typeof(AppConfig))]
[YamlSerializable(typeof(IcecastConfig))]
[YamlSerializable(typeof(WebUiConfig))]
[YamlSerializable(typeof(NetworkConfig))]
[YamlSerializable(typeof(BridgeConfig))]
[YamlSerializable(typeof(List<BridgeConfig>))]
public partial class YamlContext : StaticContext { }

public class ConfigService
{
    private static readonly YamlContext _yamlContext = new();

    public AppConfig Config { get; private set; } = new();

    public void LoadFromFile(string path)
    {
        if (!File.Exists(path))
            return;

        var yaml = File.ReadAllText(path);
        var deserializer = new StaticDeserializerBuilder(_yamlContext)
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();

        Config = deserializer.Deserialize<AppConfig>(yaml) ?? new AppConfig();
    }

    public void LoadFromEnvironment()
    {
        var name = Environment.GetEnvironmentVariable("AIRPLAY_NAME") ?? "Recastify";
        var mount = Environment.GetEnvironmentVariable("ICECAST_MOUNT") ?? "/stream";
        var bitrate = Environment.GetEnvironmentVariable("AUDIO_BITRATE") ?? "320k";
        var disableProxy = (Environment.GetEnvironmentVariable("DISABLE_STREAM_PROXY") ?? "true").ToLowerInvariant() == "true";

        Config.Bridges.Clear();
        Config.Bridges.Add(new BridgeConfig
        {
            Name = name,
            Mount = mount,
            Bitrate = bitrate,
            Enabled = true
        });
        Config.WebUi.DisableStreamProxy = disableProxy;
    }

    public void SaveToFile(string path)
    {
        var serializer = new StaticSerializerBuilder(_yamlContext)
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .Build();

        var yaml = serializer.Serialize(Config);
        File.WriteAllText(path, yaml);
    }

    public void SyncFromBridges(List<Recastify.Models.Bridge> bridges)
    {
        Config.Bridges.Clear();
        foreach (var b in bridges)
        {
            Config.Bridges.Add(new BridgeConfig
            {
                Name = b.Name,
                Mount = b.Mount,
                Ip = b.Ip,
                Bitrate = b.Bitrate,
                Enabled = b.Enabled
            });
        }

        var configPath = Environment.GetEnvironmentVariable("CONFIG_PATH") ?? "/app/config.yaml";
        try
        {
            SaveToFile(configPath);
        }
        catch
        {
            // Config path may not be writable (e.g., mounted read-only)
        }
    }
}

public class AppConfig
{
    public IcecastConfig Icecast { get; set; } = new();
    public WebUiConfig WebUi { get; set; } = new();
    public NetworkConfig Network { get; set; } = new();
    public List<BridgeConfig> Bridges { get; set; } = new();
}

public class IcecastConfig
{
    public string Host { get; set; } = "icecast";
    public int Port { get; set; } = 8000;
    public string SourcePassword { get; set; } = "changeme";
    public string AdminPassword { get; set; } = "changeme";
    public int MaxClients { get; set; } = 20;
}

public class WebUiConfig
{
    public int Port { get; set; } = 3000;
    public bool DisableStreamProxy { get; set; } = true;
}

public class NetworkConfig
{
    public string Mode { get; set; } = "host";
    public string ParentInterface { get; set; } = "eth0";
    public string Subnet { get; set; } = "192.168.1.0/24";
    public string Gateway { get; set; } = "192.168.1.1";
}

public class BridgeConfig
{
    public string Name { get; set; } = "Recastify";
    public string Mount { get; set; } = "/stream";
    public string? Ip { get; set; }
    public string Bitrate { get; set; } = "320k";
    public bool Enabled { get; set; } = true;
}
