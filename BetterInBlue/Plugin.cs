using System;
using System.Collections.Generic;
using Dalamud.Game.Command;
using Dalamud.Plugin;
using BetterInBlue.Windows;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Internal;
using Dalamud.Interface.Windowing;
using ImGuiNET;
using Lumina.Excel;
using Lumina.Excel.Sheets;
using Action = Lumina.Excel.Sheets.Action;
using System.Linq;
using Dalamud.IoC;
using Dalamud.Plugin.Services;
using Dalamud.Interface.Textures.TextureWraps;

namespace BetterInBlue;

public sealed class Plugin : IDalamudPlugin {
    public string Name => "Better in Blue";
    private const string CommandName = "/pblue";

    public WindowSystem WindowSystem = new("BetterInBlue");
    public static Configuration Configuration = null!;
    public MainWindow MainWindow;
    public ConfigWindow ConfigWindow;

    public static ExcelSheet<Action> Action = null!;
    public static ExcelSheet<AozAction> AozAction = null!;
    public static ExcelSheet<AozActionTransient> AozActionTransient = null!;

    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ITextureProvider TextureProvider { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;
    [PluginService] internal static IDataManager DataManager { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;
    public Plugin() {

        PluginInterface.Create<Services>();

        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        this.MainWindow = new MainWindow(this);
        this.ConfigWindow = new ConfigWindow(this);

        WindowSystem.AddWindow(MainWindow);
        WindowSystem.AddWindow(ConfigWindow);

        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommandInternal) {
            HelpMessage = "Opens the main menu."
        });

        PluginInterface.UiBuilder.Draw += this.DrawUi;
        PluginInterface.UiBuilder.OpenConfigUi += this.OpenConfigUi;

        Action = DataManager.GetExcelSheet<Action>()!;
        AozAction = DataManager.GetExcelSheet<AozAction>()!;
        AozActionTransient = DataManager.GetExcelSheet<AozActionTransient>()!;
    }
 
    public IDalamudTextureWrap GetIcon(uint id) {
        if (id == 0) {
            return (IDalamudTextureWrap) TextureProvider.GetFromFile("ui/uld/DragTargetA_hr1.tex");
        }

        var row = AozAction.GetRow(id)!;
        var transient = AozActionTransient.GetRow(row.RowId)!;
        var icon = TextureProvider.GetFromGameIcon(transient.Icon)!;
        return (IDalamudTextureWrap) icon;
    }

    public void Dispose() {
        this.WindowSystem.RemoveAllWindows();
        this.MainWindow.Dispose();
        this.ConfigWindow.Dispose();

        Services.CommandManager.RemoveHandler(CommandName);
    }

    private void OnCommandInternal(string _, string args) {
        args = args.ToLower();
        OnCommand(args.Split(' ').ToList());
    }

    private void OnCommand(List<string> args) {
        switch (args[0]) {
            case "config":
            case "settings":
                this.OpenConfigUi();
                break;
            case "apply":
            case "load":
                ApplyLoadoutByName(args.Skip(1).ToList());
                break;

            default:
                this.MainWindow.IsOpen = true;
                break;
        }
    }

    private static void ApplyLoadoutByName(List<string> args) {
        var name = string.Join(" ", args).Trim();
        foreach (var loadout in Configuration.Loadouts) {
            if (loadout.Name.ToLower() == name)
                loadout.Apply();
        }
    }

    private void DrawUi() {
        this.WindowSystem.Draw();
    }

    public void OpenConfigUi() {
        this.ConfigWindow.IsOpen = true;
    }

    public static uint AozToNormal(uint id) {
        if (id == 0) return 0;
        //return AozAction.GetRow(id)!.Action.Row;
        return AozAction.GetRow(id)!.RowId;
    }

    public static uint NormalToAoz(uint id) {
        foreach (var aozAction in AozAction) {
            if (aozAction.RowId == id) return aozAction.RowId;
        }

        throw new Exception("https://tenor.com/view/8032213");
    }

    // https://github.com/Ottermandias/OtterGui/blob/03b6b17fee66488fff7f598e444fa99454098767/Util.cs#L263
    public static bool DisabledButtonWithTooltip(
        FontAwesomeIcon icon,
        bool disabled,
        string enabledText = "",
        string disabledText = ""
    ) {
        if (disabled) ImGui.PushStyleVar(ImGuiStyleVar.Alpha, 0.5f);
        var ret = ImGuiComponents.IconButton(icon);
        if (disabled) ImGui.PopStyleVar();

        var str = disabled ? disabledText : enabledText;
        if (!string.IsNullOrEmpty(str) && ImGui.IsItemHovered()) ImGui.SetTooltip(str);

        return ret && !disabled;
    }
}
