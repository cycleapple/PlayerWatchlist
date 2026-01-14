#nullable enable
using System;
using System.Collections.Generic;
using Dalamud.Configuration;
using Dalamud.Plugin;
using PlayerWatchlist.Models;

namespace PlayerWatchlist;

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    public List<WatchedPlayer> Blacklist { get; set; } = new();
    public List<WatchedPlayer> Whitelist { get; set; } = new();

    public int DetectionRadius { get; set; } = 50;
    public bool ShowLines { get; set; } = true;
    public bool SendChatNotification { get; set; } = true;
    public bool SendSystemNotification { get; set; } = true;
    public float LineThickness { get; set; } = 4f;

    public uint BlacklistLineColor { get; set; } = 0xFF0000FF;
    public uint WhitelistLineColor { get; set; } = 0xFF00FF00;

    [NonSerialized]
    private IDalamudPluginInterface? pluginInterface;

    public void Initialize(IDalamudPluginInterface pluginInterface)
    {
        this.pluginInterface = pluginInterface;
    }

    public void Save()
    {
        pluginInterface?.SavePluginConfig(this);
    }
}
