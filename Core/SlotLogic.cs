using System.Collections.Generic;
using System.Linq;

namespace Aetherpath.Core;

/// <summary>
/// Pure slot-merge and pricing helpers with no Dalamud / Lumina dependency, so
/// <c>tools/logiccheck</c> can exercise them without the game SDK.
/// </summary>
public static class SlotLogic
{
    public const int FavoredSlotCount = 4;

    /// <summary>Pad/truncate a favored id list without dropping zeros (index-preserving).</summary>
    public static List<uint> NormalizeFavoredSlots(IEnumerable<uint>? favoredIds, int slotCount = FavoredSlotCount)
    {
        var result = new List<uint>(slotCount);
        if (favoredIds != null)
        {
            foreach (var id in favoredIds)
            {
                if (result.Count >= slotCount)
                    break;
                result.Add(id);
            }
        }

        while (result.Count < slotCount)
            result.Add(0);

        return result;
    }

    /// <summary>Fill empty manual favored slots from the live list by index.</summary>
    public static void MergeFavoredGaps(IList<uint> manual, IReadOnlyList<uint> live, int slotCount = FavoredSlotCount)
    {
        for (var i = 0; i < slotCount; i++)
        {
            if (manual[i] == 0 && i < live.Count && live[i] != 0)
                manual[i] = live[i];
        }
    }

    public static bool SlotSetsMatch(
        uint? freeA,
        uint? homeA,
        IReadOnlyList<uint> favoredA,
        uint? freeB,
        uint? homeB,
        IReadOnlyList<uint> favoredB)
    {
        if (freeA != freeB || homeA != homeB)
            return false;

        return favoredA.OrderBy(id => id).SequenceEqual(favoredB.OrderBy(id => id));
    }

    /// <summary>
    /// Merge manual overrides onto a live slot snapshot. Returns free, home, favored, and whether
    /// the result still matches live prices (IsLive).
    /// </summary>
    public static (uint? Free, uint? Home, List<uint> Favored, bool IsLive) ResolveOverrides(
        bool autoRead,
        uint overrideFree,
        uint overrideHome,
        IReadOnlyList<uint> overrideFavored,
        uint? liveFree,
        uint? liveHome,
        IReadOnlyList<uint> liveFavored)
    {
        var hasOverride = overrideFree != 0
                          || overrideHome != 0
                          || overrideFavored.Any(id => id != 0);

        var liveFav = NormalizeFavoredSlots(liveFavored);
        var liveFreeId = NullIfZero(liveFree);
        var liveHomeId = NullIfZero(liveHome);

        if (autoRead && !hasOverride)
            return (liveFreeId, liveHomeId, liveFav, true);

        var free = overrideFree == 0 ? null : (uint?)overrideFree;
        var home = overrideHome == 0 ? null : (uint?)overrideHome;
        var favored = NormalizeFavoredSlots(overrideFavored);

        if (autoRead)
        {
            free ??= liveFreeId;
            home ??= liveHomeId;
            MergeFavoredGaps(favored, liveFav);

            if (SlotSetsMatch(free, home, favored, liveFreeId, liveHomeId, liveFav))
                return (liveFreeId, liveHomeId, liveFav, true);
        }

        return (free, home, favored, false);
    }

    /// <summary>
    /// Price a destination under a given slot configuration. Projected favored prices are never
    /// flagged exact (game rounding on the 50% discount is undocumented).
    /// </summary>
    public static (uint Cost, bool Exact, long? Baseline) PriceFor(
        uint liveCost,
        long? baseCost,
        bool isLive,
        bool isFree,
        bool isFavored)
    {
        if (isLive)
            return (liveCost, baseCost != null || liveCost == 0, baseCost);

        var baseline = baseCost;
        if (isFree)
            return (0, true, baseline);

        if (baseline == null)
            return (liveCost, false, null);

        if (isFavored)
            return ((uint)(baseline.Value / 2), false, baseline);

        return ((uint)baseline.Value, true, baseline);
    }

    /// <summary>
    /// Aetheryte ids that count as walk/aethernet neighbours of a territory, using zone name
    /// (PlaceNameZone) rather than the broader region name.
    /// </summary>
    public static HashSet<uint> WalkInCandidateIds(
        uint territoryId,
        string zoneName,
        IEnumerable<(uint Id, uint TerritoryId, string ZoneName, bool IsAetheryte, bool Invisible)> aetherytes)
    {
        return aetherytes
            .Where(a => a.IsAetheryte
                        && !a.Invisible
                        && (a.TerritoryId == territoryId
                            || (zoneName.Length > 0 && a.ZoneName == zoneName)))
            .Select(a => a.Id)
            .ToHashSet();
    }

    private static uint? NullIfZero(uint? id) => id is null or 0 ? null : id;
}
