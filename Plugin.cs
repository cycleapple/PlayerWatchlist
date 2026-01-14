#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.Gui.ContextMenu;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using ImGuiNET;
using Lumina.Excel.Sheets;
using PlayerWatchlist.Models;
using PlayerWatchlist.Windows;

namespace PlayerWatchlist;

public sealed class Plugin : IDalamudPlugin
{
    public string Name => "PlayerWatchlist";

    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static IObjectTable ObjectTable { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;
    [PluginService] internal static IChatGui ChatGui { get; private set; } = null!;
    [PluginService] internal static INotificationManager NotificationManager { get; private set; } = null!;
    [PluginService] internal static IContextMenu ContextMenu { get; private set; } = null!;
    [PluginService] internal static IGameGui GameGui { get; private set; } = null!;
    [PluginService] internal static IDataManager DataManager { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;

    public Configuration Configuration { get; init; }
    public WindowSystem WindowSystem = new("PlayerWatchlist");
    private MainWindow MainWindow { get; init; }

    private DateTime lastUpdateTime = DateTime.MinValue;
    private readonly TimeSpan updateInterval = TimeSpan.FromMilliseconds(500);
    private readonly HashSet<ulong> notifiedPlayers = new();
    private readonly List<DetectedPlayer> currentDetectedPlayers = new();

    private class DetectedPlayer
    {
        public IPlayerCharacter Character { get; set; } = null!;
        public WatchedPlayer WatchedInfo { get; set; } = null!;
        public bool IsBlacklisted { get; set; }
    }

    public Plugin()
    {
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        Configuration.Initialize(PluginInterface);

        MainWindow = new MainWindow(this);
        WindowSystem.AddWindow(MainWindow);

        PluginInterface.UiBuilder.Draw += DrawUI;
        PluginInterface.UiBuilder.OpenConfigUi += DrawConfigUI;

        Framework.Update += OnFrameworkUpdate;
        ContextMenu.OnMenuOpened += OnMenuOpened;
        ClientState.TerritoryChanged += OnTerritoryChanged;

        CommandManager.AddHandler("/pwl", new Dalamud.Game.Command.CommandInfo(OnCommand)
        {
            HelpMessage = "開啟 PlayerWatchlist 設定視窗"
        });
    }

    public void Dispose()
    {
        WindowSystem.RemoveAllWindows();
        MainWindow.Dispose();

        PluginInterface.UiBuilder.Draw -= DrawUI;
        PluginInterface.UiBuilder.OpenConfigUi -= DrawConfigUI;

        Framework.Update -= OnFrameworkUpdate;
        ContextMenu.OnMenuOpened -= OnMenuOpened;
        ClientState.TerritoryChanged -= OnTerritoryChanged;

        CommandManager.RemoveHandler("/pwl");
    }

    private void OnCommand(string command, string args)
    {
        MainWindow.IsOpen = true;
    }

    private void DrawUI()
    {
        WindowSystem.Draw();
        DrawLines();
    }

    private void DrawConfigUI()
    {
        MainWindow.IsOpen = true;
    }

    private void OnTerritoryChanged(ushort territoryId)
    {
        notifiedPlayers.Clear();
        currentDetectedPlayers.Clear();
    }

    private void OnFrameworkUpdate(IFramework framework)
    {
        if (DateTime.Now - lastUpdateTime < updateInterval)
            return;

        lastUpdateTime = DateTime.Now;
        UpdateDetectedPlayers();
    }

    private unsafe void UpdateDetectedPlayers()
    {
        if (ObjectTable.GetObjectAddress(0) == nint.Zero)
            return;

        var localPlayer = ObjectTable.FirstOrDefault(x => x is IPlayerCharacter) as IPlayerCharacter;
        if (localPlayer == null)
            return;

        var myPos = localPlayer.Position;
        var checkRange = Configuration.DetectionRadius * Configuration.DetectionRadius;

        currentDetectedPlayers.Clear();

        foreach (var obj in ObjectTable)
        {
            if (obj.ObjectKind != ObjectKind.Player)
                continue;

            if (obj is not IPlayerCharacter player)
                continue;

            if (obj.Address == localPlayer.Address)
                continue;

            if (Vector3.DistanceSquared(myPos, obj.Position) > checkRange)
                continue;

            var chara = (Character*)player.Address;
            if (chara == null)
                continue;

            var contentId = chara->ContentId;
            var accountId = chara->AccountId;
            var playerName = player.Name.TextValue;
            var playerWorld = player.HomeWorld.Value.Name.ToString();

            var blacklisted = Configuration.Blacklist.FirstOrDefault(w =>
                (w.ContentId != 0 && w.ContentId == contentId) ||
                (w.AccountId != 0 && w.AccountId == accountId) ||
                (w.ContentId == 0 && w.AccountId == 0 &&
                 w.Name.Equals(playerName, StringComparison.OrdinalIgnoreCase) &&
                 w.WorldName.Equals(playerWorld, StringComparison.OrdinalIgnoreCase)));

            var whitelisted = Configuration.Whitelist.FirstOrDefault(w =>
                (w.ContentId != 0 && w.ContentId == contentId) ||
                (w.AccountId != 0 && w.AccountId == accountId) ||
                (w.ContentId == 0 && w.AccountId == 0 &&
                 w.Name.Equals(playerName, StringComparison.OrdinalIgnoreCase) &&
                 w.WorldName.Equals(playerWorld, StringComparison.OrdinalIgnoreCase)));

            if (blacklisted != null)
            {
                currentDetectedPlayers.Add(new DetectedPlayer
                {
                    Character = player,
                    WatchedInfo = blacklisted,
                    IsBlacklisted = true
                });

                UpdateLastSeen(blacklisted, player);
                TrySendNotification(player, blacklisted, true);
            }
            else if (whitelisted != null)
            {
                currentDetectedPlayers.Add(new DetectedPlayer
                {
                    Character = player,
                    WatchedInfo = whitelisted,
                    IsBlacklisted = false
                });

                UpdateLastSeen(whitelisted, player);
                TrySendNotification(player, whitelisted, false);
            }
        }
    }

    private unsafe void UpdateLastSeen(WatchedPlayer watched, IPlayerCharacter player)
    {
        watched.LastSeenTime = DateTime.Now;

        var territory = DataManager.GetExcelSheet<TerritoryType>()?.GetRowOrDefault(ClientState.TerritoryType);
        if (territory != null)
        {
            watched.LastSeenLocation = territory.Value.PlaceName.ValueNullable?.Name.ToString() ?? "未知";
        }

        // 如果是手動新增的玩家，更新 ContentId 和 AccountId
        if (watched.ContentId == 0 && watched.AccountId == 0)
        {
            var chara = (Character*)player.Address;
            if (chara != null)
            {
                watched.ContentId = chara->ContentId;
                watched.AccountId = chara->AccountId;
                Configuration.Save();
            }
        }
    }

    private void TrySendNotification(IPlayerCharacter player, WatchedPlayer watched, bool isBlacklisted)
    {
        // 使用 ContentId、AccountId 或名字的 hash 作為唯一識別
        var key = watched.ContentId != 0 ? watched.ContentId :
                  watched.AccountId != 0 ? watched.AccountId :
                  (ulong)$"{watched.Name}@{watched.WorldName}".GetHashCode();

        if (notifiedPlayers.Contains(key))
            return;

        notifiedPlayers.Add(key);

        var listType = isBlacklisted ? "黑名單" : "白名單";
        var message = $"[PlayerWatchlist] 偵測到{listType}玩家: {player.Name}@{player.HomeWorld.Value.Name}";

        if (!string.IsNullOrEmpty(watched.Note))
        {
            message += $"\n備註: {watched.Note}";
        }

        if (Configuration.SendChatNotification)
        {
            ChatGui.Print(new XivChatEntry
            {
                Message = message,
                Type = isBlacklisted ? XivChatType.Urgent : XivChatType.SystemMessage
            });
        }

        if (Configuration.SendSystemNotification)
        {
            NotificationManager.AddNotification(new Dalamud.Interface.ImGuiNotification.Notification
            {
                Title = "PlayerWatchlist",
                Content = message,
                Type = isBlacklisted
                    ? Dalamud.Interface.ImGuiNotification.NotificationType.Warning
                    : Dalamud.Interface.ImGuiNotification.NotificationType.Info,
                Minimized = false,
            });
        }
    }

    private void DrawLines()
    {
        if (!Configuration.ShowLines || currentDetectedPlayers.Count == 0)
            return;

        var localPlayer = ObjectTable.FirstOrDefault(x => x is IPlayerCharacter) as IPlayerCharacter;
        if (localPlayer == null)
            return;

        if (!GameGui.WorldToScreen(localPlayer.Position, out var localScreenPos))
            return;

        var drawList = ImGui.GetForegroundDrawList();

        foreach (var detected in currentDetectedPlayers)
        {
            if (!GameGui.WorldToScreen(detected.Character.Position, out var targetScreenPos))
                continue;

            var lineColor = detected.IsBlacklisted
                ? Configuration.BlacklistLineColor
                : Configuration.WhitelistLineColor;

            drawList.AddLine(localScreenPos, targetScreenPos, lineColor, Configuration.LineThickness);
            drawList.AddCircleFilled(localScreenPos, 8f, lineColor);
            drawList.AddCircleFilled(targetScreenPos, 8f, lineColor);

            var labelPos = targetScreenPos + new Vector2(10, -20);
            var label = $"{detected.Character.Name}";
            if (!string.IsNullOrEmpty(detected.WatchedInfo.Note))
            {
                label += $" ({detected.WatchedInfo.Note})";
            }

            drawList.AddText(labelPos, lineColor, label);
        }
    }

    private unsafe void OnMenuOpened(IMenuOpenedArgs args)
    {
        if (args.Target is not MenuTargetDefault target)
            return;

        if (target.TargetContentId == 0)
            return;

        var targetPlayer = ObjectTable.FirstOrDefault(x =>
            x is IPlayerCharacter p && ((Character*)p.Address)->ContentId == target.TargetContentId) as IPlayerCharacter;

        if (targetPlayer == null)
            return;

        var chara = (Character*)targetPlayer.Address;
        var contentId = chara->ContentId;
        var accountId = chara->AccountId;

        var isInBlacklist = Configuration.Blacklist.Any(w =>
            w.ContentId == contentId || w.AccountId == accountId);

        var isInWhitelist = Configuration.Whitelist.Any(w =>
            w.ContentId == contentId || w.AccountId == accountId);

        args.AddMenuItem(new MenuItem
        {
            Name = isInBlacklist ? "從黑名單移除" : "添加到黑名單",
            OnClicked = _ => ToggleBlacklist(targetPlayer, contentId, accountId, isInBlacklist)
        });

        args.AddMenuItem(new MenuItem
        {
            Name = isInWhitelist ? "從白名單移除" : "添加到白名單",
            OnClicked = _ => ToggleWhitelist(targetPlayer, contentId, accountId, isInWhitelist)
        });
    }

    private void ToggleBlacklist(IPlayerCharacter player, ulong contentId, ulong accountId, bool isInBlacklist)
    {
        if (isInBlacklist)
        {
            Configuration.Blacklist.RemoveAll(w =>
                w.ContentId == contentId || w.AccountId == accountId);

            ChatGui.Print($"[PlayerWatchlist] 已將 {player.Name} 從黑名單移除");
        }
        else
        {
            Configuration.Whitelist.RemoveAll(w =>
                w.ContentId == contentId || w.AccountId == accountId);

            Configuration.Blacklist.Add(new WatchedPlayer(player));
            ChatGui.Print($"[PlayerWatchlist] 已將 {player.Name} 添加到黑名單");
        }

        Configuration.Save();
    }

    private void ToggleWhitelist(IPlayerCharacter player, ulong contentId, ulong accountId, bool isInWhitelist)
    {
        if (isInWhitelist)
        {
            Configuration.Whitelist.RemoveAll(w =>
                w.ContentId == contentId || w.AccountId == accountId);

            ChatGui.Print($"[PlayerWatchlist] 已將 {player.Name} 從白名單移除");
        }
        else
        {
            Configuration.Blacklist.RemoveAll(w =>
                w.ContentId == contentId || w.AccountId == accountId);

            Configuration.Whitelist.Add(new WatchedPlayer(player));
            ChatGui.Print($"[PlayerWatchlist] 已將 {player.Name} 添加到白名單");
        }

        Configuration.Save();
    }

    public void AddToBlacklist(WatchedPlayer player)
    {
        if (!Configuration.Blacklist.Contains(player))
        {
            Configuration.Whitelist.RemoveAll(w =>
                w.ContentId == player.ContentId || w.AccountId == player.AccountId);

            Configuration.Blacklist.Add(player);
            Configuration.Save();
        }
    }

    public void AddToWhitelist(WatchedPlayer player)
    {
        if (!Configuration.Whitelist.Contains(player))
        {
            Configuration.Blacklist.RemoveAll(w =>
                w.ContentId == player.ContentId || w.AccountId == player.AccountId);

            Configuration.Whitelist.Add(player);
            Configuration.Save();
        }
    }

    public void RemoveFromBlacklist(WatchedPlayer player)
    {
        Configuration.Blacklist.Remove(player);
        Configuration.Save();
    }

    public void RemoveFromWhitelist(WatchedPlayer player)
    {
        Configuration.Whitelist.Remove(player);
        Configuration.Save();
    }
}
