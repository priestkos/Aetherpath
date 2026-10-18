using System;
using System.Collections.Generic;
using System.Linq;

using Dalamud.Game.ClientState.Aetherytes;

using FFXIVClientStructs.FFXIV.Client.Game.UI;

namespace Aetherpath.Core;

/// <summary>
/// Live prices, straight out of the game's teleport list.
///
/// Two layers of truth:
///  * <see cref="Entry.LiveCost"/> - exactly what the Teleport window would charge you right now,
///    with your current free/favored configuration already applied. Ground truth.
///  * <see cref="Entry.BaseCost"/> - the price with no free/favored discount, derived by inverting
///    the discount. Exact for un-discounted entries. For a favored entry the game charges 50% of
///    the fee (the fee cap is applied before discounts), so we double it; the game's rounding is
///    not documented, so a favored-derived base can be off by 1 gil and is flagged as an estimate.
///    For a free destination the game reports 0, so the undiscounted price is unknown - we say so
///    instead of inventing a number.
///
/// The >1000 gil "growth is halved" step described in the client structs belongs to the base
/// distance curve, not to the discount, so it is already inside whatever the game hands us here.
/// Free company actions and squadron manuals also move the live price; a base derived from a live
/// number inherits whatever modifiers were in force when it was read.
/// </summary>
public sealed class CostModel
{
    public sealed class Entry
    {
        public uint AetheryteId { get; init; }
        public byte SubIndex { get; init; }
        public uint TerritoryId { get; init; }
        public byte Ward { get; init; }
        public byte Plot { get; init; }

        /// <summary>What the game charges right now, free/favored applied.</summary>
        public uint LiveCost { get; init; }

        public bool IsFavourite { get; init; }

        /// <summary>Your one OTP free destination (not surfaced by Dalamud's aetheryte list - read from the client struct).</summary>
        public bool IsFreeAetheryte { get; init; }

        public bool IsSharedHouse { get; init; }
        public bool IsApartment { get; init; }

        public long? BaseCost { get; init; }
        public bool BaseIsExact { get; init; }

        public string Name { get; init; } = string.Empty;
        public string TerritoryName { get; init; } = string.Empty;
        public string ZoneName { get; init; } = string.Empty;
        public string RegionName { get; init; } = string.Empty;
        public int AetherstreamX { get; init; }
        public int AetherstreamY { get; init; }

        public bool IsEstate => this.IsSharedHouse || this.IsApartment || this.Ward > 0 || this.Plot > 0;

        public string KindTag
        {
            get
            {
                if (this.IsFreeAetheryte)
                    return "FREE";
                if (this.IsFavourite)
                    return "FAV";
                if (this.IsEstate)
                    return "ESTATE";
                return string.Empty;
            }
        }

        public string Label => this.ZoneName.Length > 0 && this.ZoneName != this.TerritoryName
            ? $"{this.Name} ({this.ZoneName})"
            : this.Name;

        public override string ToString() => this.Label;
    }

    private readonly Dictionary<(uint Id, byte Sub), Entry> byKey = new();
    private readonly Dictionary<uint, Entry> byAetheryteId = new();

    private DateTime lastRefresh;
    private int refreshCount;

    public List<Entry> Entries { get; } = new();

    /// <summary>True when the game's teleport list was readable (i.e. a character is logged in).</summary>
    public bool HasLiveData { get; private set; }

    /// <summary>How many of the entries we could price exactly, and how many are estimates - shown in diagnostics.</summary>
    public int ExactCount { get; private set; }

    public int EstimatedCount { get; private set; }

    public int UnknownCount { get; private set; }

    public DateTime LastRefresh => this.lastRefresh;

    public int RefreshCount => this.refreshCount;

    public uint? CurrentFreeDestinationId => this.Entries.FirstOrDefault(e => e.IsFreeAetheryte)?.AetheryteId;

    public IReadOnlyList<Entry> CurrentFavoured => this.Entries.Where(e => e.IsFavourite).ToList();

    public Entry? Get(uint aetheryteId) => this.byAetheryteId.GetValueOrDefault(aetheryteId);

    public Entry? GetExact(uint aetheryteId, byte subIndex) => this.byKey.GetValueOrDefault((aetheryteId, subIndex));

    /// <summary>Refresh at most once a second - the teleport list only changes when you move.</summary>
    public void Refresh(bool force = false)
    {
        if (!force && (DateTime.UtcNow - this.lastRefresh).TotalMilliseconds < 1000)
            return;

        this.lastRefresh = DateTime.UtcNow;
        this.refreshCount++;

        try
        {
            this.Rebuild();
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "[Aetherpath] failed to read the live teleport list.");
            this.HasLiveData = false;
        }
    }

    private void Rebuild()
    {
        // Touching IAetheryteList first forces the game to rebuild Telepo.TeleportList for the
        // current territory; read the struct afterwards, never before.
        var length = Plugin.AetheryteList.Length;
        if (length <= 0 || !Plugin.PlayerState.IsLoaded)
        {
            this.HasLiveData = false;
            this.Entries.Clear();
            this.byKey.Clear();
            this.byAetheryteId.Clear();
            return;
        }

        var freeIds = ReadFreeDestinationFlags();
        var freeIdFromPlayer = ReadPlayerFreeAetheryteId();

        this.Entries.Clear();
        this.byKey.Clear();
        this.byAetheryteId.Clear();
        this.ExactCount = 0;
        this.EstimatedCount = 0;
        this.UnknownCount = 0;

        foreach (IAetheryteEntry source in Plugin.AetheryteList)
        {
            var id = source.AetheryteId;
            var sub = source.SubIndex;
            var info = Plugin.GameData.Get(id);

            // Prefer IPlayerState.FreeAetheryte; Telepo flags remain as a SubIndex-aware fallback.
            var isFree = (freeIdFromPlayer is { } fid && id == fid)
                         || freeIds.Contains((id, sub))
                         || freeIds.Contains((id, 0));
            var entry = new Entry
            {
                AetheryteId = id,
                SubIndex = sub,
                TerritoryId = source.TerritoryId,
                Ward = source.Ward,
                Plot = source.Plot,
                LiveCost = source.GilCost,
                IsFavourite = source.IsFavourite,
                IsFreeAetheryte = isFree,
                IsSharedHouse = source.IsSharedHouse,
                IsApartment = source.IsApartment,
                Name = info?.Name ?? $"Aetheryte {id}",
                TerritoryName = info?.TerritoryName ?? Plugin.GameData.TerritoryName(source.TerritoryId),
                ZoneName = info?.ZoneName ?? string.Empty,
                RegionName = info?.RegionName ?? string.Empty,
                AetherstreamX = info?.AetherstreamX ?? 0,
                AetherstreamY = info?.AetherstreamY ?? 0,
                BaseCost = InvertBase(source.GilCost, source.IsFavourite, isFree, out var exact),
                BaseIsExact = exact,
            };

            if (entry.BaseCost == null)
                this.UnknownCount++;
            else if (entry.BaseIsExact)
                this.ExactCount++;
            else
                this.EstimatedCount++;

            this.Entries.Add(entry);
            this.byKey[(id, sub)] = entry;
            // Prefer a non-estate row when the same aetheryte id appears with multiple SubIndex values.
            if (!this.byAetheryteId.TryGetValue(id, out var existing) || (existing.IsEstate && !entry.IsEstate))
                this.byAetheryteId[id] = entry;
        }

        this.Entries.Sort((a, b) => Nullable.Compare(a.BaseCost, b.BaseCost));

        this.HasLiveData = true;
    }

    /// <summary>Reads the free-destination aetheryte id from <c>IPlayerState</c> when available.</summary>
    private static uint? ReadPlayerFreeAetheryteId()
    {
        try
        {
            if (!Plugin.PlayerState.IsLoaded)
                return null;

            var rowId = Plugin.PlayerState.FreeAetheryte.RowId;
            return rowId == 0 ? null : rowId;
        }
        catch (Exception ex)
        {
            Plugin.Log.Debug(ex, "[Aetherpath] could not read FreeAetheryte from IPlayerState.");
            return null;
        }
    }

    /// <summary>
    /// Reads the IsFreeAetheryte flag from Telepo.TeleportList as a SubIndex-aware fallback.
    /// Dalamud's <c>IAetheryteEntry</c> does not expose the flag; <c>IPlayerState.FreeAetheryte</c>
    /// is preferred when present.
    /// </summary>
    private static unsafe HashSet<(uint Id, byte Sub)> ReadFreeDestinationFlags()
    {
        var result = new HashSet<(uint, byte)>();

        var telepo = Telepo.Instance();
        if (telepo == null)
            return result;

        var list = telepo->TeleportList;
        var count = list.Count;
        if (count <= 0 || count > 4096)
            return result;

        for (var i = 0; i < count; i++)
        {
            var info = list[i];
            if (info.IsFreeAetheryte)
                result.Add((info.AetheryteId, info.SubIndex));
        }

        return result;
    }

    /// <summary>Undo whatever discount the game already applied, to recover the full price.</summary>
    private static long? InvertBase(uint liveCost, bool favoured, bool isFree, out bool exact)
    {
        exact = true;

        if (isFree)
        {
            // The game reports 0 for the free destination; the undiscounted price is not recoverable.
            if (liveCost == 0)
            {
                exact = false;
                return null;
            }

            return liveCost;
        }

        if (!favoured)
            return liveCost;

        // Favored destinations are billed at 50% of the fee (the 999 gil cap was applied before
        // discounts). The game's rounding is undocumented, so this is an estimate: +-1 gil.
        exact = false;
        return (long)liveCost * 2;
    }

    /// <summary>Apply the game's favored-destination discount to an undiscounted price.</summary>
    public static uint ApplyFavoured(long baseCost) => (uint)(baseCost / 2);

    /// <summary>What you would pay with no slots involved at all.</summary>
    public static uint FullPrice(Entry entry) => (uint)(entry.BaseCost ?? entry.LiveCost);

    /// <summary>Best (lowest) price currently on offer for a destination, and why.</summary>
    public static (uint Cost, string Via) CheapestKnown(Entry entry, Configuration config)
    {
        var candidates = new List<(uint Cost, string Via)>();

        if (config.ConsiderFreeSlot && entry.IsFreeAetheryte)
            candidates.Add((0, "free destination"));
        if (config.ConsiderFavoredSlots && entry.IsFavourite)
            candidates.Add((ApplyFavoured(FullPrice(entry)), "favored slot"));
        if (config.ConsiderTickets && config.AetheryteTickets > 0)
            candidates.Add((0, "aetheryte ticket"));
        if (config.ConsiderDirect)
            candidates.Add((entry.LiveCost, "direct"));

        return candidates.Count == 0
            ? (entry.LiveCost, "direct")
            : candidates.OrderBy(c => c.Cost).First();
    }
}