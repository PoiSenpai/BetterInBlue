using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Interface;
using Dalamud.Interface.Components;
//using Dalamud.Interface.Internal.Notifications;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Logging;
using Dalamud.Plugin.Services;
using Dalamud.Plugin;
using Dalamud.Utility;
using FFXIVClientStructs.FFXIV.Client.Game;
using ImGuiNET;
using Dalamud.Interface.ImGuiNotification;

namespace BetterInBlue.Windows;

public class MainWindow : Window, IDisposable {
    private Plugin plugin;
    private Loadout? selectedLoadout;

    private int editing;
    private string searchFilter = string.Empty;
    private bool shouldOpen;
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ITextureProvider TextureProvider { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;
    [PluginService] internal static IDataManager DataManager { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;
    [PluginService] internal static INotificationManager NotificationManager { get; private set; } = null!;

    public MainWindow(Plugin plugin) : base("Better in Blue") {
        this.plugin = plugin;

        // Stolen from Namingway lol
        this.Size = new Vector2(450, 400);
        this.SizeCondition = ImGuiCond.FirstUseEver;
    }

    public void Dispose() { }

    public override void Draw() {
        var cra = ImGui.GetContentRegionAvail();
        var sidebar = cra with {X = cra.X * 0.25f};
        var editor = cra with {X = cra.X * 0.75f};

        this.DrawSidebar(sidebar);
        ImGui.SameLine();
        this.DrawEditor(editor);

        if (this.shouldOpen) {
            ImGui.OpenPopup("ActionContextMenu");
            this.shouldOpen = false;
        }

        this.DrawContextMenu();
    }

    private unsafe void DrawSidebar(Vector2 size) {
        if (ImGui.BeginChild("Sidebar", size, true)) {
            if (ImGuiComponents.IconButton(FontAwesomeIcon.Plus)) {
                Plugin.Configuration.Loadouts.Add(new Loadout());
                Plugin.Configuration.Save();
            }

            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Create a new loadout.");
            ImGui.SameLine();

            if (ImGuiComponents.IconButton(FontAwesomeIcon.Clipboard)) {
                var maybeLoadout = Loadout.FromPreset(ImGui.GetClipboardText());
                if (maybeLoadout != null) {
                    Plugin.Configuration.Loadouts.Add(maybeLoadout);
                    Plugin.Configuration.Save();
                } else {
                    Notification notification = new Notification();
                    notification.Title = "Better In Blue";
                    notification.Content = "Failed to load preset from clipboard.";
                    notification.Type = NotificationType.Error;
                    NotificationManager.AddNotification(notification);
                }
            }

            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Load a preset from the clipboard.");
            ImGui.SameLine();

            if (ImGuiComponents.IconButton(FontAwesomeIcon.FileImport)) {
                var loadout = new Loadout();
                for (var i = 0; i < 24; i++)
                    loadout.Actions.SetValue(
                        Plugin.NormalToAoz(ActionManager.Instance()->GetActiveBlueMageActionInSlot(i)), i);
                Plugin.Configuration.Loadouts.Add(loadout);
                Plugin.Configuration.Save();
            }

            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Create preset from current spell loadout.");
            ImGui.SameLine();

            if (ImGuiComponents.IconButton(FontAwesomeIcon.Cog)) {
                this.plugin.OpenConfigUi();
            }

            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Open the config window.");
            ImGui.Separator();

            foreach (var loadout in Plugin.Configuration.Loadouts) {
                var label = loadout.Name + "##" + loadout.GetHashCode();
                if (ImGui.Selectable(label, loadout == this.selectedLoadout)) {
                    this.selectedLoadout = loadout;
                }
            }

            ImGui.EndChild();
        }
    }

    private void DrawEditor(Vector2 size) {
        if (this.selectedLoadout == null) return;

        if (ImGui.BeginChild("Editor", size)) {
            var canApply = this.selectedLoadout.CanApply();
            if (Plugin.DisabledButtonWithTooltip(
                    FontAwesomeIcon.Play,
                    !canApply,
                    "Apply the current loadout.",
                    "Some conditions are not met to apply this loadout. You must meet all of the following conditions:\n"
                    + "- You must be a Blue Mage.\n"
                    + "- You must not be in combat.\n"
                    + "- You must have every action in the loadout unlocked.\n"
                    + "- Your loadout must not be invalid (e.g. two of the same action or invalid action IDs)."
                )) {
                var worked = this.selectedLoadout.Apply();
                if (!worked) {
                    Notification notification = new Notification();
                    notification.Title = "Better In Blue";
                    notification.Content = "Failed to apply loadout. :(\n"
                        + "You should have gotten an error message on screen explaining why. If not, please report this!";
                    notification.Type = NotificationType.Error;
                    NotificationManager.AddNotification(notification);
                }
            }

            ImGui.SameLine();

            var canDelete = ImGui.GetIO().KeyCtrl;
            if (Plugin.DisabledButtonWithTooltip(
                    FontAwesomeIcon.Trash,
                    !canDelete,
                    "",
                    "Delete this loadout - this can't be undone. Hold Ctrl to enable the delete button."
                )) {
                Plugin.Configuration.Loadouts.Remove(this.selectedLoadout);
                Plugin.Configuration.Save();

                this.selectedLoadout = null;
                ImGui.EndChild();
                return;
            }

            ImGui.SameLine();

            if (ImGuiComponents.IconButton(FontAwesomeIcon.FileExport)) {
                ImGui.SetClipboardText(this.selectedLoadout.ToPreset());
                Notification notification = new Notification();
                notification.Title = "Better In Blue";
                notification.Content = "Copied loadout to clipboard.\nConsider sharing it in #preset-sharing in the XIVLauncher & Dalamud Discord server!";
                notification.Type = NotificationType.Success;
                NotificationManager.AddNotification(notification);
            }

            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Copy this loadout as a preset to the clipboard.");

            // Can't ref the damn name, I hate getter/setters
            var name = this.selectedLoadout!.Name;
            if (ImGui.InputText("Name", ref name, 256)) {
                this.selectedLoadout.Name = name;
                Plugin.Configuration.Save();
            }

            ImGui.Separator();

            for (var i = 0; i < 12; i++) {
                this.DrawSquare(i);
                ImGui.SameLine();
            }

            ImGui.NewLine();

            for (var i = 12; i < 24; i++) {
                this.DrawSquare(i);
                ImGui.SameLine();
            }

            ImGui.EndChild();
        }
    }

    private void DrawSquare(int index) {
        var current = this.selectedLoadout!.Actions[index];
        var icon = this.plugin.GetIcon(current);

        ImGui.Image(icon.ImGuiHandle, new Vector2(48, 48));
        if (ImGui.IsItemHovered() && current != 0) {
            var action = Plugin.AozToNormal(current);
            var name = Plugin.Action.GetRow(action)!.Name.ToDalamudString().TextValue;
            ImGui.SetTooltip(name);
        }

        if (ImGui.IsItemClicked(ImGuiMouseButton.Left)) {
            this.editing = index;

            // Why does OpenPopup not work here? I dunno!
            this.shouldOpen = true;
        }

        if (ImGui.IsItemClicked(ImGuiMouseButton.Right)) {
            this.selectedLoadout!.Actions[index] = 0;
            Plugin.Configuration.Save();
        }
    }

    private void DrawContextMenu() {
        if (ImGui.BeginPopup("ActionContextMenu")) {
            ImGui.InputText("##Search", ref this.searchFilter, 256);

            if (ImGui.BeginChild("ActionList", new Vector2(256, 256))) {
                foreach (var listAction in Plugin.AozAction) {
                    if (listAction.RowId == 0) continue;

                    var listName = listAction.Action.Value!.Name.ToDalamudString().TextValue;
                    var listIcon = this.plugin.GetIcon(listAction.RowId);

                    var meetsSearchFilter = string.IsNullOrEmpty(this.searchFilter)
                                            || listName.ToLower().Contains(this.searchFilter.ToLower());
                    if (!meetsSearchFilter) continue;

                    var rowHeight = ImGui.GetTextLineHeightWithSpacing();

                    ImGui.Image(listIcon.ImGuiHandle, new Vector2(rowHeight, rowHeight));
                    ImGui.SameLine();

                    var tooManyOfAction = this.selectedLoadout!.ActionCount(listAction.RowId) > 0;
                    var notUnlocked = !this.selectedLoadout!.ActionUnlocked(listAction.RowId);

                    var locked = tooManyOfAction || notUnlocked;
                    var flags = locked
                                    ? ImGuiSelectableFlags.Disabled
                                    : ImGuiSelectableFlags.None;

                    if (ImGui.Selectable(listName, false, flags)) {
                        this.selectedLoadout!.Actions[this.editing] = listAction.RowId;
                        Plugin.Configuration.Save();
                        ImGui.CloseCurrentPopup();
                    }

                    // Can't hover a disabled Selectable, other UI element it is then
                    if (locked) {
                        ImGui.SameLine();
                        ImGui.TextUnformatted("(?)");
                        if (ImGui.IsItemHovered()) {
                            var str = "Issues:\n";

                            if (tooManyOfAction) {
                                str +=
                                    "- This loadout already has this action, so you can't add it twice. Remove the action from the loadout to add it again.\n";
                            }

                            if (notUnlocked) {
                                str += "- You haven't unlocked this action yet.";
                            }

                            ImGui.SetTooltip(str.Trim());
                        }
                    }
                }

                ImGui.EndChild();
            }

            ImGui.EndPopup();
        }
    }
}
