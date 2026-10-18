using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;

using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;

using Aetherpath.Core;

namespace Aetherpath.Windows;

public sealed class MainWindow : Window, IDisposable
{
    private static readonly Vector4 Accent = new(0.55f, 0.82f, 1.00f, 1.00f);
    private static readonly Vector4 AccentSoft = new(0.55f, 0.82f, 1.00f, 0.65f);
    private static readonly Vector4 Gold = new(0.98f, 0.85f, 0.45f, 1.00f);
    private static readonly Vector4 Good = new(0.55f, 0.90f, 0.60f, 1.00f);
    private static readonly Vector4 Bad = new(0.95f, 0.55f, 0.55f, 1.00f);
    private static readonly Vector4 Muted = new(0.65f, 0.65f, 0.70f, 1.00f);

    private readonly Plugin plugin;

    private uint? selectedAetheryteId;
    private uint? selectedTerritoryId;
    private string selectedLabel = string.Empty;

    private string search = string.Empty;
    private bool showZones = true;

    private string cacheKey = string.Empty;
    private List<TravelOption> cachedPlan = new();
    private SlotAdvice cachedAdvice = new();
    private SlotState cachedSlots = new();

    public MainWindow(Plugin plugin)
        : base("Aetherpath###AetherpathMainWindow", ImGuiWindowFlags.NoCollapse)
    {
        this.plugin = plugin;

        this.Size = new Vector2(860, 620);
        this.SizeCondition = ImGuiCond.FirstUseEver;
        this.SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(560, 380),
            MaximumSize = new Vector2(4000, 4000),
        };
    }

    public void Dispose()
    {
    }

    /// <summary>Push a text colour only when we actually want one - a null keeps the theme colour.</summary>
    private static void PushText(Vector4? color)
    {
        if (color is { } c)
            ImGui.PushStyleColor(ImGuiCol.Text, c);
    }

    private static void PopText(Vector4? color)
    {
        if (color != null)
            ImGui.PopStyleColor();
    }

    public override void Draw()
    {
        var config = this.plugin.Configuration;
        Plugin.GameData.EnsureLoaded();
        var model = this.plugin.CostModel;
        model.Refresh();

        var slots = SlotState.Resolve(config, model);
        var request = this.BuildRequest();

        var key = string.Create(
            CultureInfo.InvariantCulture,
            $"{model.RefreshCount}|{this.selectedAetheryteId}|{this.selectedTerritoryId}|{config.AetheryteTickets}|{config.AutoReadSlots}|{slots.IsLive}|{slots.FreeDestinationId}|{slots.HomePointId}|{string.Join(',', slots.Favored)}|{config.MaxRoutesShown}|{config.HideTravelAbove999}|{config.IncludeWalkInDestinations}|{config.CompactNumbers}|{config.ConsiderMultiLeg}{config.ConsiderReturn}{config.ConsiderTickets}{config.ConsiderEstates}{config.ConsiderFreeSlot}{config.ConsiderFavoredSlots}{config.ConsiderDirect}");

        if (key != this.cacheKey)
        {
            this.cacheKey = key;
            this.cachedSlots = slots;
            this.cachedPlan = this.plugin.Planner.Plan(config, model, slots, request);
            this.cachedAdvice = SlotOptimizer.Analyse(model, slots, config, request);
        }

        this.DrawHeader(model, slots, config);

        if (!model.HasLiveData)
        {
            ImGuiHelpers.ScaledDummy(6f);
            ImGui.TextColored(Gold, "No teleport data yet - log in to a character and attune to an aetheryte.");
            ImGui.TextColored(Muted, "Aetherpath reads the game's own teleport window, so it needs a character in the world.");
            return;
        }

        if (ImGui.BeginTable("aetherpath_layout", 2, ImGuiTableFlags.Resizable | ImGuiTableFlags.BordersInnerV))
        {
            ImGui.TableSetupColumn("##dest", ImGuiTableColumnFlags.WidthStretch, 0.34f);
            ImGui.TableSetupColumn("##routes", ImGuiTableColumnFlags.WidthStretch, 0.66f);

            ImGui.TableNextRow();

            ImGui.TableSetColumnIndex(0);
            this.DrawDestinationPicker(model, config);

            ImGui.TableSetColumnIndex(1);
            this.DrawRoutes(model, config, this.cachedSlots, this.cachedPlan, this.cachedAdvice);

            ImGui.EndTable();
        }

        ImGuiHelpers.ScaledDummy(4f);
        ImGui.Separator();
        ImGui.TextColored(Muted, "Aetherpath never teleports and never changes your settings - it only does the maths.");
        if (config.ShowDiagnostics)
        {
            ImGui.SameLine();
            ImGui.TextColored(AccentSoft, "|");
            ImGui.SameLine();
            ImGui.TextColored(Muted, $"live entries: {model.Entries.Count}, exact: {model.ExactCount}, projected: {model.EstimatedCount}, unpriced: {model.UnknownCount}");
        }
    }

    private PlanRequest BuildRequest() => new()
    {
        AetheryteId = this.selectedAetheryteId,
        TerritoryId = this.selectedTerritoryId,
        Label = this.selectedLabel,
    };

    private void DrawHeader(CostModel model, SlotState slots, Configuration config)
    {
        var where = Plugin.ClientState.TerritoryType != 0
            ? Plugin.GameData.TerritoryName(Plugin.ClientState.TerritoryType)
            : "unknown";

        ImGui.TextColored(Accent, "AETHER");
        ImGui.SameLine(0, 0);
        ImGui.TextColored(Gold, "PATH");

        ImGui.SameLine();
        ImGui.TextColored(Muted, $"| you are in {where}");

        ImGui.SameLine();
        var freeLabel = slots.FreeDestinationId is { } f && model.Get(f) is { } fe ? fe.Label : "not set";
        ImGui.TextColored(Muted, $"| free transfer: ");
        ImGui.SameLine(0, 0);
        ImGui.TextColored(slots.FreeDestinationId != null ? Good : Bad, freeLabel);

        var favoredLabels = slots.Favored
            .Where(id => id != 0)
            .Select(id => model.Get(id)?.Label ?? $"#{id}")
            .ToList();
        ImGui.TextColored(Muted, "favored: ");
        ImGui.SameLine(0, 0);
        ImGui.TextColored(favoredLabels.Count > 0 ? AccentSoft : Bad, favoredLabels.Count > 0 ? string.Join(" · ", favoredLabels) : "none");

        if (!slots.IsLive)
        {
            ImGui.SameLine();
            ImGui.TextColored(Gold, "[manual override active - prices are projections from here]");
        }

        if (slots.FreeDestinationId == null)
        {
            ImGui.TextColored(Muted, "No free destination detected. If you have one (it comes from registering a one-time-password authenticator), type its aetheryte id in ");
            ImGui.SameLine(0, 0);
            ImGui.TextColored(AccentSoft, "/aetherpath cfg");
            ImGui.SameLine(0, 0);
            ImGui.TextColored(Muted, " to price routes around it.");
        }

        ImGui.Separator();
    }

    private void DrawDestinationPicker(CostModel model, Configuration config)
    {
        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##ap_search", "Search aetheryte or zone...", ref this.search, 128);

        ImGui.Checkbox("Zones##ap_zones", ref this.showZones);
        ImGui.SameLine();
        if (ImGui.SmallButton("Clear##ap_clear"))
        {
            this.selectedAetheryteId = null;
            this.selectedTerritoryId = null;
            this.selectedLabel = string.Empty;
        }

        using var child = ImRaii.Child("ap_dest_list", new Vector2(0, 0), true);
        if (!child.Success)
            return;

        var filter = this.search.Trim();

        if (this.showZones)
        {
            var zones = Plugin.GameData.AllZones
                .Where(z => filter.Length == 0 || z.Label.Contains(filter, StringComparison.OrdinalIgnoreCase))
                .OrderBy(z => z.Label, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (zones.Count > 0)
            {
                ImGui.TextColored(AccentSoft, "ZONES");
                foreach (var zone in zones)
                {
                    var priced = model.Entries
                        .Where(e => e.TerritoryId == zone.TerritoryId && !e.IsEstate)
                        .ToList();

                    if (priced.Count == 0)
                    {
                        if (!config.IncludeWalkInDestinations)
                            continue;

                        // Only list walk-in zones that have at least one unlocked neighbour in the same PlaceNameZone.
                        var zoneName = Plugin.GameData.ZoneName(zone.TerritoryId);
                        var neighbourIds = SlotLogic.WalkInCandidateIds(
                            zone.TerritoryId,
                            zoneName,
                            Plugin.GameData.AllAetherytes.Select(a => (a.Id, a.TerritoryId, a.ZoneName, a.IsAetheryte, a.Invisible)));
                        if (!model.Entries.Any(e => neighbourIds.Contains(e.AetheryteId) && !e.IsEstate))
                            continue;
                    }

                    var label = $"{zone.Label}##zone{zone.TerritoryId}";
                    var selected = this.selectedTerritoryId == zone.TerritoryId;

                    ImGui.PushStyleColor(ImGuiCol.Text, selected ? Gold : Muted);
                    if (ImGui.Selectable(label, selected))
                    {
                        this.selectedTerritoryId = zone.TerritoryId;
                        this.selectedAetheryteId = null;
                        this.selectedLabel = zone.Label;
                    }

                    ImGui.PopStyleColor();

                    ImGui.SameLine();
                    if (priced.Count == 0)
                        ImGui.TextColored(Muted, "walk-in");
                    else
                        this.DrawCostTag(config, priced.Min(e => e.LiveCost), priced.Count > 1 ? $"{priced.Count} aetherytes" : "1 aetheryte");
                }

                ImGuiHelpers.ScaledDummy(6f);
                ImGui.TextColored(AccentSoft, "AETHERYTES");
            }
        }

        foreach (var entry in model.Entries
                     .Where(e => !e.IsEstate)
                     .Where(e => filter.Length == 0 || e.Label.Contains(filter, StringComparison.OrdinalIgnoreCase))
                     .OrderBy(e => e.Label, StringComparer.OrdinalIgnoreCase))
        {
            var selected = this.selectedAetheryteId == entry.AetheryteId;
            var tag = entry.KindTag.Length > 0 ? $"  [{entry.KindTag}]" : string.Empty;

            var color = selected ? Gold : entry.KindTag.Length > 0 ? Good : (Vector4?)null;
            PushText(color);
            if (ImGui.Selectable($"{entry.Label}{tag}##ae{entry.AetheryteId}_{entry.SubIndex}", selected))
            {
                this.selectedAetheryteId = entry.AetheryteId;
                this.selectedTerritoryId = null;
                this.selectedLabel = entry.Label;
            }

            PopText(color);

            ImGui.SameLine();
            this.DrawCostTag(config, entry.LiveCost, entry.BaseIsExact ? string.Empty : "projected");
        }
    }

    private void DrawCostTag(Configuration config, uint cost, string suffix)
    {
        var text = cost == 0 ? "free" : FormatGil(config, cost) + "g";
        ImGui.PushStyleColor(ImGuiCol.Text, cost == 0 ? Good : Gold);
        ImGui.TextUnformatted(text);
        ImGui.PopStyleColor();

        if (suffix.Length > 0)
        {
            ImGui.SameLine();
            ImGui.TextColored(Muted, suffix);
        }
    }

    private static string FormatGil(Configuration config, long cost)
        => config.CompactNumbers ? cost.ToString("N0") : cost.ToString(CultureInfo.InvariantCulture);

    private void DrawRoutes(CostModel model, Configuration config, SlotState slots, List<TravelOption> plan, SlotAdvice advice)
    {
        if (this.selectedLabel.Length == 0)
        {
            ImGui.TextColored(Accent, "Pick a destination on the left.");
            ImGuiHelpers.ScaledDummy(4f);
            ImGui.TextWrapped("Aetherpath prices every way to get there: the plain teleport, your free transfer, a favored slot, aetheryte tickets, Return, estate teleports, and teleporting to a cheaper neighbour and walking in.");
            ImGuiHelpers.ScaledDummy(8f);
            this.DrawSlotAdvice(advice, model, slots, config);
            return;
        }

        ImGui.TextColored(Accent, this.selectedLabel);
        ImGui.SameLine();
        ImGui.TextColored(Muted, plan.Count > 0 ? $"{plan.Count} priced option(s)" : "no priced option");

        if (!slots.IsLive)
        {
            ImGui.TextColored(Gold, "Manual override is on: these numbers are what your slots *would* do, priced from your current location.");
        }

        ImGuiHelpers.ScaledDummy(4f);

        if (plan.Count == 0)
        {
            ImGui.TextColored(Bad, "Nothing to price here yet - that destination has no aetheryte in reach from your current zone.");
        }
        else if (ImGui.BeginTable("ap_routes", 4, ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.SizingStretchProp))
        {
            ImGui.TableSetupColumn("Route", ImGuiTableColumnFlags.WidthStretch, 0.42f);
            ImGui.TableSetupColumn("Gil", ImGuiTableColumnFlags.WidthFixed, 70f * ImGuiHelpers.GlobalScale);
            ImGui.TableSetupColumn("Saves", ImGuiTableColumnFlags.WidthFixed, 80f * ImGuiHelpers.GlobalScale);
            ImGui.TableSetupColumn("What it needs", ImGuiTableColumnFlags.WidthStretch, 0.40f);

            ImGui.TableHeadersRow();

            var best = plan.Where(o => o.Feasible && !o.IsPartialLeg).Select(o => (long?)o.GilCost).Min();

            foreach (var option in plan)
            {
                ImGui.TableNextRow();

                ImGui.TableSetColumnIndex(0);
                var isBest = option.Feasible && !option.IsPartialLeg && option.GilCost == best;
                var routeColor = !option.Feasible ? Muted : isBest ? Good : (Vector4?)null;
                PushText(routeColor);
                ImGui.TextUnformatted(option.ModeLabel + (isBest ? "  ★ cheapest" : string.Empty));
                PopText(routeColor);
                var showTooltip = ImGui.IsItemHovered();

                if (option.SelectionLabel.Length > 0 && option.Mode != TravelMode.Direct)
                {
                    ImGui.TextColored(Muted, $"  via {option.SelectionLabel}");
                    showTooltip = showTooltip || ImGui.IsItemHovered();
                }

                if (showTooltip)
                {
                    var lines = new List<string>(option.Steps);
                    if (option.Note.Length > 0)
                        lines.Add(option.Note);
                    if (lines.Count > 0)
                        ImGui.SetTooltip(string.Join("\n", lines));
                }

                ImGui.TableSetColumnIndex(1);
                ImGui.PushStyleColor(ImGuiCol.Text, option.GilCost == 0 ? Good : option.Feasible ? Gold : Muted);
                ImGui.TextUnformatted(option.GilCost == 0 ? "0 (free)" : FormatGil(config, option.GilCost));
                ImGui.PopStyleColor();
                if (!option.CostIsExact)
                {
                    ImGui.SameLine();
                    ImGui.TextColored(Muted, "~");
                }

                if (option.ConsumesTicket)
                {
                    ImGui.SameLine();
                    ImGui.TextColored(Muted, "+1 ticket");
                }

                ImGui.TableSetColumnIndex(2);
                if (option.Savings > 0)
                {
                    ImGui.TextColored(Good, $"-{FormatGil(config, option.Savings)}");
                }
                else
                {
                    ImGui.TextColored(Muted, "baseline");
                }

                ImGui.TableSetColumnIndex(3);
                ImGui.TextWrapped(option.Requirement.Length > 0 ? option.Requirement : "-");
                if (option.IsPartialLeg)
                {
                    ImGui.TextColored(Muted, "first leg only");
                }
            }

            ImGui.EndTable();
        }

        ImGuiHelpers.ScaledDummy(8f);
        this.DrawSlotAdvice(advice, model, slots, config);
    }

    private void DrawSlotAdvice(SlotAdvice advice, CostModel model, SlotState slots, Configuration config)
    {
        if (advice.Headline.Length == 0 && advice.Lines.Count == 0)
            return;

        ImGui.PushStyleColor(ImGuiCol.Text, Accent);
        ImGui.TextUnformatted("BEST USE OF YOUR SLOTS");
        ImGui.PopStyleColor();

        if (advice.Headline.Length > 0)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, Gold);
            ImGui.TextWrapped(advice.Headline);
            ImGui.PopStyleColor();
        }

        foreach (var line in advice.Lines)
            ImGui.BulletText(line);

        if (config.ShowDiagnostics && advice.BestFreeHolders.Count > 0 && ImGui.TreeNode("What your slots are worth right now##ap_slotvalue"))
        {
            if (ImGui.BeginTable("ap_slot_table", 3, ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp))
            {
                ImGui.TableSetupColumn("Destination");
                ImGui.TableSetupColumn("Full price");
                ImGui.TableSetupColumn("Favored");
                ImGui.TableHeadersRow();

                foreach (var candidate in advice.BestFreeHolders)
                {
                    ImGui.TableNextRow();
                    ImGui.TableSetColumnIndex(0);
                    ImGui.TextUnformatted(candidate.Label + (candidate.IsFree ? "  [free]" : candidate.IsFavored ? "  [fav]" : string.Empty));
                    ImGui.TableSetColumnIndex(1);
                    ImGui.TextColored(Gold, FormatGil(config, candidate.BaseCost));
                    ImGui.TableSetColumnIndex(2);
                    ImGui.TextColored(Muted, FormatGil(config, CostModel.ApplyFavoured(candidate.BaseCost)));
                }

                ImGui.EndTable();
            }

            ImGui.TreePop();
        }
    }
}