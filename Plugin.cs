using System;
using System.Collections.Generic;

using Dalamud.Game.ClientState.Aetherytes;
using Dalamud.Game.Command;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

using Aetherpath.Core;
using Aetherpath.Windows;

namespace Aetherpath;

/// <summary>
/// Aetherpath - teleport cost analyst. Read-only by design: this plugin never teleports,
/// never consumes a ticket and never writes to game memory.
/// </summary>
public sealed class Plugin : IDalamudPlugin
{
    public string Name => "Aetherpath";

    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;
    [PluginService] internal static IPlayerState PlayerState { get; private set; } = null!;
    [PluginService] internal static IDataManager DataManager { get; private set; } = null!;
    [PluginService] internal static IAetheryteList AetheryteList { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;

    internal Configuration Configuration { get; }

    internal readonly WindowSystem WindowSystem = new("Aetherpath");

    /// <summary>Static so the cost model and planner can reach sheet lookups without plumbing.</summary>
    internal static GameData GameData { get; private set; } = null!;

    internal CostModel CostModel { get; }
    internal RoutePlanner Planner { get; }

    private readonly MainWindow mainWindow;
    private readonly ConfigWindow configWindow;

    public Plugin()
    {
        this.Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

        GameData = new GameData();
        this.CostModel = new CostModel();
        this.Planner = new RoutePlanner();

        this.mainWindow = new MainWindow(this);
        this.configWindow = new ConfigWindow(this);

        this.WindowSystem.AddWindow(this.mainWindow);
        this.WindowSystem.AddWindow(this.configWindow);

        CommandManager.AddHandler("/aetherpath", new CommandInfo(this.OnCommand)
        {
            HelpMessage = "Open the Aetherpath teleport cost analyst. (/aetherpath cfg for settings)",
        });
        CommandManager.AddHandler("/ap", new CommandInfo(this.OnCommand)
        {
            HelpMessage = "Short alias for /aetherpath.",
        });

        PluginInterface.UiBuilder.Draw += this.WindowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi += this.ToggleConfigUi;
        PluginInterface.UiBuilder.OpenMainUi += this.ToggleMainUi;

        Log.Information($"[Aetherpath] loaded (read-only: no teleports, no settings writes). Config v{this.Configuration.Version}");
    }

    public void Dispose()
    {
        PluginInterface.UiBuilder.Draw -= this.WindowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi -= this.ToggleConfigUi;
        PluginInterface.UiBuilder.OpenMainUi -= this.ToggleMainUi;

        this.WindowSystem.RemoveAllWindows();
        this.mainWindow.Dispose();
        this.configWindow.Dispose();

        CommandManager.RemoveHandler("/aetherpath");
        CommandManager.RemoveHandler("/ap");
    }

    private void OnCommand(string command, string args)
    {
        var a = args.Trim().ToLowerInvariant();
        if (a is "cfg" or "config" or "settings")
        {
            this.ToggleConfigUi();
            return;
        }

        this.ToggleMainUi();
    }

    private void ToggleConfigUi() => this.configWindow.Toggle();

    private void ToggleMainUi() => this.mainWindow.Toggle();
}