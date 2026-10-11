using System;
using System.Collections.Generic;
using System.Linq;

namespace Aetherpath.Core;

/// <summary>
/// Which aetheryte currently holds which travel perk - either as the game has it right now,
/// or as you typed it in for a what-if. Aetherpath never writes these back.
/// </summary>
public sealed class SlotState
{
    /// <summary>
    /// Three favored destinations, or four once you have logged into the Companion app at least
    /// once (that unlock is permanent). Auto-read picks up however many the game reports.
    /// </summary>
    public const int FavoredSlotCount = 4;

    /// <summary>Your one-time-password free transfer destination.</summary>
    public uint? FreeDestinationId { get; set; }

    /// <summary>Up to three favored destinations.</summary>
    public List<uint> Favored { get; set; } = new() { 0, 0, 0 };

    /// <summary>Home point used by Return.</summary>
    public uint? HomePointId { get; set; }

    /// <summary>True when this came from the game rather than the manual override fields.</summary>
    public bool IsLive { get; set; }

    public bool IsFree(uint aetheryteId) => this.FreeDestinationId == aetheryteId;

    public bool IsFavored(uint aetheryteId) => this.Favored.Contains(aetheryteId);

    public int FreeFavoredSlots => this.Favored.Count(id => id == 0);

    /// <summary>Build the state from what the game reports right now.</summary>
    public static SlotState FromGame(CostModel model)
    {
        var state = new SlotState
        {
            FreeDestinationId = model.CurrentFreeDestinationId,
            IsLive = true,
        };

        state.Favored = model.CurrentFavoured.Select(e => e.AetheryteId).Take(FavoredSlotCount).ToList();
        while (state.Favored.Count < FavoredSlotCount)
            state.Favored.Add(0);

        return state;
    }

    /// <summary>Build the state from the manual override fields in the config.</summary>
    public static SlotState FromConfig(Configuration config)
    {
        var state = new SlotState
        {
            FreeDestinationId = config.OverrideFreeDestination == 0 ? null : config.OverrideFreeDestination,
            HomePointId = config.OverrideHomePoint == 0 ? null : config.OverrideHomePoint,
            IsLive = false,
        };

        state.Favored = config.OverrideFavored
            .Where(id => id != 0)
            .Take(FavoredSlotCount)
            .ToList();
        while (state.Favored.Count < FavoredSlotCount)
            state.Favored.Add(0);

        return state;
    }

    /// <summary>
    /// Auto-read with manual override: when the override fields hold anything, they win; otherwise
    /// we use what the game told us.
    /// </summary>
    public static SlotState Resolve(Configuration config, CostModel model)
    {
        var hasOverride = config.OverrideFreeDestination != 0
                          || config.OverrideHomePoint != 0
                          || config.OverrideFavored.Any(id => id != 0);

        if (config.AutoReadSlots && !hasOverride)
            return FromGame(model);

        var manual = FromConfig(config);

        // Manual override that only fills *some* fields: keep the game's values for the rest so a
        // half-filled override does not silently pretend you have empty slots.
        if (config.AutoReadSlots)
        {
            var live = FromGame(model);
            manual.FreeDestinationId ??= live.FreeDestinationId;
            manual.HomePointId ??= live.HomePointId;
            for (var i = 0; i < FavoredSlotCount; i++)
            {
                if (manual.Favored[i] == 0 && live.Favored[i] != 0)
                    manual.Favored[i] = live.Favored[i];
            }

            // An override that describes the same setup the game already has is not a what-if:
            // keep the exact live prices instead of projecting them.
            if (manual.Matches(live))
                return live;
        }

        manual.IsLive = false;
        return manual;
    }

    /// <summary>True when two slot states describe the same configuration.</summary>
    public bool Matches(SlotState other)
    {
        if (this.FreeDestinationId != other.FreeDestinationId)
            return false;
        if (this.HomePointId != other.HomePointId)
            return false;

        return this.Favored.OrderBy(id => id).SequenceEqual(other.Favored.OrderBy(id => id));
    }
}

/// <summary>A single way to get to the destination, priced.</summary>
public sealed class TravelOption
{
    public TravelMode Mode { get; init; }

    /// <summary>Gil you pay for this route as configured.</summary>
    public uint GilCost { get; set; }

    /// <summary>False when the number is a projection (favored inverse, or a staged leg).</summary>
    public bool CostIsExact { get; set; }

    /// <summary>What this destination costs you with no perks at all - the number the savings are against.</summary>
    public long? BaselineCost { get; set; }

    /// <summary>What you click in the game's Teleport window. Aetherpath never clicks it for you.</summary>
    public string SelectionLabel { get; set; } = string.Empty;

    public uint? SelectionAetheryteId { get; set; }

    public List<string> Steps { get; } = new();

    public string Requirement { get; set; } = string.Empty;

    public string Note { get; set; } = string.Empty;

    public bool Feasible { get; set; } = true;

    /// <summary>True for legs that are only part of a journey (Return, then travel on).</summary>
    public bool IsPartialLeg { get; set; }

    public bool ConsumesTicket { get; set; }

    public long Savings => Math.Max(0, (this.BaselineCost ?? this.GilCost) - this.GilCost);

    public string ModeLabel => this.Mode switch
    {
        TravelMode.Direct => "Direct teleport",
        TravelMode.FreeSlot => "Free destination",
        TravelMode.FavoredSlot => "Favored slot",
        TravelMode.Ticket => "Aetheryte ticket",
        TravelMode.Return => "Return (home point)",
        TravelMode.EstateTeleport => "Estate teleport",
        TravelMode.WalkIn => "Teleport + walk-in",
        _ => this.Mode.ToString(),
    };
}

public enum TravelMode
{
    Direct,
    FreeSlot,
    FavoredSlot,
    Ticket,
    Return,
    EstateTeleport,
    WalkIn,
}

/// <summary>What the user is trying to reach.</summary>
public sealed class PlanRequest
{
    /// <summary>Set when a specific aetheryte was picked.</summary>
    public uint? AetheryteId { get; init; }

    /// <summary>Set when a zone was picked.</summary>
    public uint? TerritoryId { get; init; }

    public string Label { get; init; } = string.Empty;
}