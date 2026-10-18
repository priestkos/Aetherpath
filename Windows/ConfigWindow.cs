using System;
using System.Numerics;

using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;

using Aetherpath.Core;

namespace Aetherpath.Windows;

public sealed class ConfigWindow : Window, IDisposable
{
    private readonly Plugin plugin;

    public ConfigWindow(Plugin plugin)
        : base("Aetherpath settings###AetherpathConfigWindow")
    {
        this.plugin = plugin;
        this.Size = new Vector2(520, 560);
        this.SizeCondition = ImGuiCond.FirstUseEver;
    }

    public void Dispose()
    {
    }

    public override void Draw()
    {
        var config = this.plugin.Configuration;

        if (ImGui.CollapsingHeader("My travel slots", ImGuiTreeNodeFlags.DefaultOpen))
        {
            ImGui.TextWrapped("Aetherpath reads your free transfer and favored destinations from the game. Type values here to override them and price what-if setups - nothing here is written back to the game.");
            ImGuiHelpers.ScaledDummy(4f);

            var auto = config.AutoReadSlots;
            if (ImGui.Checkbox("Read slots from the game##ap_auto", ref auto))
            {
                config.AutoReadSlots = auto;
                config.Save();
            }

            ImGuiHelpers.ScaledDummy(4f);
            ImGui.TextUnformatted("Override: free destination");
            ImGui.SameLine();
            ImGui.TextColored(new Vector4(0.65f, 0.65f, 0.70f, 1f), "(aetheryte id, 0 = none)");
            var free = (int)config.OverrideFreeDestination;
            ImGui.SetNextItemWidth(140 * ImGuiHelpers.GlobalScale);
            if (ImGui.InputInt("##ap_free_override", ref free))
            {
                config.OverrideFreeDestination = (uint)Math.Max(0, free);
                config.Save();
            }

            ImGui.TextUnformatted("Override: home point (Return)");
            var home = (int)config.OverrideHomePoint;
            ImGui.SetNextItemWidth(140 * ImGuiHelpers.GlobalScale);
            if (ImGui.InputInt("##ap_home_override", ref home))
            {
                config.OverrideHomePoint = (uint)Math.Max(0, home);
                config.Save();
            }

            ImGui.TextUnformatted("Override: favored destinations (ids)");
            ImGui.SameLine();
            ImGui.TextColored(new Vector4(0.65f, 0.65f, 0.70f, 1f), "(4th slot needs the Companion app unlock)");
            for (var i = 0; i < SlotState.FavoredSlotCount; i++)
            {
                while (config.OverrideFavored.Count <= i)
                    config.OverrideFavored.Add(0);

                var value = (int)config.OverrideFavored[i];
                ImGui.SetNextItemWidth(140 * ImGuiHelpers.GlobalScale);
                if (ImGui.InputInt($"##ap_fav_override{i}", ref value))
                {
                    config.OverrideFavored[i] = (uint)Math.Max(0, value);
                    config.Save();
                }

                if (i < SlotState.FavoredSlotCount - 1)
                    ImGui.SameLine();
            }
        }

        if (ImGui.CollapsingHeader("Travel options to price", ImGuiTreeNodeFlags.DefaultOpen))
        {
            Check("Plain teleport", config.ConsiderDirect, v => config.ConsiderDirect = v);
            Check("Free destination", config.ConsiderFreeSlot, v => config.ConsiderFreeSlot = v);
            Check("Favored slots", config.ConsiderFavoredSlots, v => config.ConsiderFavoredSlots = v);
            Check("Return to home point", config.ConsiderReturn, v => config.ConsiderReturn = v);
            Check("Aetheryte tickets", config.ConsiderTickets, v => config.ConsiderTickets = v);
            Check("Estate / apartment teleports", config.ConsiderEstates, v => config.ConsiderEstates = v);
            Check("Teleport to a neighbour, then walk in", config.ConsiderMultiLeg, v => config.ConsiderMultiLeg = v);

            ImGuiHelpers.ScaledDummy(4f);
            ImGui.TextUnformatted("Aetheryte tickets in your bags");
            ImGui.SameLine();
            ImGui.TextColored(new Vector4(0.65f, 0.65f, 0.70f, 1f), "(manual - Aetherpath does not read your inventory)");
            var tickets = config.AetheryteTickets;
            ImGui.SetNextItemWidth(140 * ImGuiHelpers.GlobalScale);
            if (ImGui.InputInt("##ap_tickets", ref tickets))
            {
                config.AetheryteTickets = Math.Max(0, tickets);
                config.Save();
            }
        }

        if (ImGui.CollapsingHeader("Display"))
        {
            Check("Show diagnostics counters", config.ShowDiagnostics, v => config.ShowDiagnostics = v);
            Check("Hide routes costing 1000 gil or more", config.HideTravelAbove999, v => config.HideTravelAbove999 = v);
            Check("Include walk-in zones (no local aetheryte)", config.IncludeWalkInDestinations, v => config.IncludeWalkInDestinations = v);
            Check("Compact number formatting", config.CompactNumbers, v => config.CompactNumbers = v);

            ImGui.TextUnformatted("Routes shown");
            var max = config.MaxRoutesShown;
            ImGui.SetNextItemWidth(140 * ImGuiHelpers.GlobalScale);
            if (ImGui.SliderInt("##ap_max", ref max, 3, 20))
            {
                config.MaxRoutesShown = max;
                config.Save();
            }
        }

        ImGuiHelpers.ScaledDummy(6f);
        ImGui.Separator();
        ImGui.TextWrapped("Read-only by design: Aetherpath never teleports, never spends a ticket and never writes to game memory. It reads the teleport window the game already built for you.");
        ImGui.TextWrapped($"Config v{config.Version} · live teleport entries: {this.plugin.CostModel.Entries.Count}");
    }

    private void Check(string label, bool current, Action<bool> apply)
    {
        var value = current;
        if (ImGui.Checkbox($"{label}##ap_opt_{label}", ref value))
        {
            apply(value);
            this.plugin.Configuration.Save();
        }
    }
}