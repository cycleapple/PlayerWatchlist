#nullable enable
using System;
using System.Numerics;
using Dalamud.Interface.Windowing;
using Dalamud.Bindings.ImGui;
using PlayerWatchlist.Models;

namespace PlayerWatchlist.Windows;

public class MainWindow : Window, IDisposable
{
    private readonly Plugin plugin;
    private string newPlayerName = string.Empty;
    private int selectedWorldIndex = 0;
    private string newPlayerNote = string.Empty;

    private static readonly string[] WorldList = new[]
    {
        "伊弗利特",
        "利維坦",
        "奧丁",
        "巴哈姆特",
        "泰坦",
        "迦樓羅",
        "鳳凰"
    };

    public MainWindow(Plugin plugin) : base(
        "PlayerWatchlist 設定",
        ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse)
    {
        this.plugin = plugin;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(500, 400),
            MaximumSize = new Vector2(800, 600)
        };
    }

    public void Dispose() { }

    public override void Draw()
    {
        if (ImGui.BeginTabBar("PlayerWatchlistTabs"))
        {
            if (ImGui.BeginTabItem("黑名單"))
            {
                DrawBlacklistTab();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("白名單"))
            {
                DrawWhitelistTab();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("設定"))
            {
                DrawSettingsTab();
                ImGui.EndTabItem();
            }

            ImGui.EndTabBar();
        }
    }

    private void DrawBlacklistTab()
    {
        DrawAddPlayerSection(true);

        ImGui.Separator();
        ImGui.Text($"黑名單玩家數: {plugin.Configuration.Blacklist.Count}");
        ImGui.Separator();

        if (ImGui.BeginChild("BlacklistChild", new Vector2(-1, -1), true))
        {
            DrawPlayerList(plugin.Configuration.Blacklist, true);
        }
        ImGui.EndChild();
    }

    private void DrawWhitelistTab()
    {
        DrawAddPlayerSection(false);

        ImGui.Separator();
        ImGui.Text($"白名單玩家數: {plugin.Configuration.Whitelist.Count}");
        ImGui.Separator();

        if (ImGui.BeginChild("WhitelistChild", new Vector2(-1, -1), true))
        {
            DrawPlayerList(plugin.Configuration.Whitelist, false);
        }
        ImGui.EndChild();
    }

    private void DrawAddPlayerSection(bool isBlacklist)
    {
        ImGui.Text("手動添加玩家:");

        ImGui.Text("玩家");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(120);
        ImGui.InputText("##PlayerName", ref newPlayerName, 64);

        ImGui.SameLine();
        ImGui.Text("伺服器");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(80);
        ImGui.Combo("##ServerName", ref selectedWorldIndex, WorldList, WorldList.Length);

        ImGui.SameLine();
        ImGui.Text("備註");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(120);
        ImGui.InputText("##Note", ref newPlayerNote, 256);

        ImGui.SameLine();
        if (ImGui.Button(isBlacklist ? "添加到黑名單" : "添加到白名單"))
        {
            if (!string.IsNullOrWhiteSpace(newPlayerName))
            {
                var newPlayer = new WatchedPlayer
                {
                    Name = newPlayerName.Trim(),
                    WorldName = WorldList[selectedWorldIndex],
                    Note = newPlayerNote.Trim(),
                    AddedTime = DateTime.Now
                };

                if (isBlacklist)
                    plugin.AddToBlacklist(newPlayer);
                else
                    plugin.AddToWhitelist(newPlayer);

                newPlayerName = string.Empty;
                newPlayerNote = string.Empty;
            }
        }
    }

    private void DrawPlayerList(System.Collections.Generic.List<WatchedPlayer> players, bool isBlacklist)
    {
        if (ImGui.BeginTable("PlayerTable", 5, ImGuiTableFlags.Borders | ImGuiTableFlags.Resizable | ImGuiTableFlags.RowBg))
        {
            ImGui.TableSetupColumn("玩家名稱", ImGuiTableColumnFlags.WidthStretch, 20);
            ImGui.TableSetupColumn("伺服器", ImGuiTableColumnFlags.WidthStretch, 15);
            ImGui.TableSetupColumn("備註", ImGuiTableColumnFlags.WidthStretch, 25);
            ImGui.TableSetupColumn("最後見到", ImGuiTableColumnFlags.WidthStretch, 20);
            ImGui.TableSetupColumn("操作", ImGuiTableColumnFlags.WidthFixed, 80);
            ImGui.TableHeadersRow();

            WatchedPlayer? toRemove = null;
            WatchedPlayer? toMove = null;

            for (int i = 0; i < players.Count; i++)
            {
                var player = players[i];
                ImGui.TableNextRow();

                ImGui.TableNextColumn();
                ImGui.Text(player.Name);

                ImGui.TableNextColumn();
                ImGui.Text(player.WorldName);

                ImGui.TableNextColumn();
                var note = player.Note;
                ImGui.SetNextItemWidth(-1);
                if (ImGui.InputText($"##Note{i}", ref note, 256))
                {
                    player.Note = note;
                }
                if (ImGui.IsItemDeactivatedAfterEdit())
                {
                    plugin.Configuration.Save();
                }

                ImGui.TableNextColumn();
                if (player.LastSeenTime != DateTime.MinValue)
                {
                    ImGui.Text($"{player.LastSeenTime:MM/dd HH:mm}");
                    if (!string.IsNullOrEmpty(player.LastSeenLocation))
                    {
                        ImGui.SameLine();
                        ImGui.TextDisabled($"({player.LastSeenLocation})");
                    }
                }
                else
                {
                    ImGui.TextDisabled("尚未見到");
                }

                ImGui.TableNextColumn();
                if (ImGui.Button($"移除##{i}"))
                {
                    toRemove = player;
                }

                ImGui.SameLine();
                if (ImGui.Button(isBlacklist ? $"移至白##{i}" : $"移至黑##{i}"))
                {
                    toMove = player;
                }
            }

            ImGui.EndTable();

            if (toRemove != null)
            {
                if (isBlacklist)
                    plugin.RemoveFromBlacklist(toRemove);
                else
                    plugin.RemoveFromWhitelist(toRemove);
            }

            if (toMove != null)
            {
                if (isBlacklist)
                {
                    plugin.RemoveFromBlacklist(toMove);
                    plugin.AddToWhitelist(toMove);
                }
                else
                {
                    plugin.RemoveFromWhitelist(toMove);
                    plugin.AddToBlacklist(toMove);
                }
            }
        }
    }

    private void DrawSettingsTab()
    {
        var config = plugin.Configuration;
        var changed = false;

        ImGui.Text("偵測設定");
        ImGui.Separator();

        var radius = config.DetectionRadius;
        ImGui.SetNextItemWidth(150);
        if (ImGui.SliderInt("偵測半徑", ref radius, 1, 200))
        {
            config.DetectionRadius = radius;
            changed = true;
        }
        ImGui.SameLine();
        ImGui.TextDisabled("(單位: yalm)");

        ImGui.Spacing();
        ImGui.Text("顯示設定");
        ImGui.Separator();

        var showLines = config.ShowLines;
        if (ImGui.Checkbox("顯示連結線", ref showLines))
        {
            config.ShowLines = showLines;
            changed = true;
        }

        if (config.ShowLines)
        {
            var thickness = config.LineThickness;
            ImGui.SetNextItemWidth(150);
            if (ImGui.SliderFloat("線條粗細", ref thickness, 1f, 10f))
            {
                config.LineThickness = thickness;
                changed = true;
            }

            ImGui.Spacing();

            var blackColor = ImGui.ColorConvertU32ToFloat4(config.BlacklistLineColor);
            if (ImGui.ColorEdit4("黑名單線條顏色", ref blackColor, ImGuiColorEditFlags.NoInputs))
            {
                config.BlacklistLineColor = ImGui.ColorConvertFloat4ToU32(blackColor);
                changed = true;
            }

            var whiteColor = ImGui.ColorConvertU32ToFloat4(config.WhitelistLineColor);
            if (ImGui.ColorEdit4("白名單線條顏色", ref whiteColor, ImGuiColorEditFlags.NoInputs))
            {
                config.WhitelistLineColor = ImGui.ColorConvertFloat4ToU32(whiteColor);
                changed = true;
            }
        }

        ImGui.Spacing();
        ImGui.Text("通知設定");
        ImGui.Separator();

        var sendChat = config.SendChatNotification;
        if (ImGui.Checkbox("發送聊天通知", ref sendChat))
        {
            config.SendChatNotification = sendChat;
            changed = true;
        }

        var sendSystem = config.SendSystemNotification;
        if (ImGui.Checkbox("發送系統通知", ref sendSystem))
        {
            config.SendSystemNotification = sendSystem;
            changed = true;
        }

        if (changed)
        {
            config.Save();
        }
    }
}
