using Dalamud.Configuration;

namespace FollowMe;

public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;
    public float FollowDistance { get; set; } = 2.5f;
}
