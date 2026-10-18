using System;
using System.Collections.Generic;
using System.Linq;

using Lumina.Excel;
using Lumina.Excel.Sheets;

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
    public const int FavoredSlotCount = SlotLogic.FavoredSlotCount;

    /// <summary>Your one-time-password free transfer destination.</summary>
    public uint? FreeDestinationId { get; set; }

    /// <summary>Up to four favored destinations, index-aligned with the override fields.</summary>
    public List<uint> Favored { get; set; } = new() { 0, 0, 0, 0 };

    /// <summary>Home point used by Return.</summary>
    public uint? HomePointId { get; set; }

    /// <summary>True when this came from the game rather than the manual override fields.</summary>
    public bool IsLive { get; set; }

    public bool IsFree(uint aetheryteId) => this.FreeDestinationId == aetheryteId;

    public bool IsFavored(uint aetheryteId) => this.Favored.Contains(aetheryteId);

    public int FreeFavoredSlots => this.Favored.Count(id => id == 0);

    /// <summary>
    /// Build slot state from already-resolved live ids. Prefer this over reading services directly
    /// when testing, or when a caller has already queried <c>IPlayerState</c>.
    /// </summary>
    public static SlotState FromLive(uint? freeDestinationId, IEnumerable<uint> favoredIds, uint? homePointId)
    {
        return new SlotState
        {
            FreeDestinationId = freeDestinationId is 0 or null ? null : freeDestinationId,
            HomePointId = homePointId is 0 or null ? null : homePointId,
            IsLive = true,
            Favored = SlotLogic.NormalizeFavoredSlots(favoredIds),
        };
    }

    /// <summary>Build the state from what the game reports right now.</summary>
    public static SlotState FromGame(CostModel model)
    {
        // IPlayerState is the preferred source for home / free / favored. Fall back to the
        // teleport-list flags CostModel already collected when a field is unavailable.
        var free = ReadPlayerAetheryteId(static () => Plugin.PlayerState.FreeAetheryte)
                   ?? model.CurrentFreeDestinationId;
        var home = ReadPlayerAetheryteId(static () => Plugin.PlayerState.HomeAetheryte);
        var favored = ReadPlayerFavoriteIds()
                      ?? model.CurrentFavoured.Select(e => e.AetheryteId);

        return FromLive(free, favored, home);
    }

    /// <summary>Build the state from the manual override fields in the config.</summary>
    public static SlotState FromConfig(Configuration config)
    {
        return new SlotState
        {
            FreeDestinationId = config.OverrideFreeDestination == 0 ? null : config.OverrideFreeDestination,
            HomePointId = config.OverrideHomePoint == 0 ? null : config.OverrideHomePoint,
            IsLive = false,
            // Preserve slot indices: a value in override slot 2 must stay in slot 2, not compact to slot 0.
            Favored = SlotLogic.NormalizeFavoredSlots(config.OverrideFavored),
        };
    }

    /// <summary>
    /// Auto-read with manual override: when the override fields hold anything, they win; otherwise
    /// we use what the game told us.
    /// </summary>
    public static SlotState Resolve(Configuration config, CostModel model)
        => Resolve(config, FromGame(model));

    /// <summary>
    /// Same as <see cref="Resolve(Configuration, CostModel)"/> but takes a pre-built live state so
    /// the merge rules can be exercised without Dalamud services.
    /// </summary>
    public static SlotState Resolve(Configuration config, SlotState live)
    {
        var (free, home, favored, isLive) = SlotLogic.ResolveOverrides(
            config.AutoReadSlots,
            config.OverrideFreeDestination,
            config.OverrideHomePoint,
            config.OverrideFavored,
            live.FreeDestinationId,
            live.HomePointId,
            live.Favored);

        if (isLive)
        {
            live.IsLive = true;
            // Keep the live object's favored list normalized in case the caller passed a short one.
            live.Favored = favored;
            live.FreeDestinationId = free;
            live.HomePointId = home;
            return live;
        }

        return new SlotState
        {
            FreeDestinationId = free,
            HomePointId = home,
            Favored = favored,
            IsLive = false,
        };
    }

    /// <summary>True when two slot states describe the same configuration.</summary>
    public bool Matches(SlotState other)
        => SlotLogic.SlotSetsMatch(
            this.FreeDestinationId,
            this.HomePointId,
            this.Favored,
            other.FreeDestinationId,
            other.HomePointId,
            other.Favored);

    private static uint? ReadPlayerAetheryteId(Func<RowRef<Aetheryte>> read)
    {
        try
        {
            if (!Plugin.PlayerState.IsLoaded)
                return null;

            var row = read();
            return row.RowId == 0 ? null : row.RowId;
        }
        catch (Exception ex)
        {
            Plugin.Log.Debug(ex, "[Aetherpath] could not read aetheryte id from IPlayerState.");
            return null;
        }
    }

    private static IEnumerable<uint>? ReadPlayerFavoriteIds()
    {
        try
        {
            if (!Plugin.PlayerState.IsLoaded)
                return null;

            var favorites = Plugin.PlayerState.FavoriteAetherytes;
            if (favorites == null || favorites.Count == 0)
                return null;

            return favorites
                .Select(r => r.RowId)
                .Where(id => id != 0)
                .Take(FavoredSlotCount)
                .ToList();
        }
        catch (Exception ex)
        {
            Plugin.Log.Debug(ex, "[Aetherpath] could not read favorite aetherytes from IPlayerState.");
            return null;
        }
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
