using System;
using System.Numerics;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.Command;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Services;

namespace FollowMe;

public sealed class Plugin : IDalamudPlugin
{
    [PluginService] private static IDalamudPluginInterface PluginInterface { get; set; } = null!;
    [PluginService] private static ICommandManager CommandManager { get; set; } = null!;
    [PluginService] private static IFramework Framework { get; set; } = null!;
    [PluginService] private static ITargetManager TargetManager { get; set; } = null!;
    [PluginService] private static IObjectTable ObjectTable { get; set; } = null!;
    [PluginService] private static IChatGui ChatGui { get; set; } = null!;
    [PluginService] private static IPluginLog Log { get; set; } = null!;

    private readonly Configuration config;

    private readonly ICallGateSubscriber<bool> navIsReady;
    private readonly ICallGateSubscriber<Vector3, bool, float, bool> moveCloseTo;
    private readonly ICallGateSubscriber<object> pathStop;

    private ulong followedObjectId;
    private string followedName = string.Empty;
    private Vector3 lastRequestedPosition = new(float.NaN, float.NaN, float.NaN);
    private DateTime lastPathRequestUtc = DateTime.MinValue;
    private bool following;

    public Plugin()
    {
        config = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

        // v2 changes the default from a polite trailing distance to near-zero so
        // the player stays essentially on top of the followed NPC.
        if (config.Version < 2)
        {
            config.Version = 2;
            config.FollowDistance = 0.1f;
            PluginInterface.SavePluginConfig(config);
        }

        navIsReady = PluginInterface.GetIpcSubscriber<bool>("vnavmesh.Nav.IsReady");
        moveCloseTo = PluginInterface.GetIpcSubscriber<Vector3, bool, float, bool>("vnavmesh.SimpleMove.PathfindAndMoveCloseTo");
        pathStop = PluginInterface.GetIpcSubscriber<object>("vnavmesh.Path.Stop");

        CommandManager.AddHandler("/followme", new CommandInfo(OnCommand)
        {
            HelpMessage = "Follow your targeted NPC. Usage: /followme [stop|status|distance <yalms>]"
        });

        Framework.Update += OnFrameworkUpdate;
    }

    public void Dispose()
    {
        Framework.Update -= OnFrameworkUpdate;
        CommandManager.RemoveHandler("/followme");
        StopFollowing(false);
    }

    private void OnCommand(string command, string args)
    {
        var trimmed = args.Trim();

        if (trimmed.Equals("stop", StringComparison.OrdinalIgnoreCase))
        {
            StopFollowing();
            return;
        }

        if (trimmed.Equals("status", StringComparison.OrdinalIgnoreCase))
        {
            ChatGui.Print(following
                ? $"[Follow Me] Following {followedName} at {config.FollowDistance:0.0} yalms."
                : $"[Follow Me] Not following anything. Follow distance: {config.FollowDistance:0.0} yalms.");
            return;
        }

        if (trimmed.StartsWith("distance ", StringComparison.OrdinalIgnoreCase))
        {
            if (float.TryParse(trimmed["distance ".Length..].Trim(), out var distance))
                SetDistance(distance);
            else
                ChatGui.PrintError("[Follow Me] Example: /followme distance 3");
            return;
        }

        if (trimmed.Length > 0 && float.TryParse(trimmed, out var shorthandDistance))
        {
            SetDistance(shorthandDistance);
            if (!following)
                StartFollowingCurrentTarget();
            return;
        }

        if (following)
            StopFollowing();
        else
            StartFollowingCurrentTarget();
    }

    private void SetDistance(float distance)
    {
        config.FollowDistance = Math.Clamp(distance, 0.0f, 15.0f);
        PluginInterface.SavePluginConfig(config);
        ChatGui.Print($"[Follow Me] Follow distance set to {config.FollowDistance:0.0} yalms.");
    }

    private void StartFollowingCurrentTarget()
    {
        var target = TargetManager.Target;
        if (target == null)
        {
            ChatGui.PrintError("[Follow Me] Target an NPC first.");
            return;
        }

        if (target.ObjectKind is not ObjectKind.BattleNpc and not ObjectKind.EventNpc)
        {
            ChatGui.PrintError($"[Follow Me] {target.Name.TextValue} is not an NPC target ({target.ObjectKind}).");
            return;
        }

        try
        {
            if (!navIsReady.HasFunction || !navIsReady.InvokeFunc())
            {
                ChatGui.PrintError("[Follow Me] vnavmesh is not ready. Install/enable vnavmesh first.");
                return;
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not contact vnavmesh.");
            ChatGui.PrintError("[Follow Me] Could not contact vnavmesh. Make sure it is installed and enabled.");
            return;
        }

        followedObjectId = target.GameObjectId;
        followedName = target.Name.TextValue;
        lastRequestedPosition = new Vector3(float.NaN, float.NaN, float.NaN);
        lastPathRequestUtc = DateTime.MinValue;
        following = true;

        ChatGui.Print($"[Follow Me] Following {followedName}. Type /followme again to stop.");
    }

    private void StopFollowing(bool announce = true)
    {
        var wasFollowing = following;
        following = false;
        followedObjectId = 0;
        followedName = string.Empty;
        TryStopPath();

        if (announce && wasFollowing)
            ChatGui.Print("[Follow Me] Stopped following.");
    }

    private void OnFrameworkUpdate(IFramework framework)
    {
        if (!following)
            return;

        var localPlayer = ObjectTable.LocalPlayer;
        if (localPlayer == null)
            return;

        var target = ObjectTable.SearchById(followedObjectId);
        if (target == null)
        {
            ChatGui.PrintError("[Follow Me] Lost the NPC; stopping.");
            StopFollowing(false);
            return;
        }

        var distance = Vector3.Distance(localPlayer.Position, target.Position);

        if (distance <= config.FollowDistance + 0.10f)
        {
            TryStopPath();
            return;
        }

        var now = DateTime.UtcNow;
        var targetMoved = float.IsNaN(lastRequestedPosition.X) ||
                          Vector3.Distance(lastRequestedPosition, target.Position) >= 0.25f;
        var enoughTimePassed = (now - lastPathRequestUtc).TotalMilliseconds >= 250;

        if (!targetMoved || !enoughTimePassed)
            return;

        try
        {
            if (!navIsReady.HasFunction || !navIsReady.InvokeFunc())
                return;

            if (pathStop.HasAction)
                pathStop.InvokeAction();

            if (moveCloseTo.HasFunction)
            {
                moveCloseTo.InvokeFunc(target.Position, false, config.FollowDistance);
                lastRequestedPosition = target.Position;
                lastPathRequestUtc = now;
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "vnavmesh follow update failed.");
        }
    }

    private void TryStopPath()
    {
        try
        {
            if (pathStop.HasAction)
                pathStop.InvokeAction();
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Ignoring vnavmesh stop failure.");
        }
    }
}
