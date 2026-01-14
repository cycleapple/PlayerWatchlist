#nullable enable
using System;
using Dalamud.Game.ClientState.Objects.SubKinds;
using FFXIVClientStructs.FFXIV.Client.Game.Character;

namespace PlayerWatchlist.Models;

public class WatchedPlayer : IEquatable<WatchedPlayer>
{
    public ulong ContentId { get; set; }
    public ulong AccountId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string WorldName { get; set; } = string.Empty;
    public string Note { get; set; } = string.Empty;
    public DateTime AddedTime { get; set; } = DateTime.Now;
    public DateTime LastSeenTime { get; set; } = DateTime.MinValue;
    public string LastSeenLocation { get; set; } = string.Empty;

    public WatchedPlayer() { }

    public WatchedPlayer(ulong contentId, ulong accountId, string name, string worldName)
    {
        ContentId = contentId;
        AccountId = accountId;
        Name = name;
        WorldName = worldName;
        AddedTime = DateTime.Now;
    }

    public unsafe WatchedPlayer(IPlayerCharacter player)
    {
        var chara = (Character*)player.Address;
        ContentId = chara->ContentId;
        AccountId = chara->AccountId;
        Name = player.Name.TextValue;
        WorldName = player.HomeWorld.Value.Name.ToString();
        AddedTime = DateTime.Now;
    }

    public bool Equals(WatchedPlayer? other)
    {
        if (other == null) return false;
        return ContentId == other.ContentId || AccountId == other.AccountId;
    }

    public override bool Equals(object? obj)
    {
        if (obj is WatchedPlayer other)
            return Equals(other);
        return false;
    }

    public override int GetHashCode()
    {
        return ContentId.GetHashCode();
    }

    public override string ToString()
    {
        return $"{Name}@{WorldName}";
    }
}
