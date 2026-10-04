using Dalamud.Configuration;

namespace FollowMe;

public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 2;
    public float FollowDistance { get; set; } = 0.1f;
}
