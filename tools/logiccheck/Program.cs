// Reproducible checks for Core/SlotLogic.cs (slot overrides, pricing exactness, walk-in ids).
//
//   cd tools/logiccheck && dotnet run -c Release
//
using Aetherpath.Core;

var failures = 0;

void Fail(string message)
{
    Console.WriteLine($"FAIL: {message}");
    failures++;
}

void Ok(string message) => Console.WriteLine($"ok: {message}");

// ---- favored override indices are preserved (not compacted) -----------------
{
    var normalized = SlotLogic.NormalizeFavoredSlots(new uint[] { 0, 123, 0, 0 });
    if (normalized is not [0, 123, 0, 0])
        Fail($"NormalizeFavoredSlots compacted zeros: [{string.Join(',', normalized)}]");
    else
        Ok("NormalizeFavoredSlots keeps slot-2 override in index 1");
}

// ---- merge fills gaps by index ---------------------------------------------
{
    var manual = SlotLogic.NormalizeFavoredSlots(new uint[] { 0, 123, 0, 0 });
    var live = SlotLogic.NormalizeFavoredSlots(new uint[] { 10, 20, 30, 40 });
    SlotLogic.MergeFavoredGaps(manual, live);
    if (manual is not [10, 123, 30, 40])
        Fail($"MergeFavoredGaps wrong: [{string.Join(',', manual)}]");
    else
        Ok("MergeFavoredGaps fills empty slots from live by index");
}

// ---- ResolveOverrides: auto-read, no override -> live ----------------------
{
    var (free, home, favored, isLive) = SlotLogic.ResolveOverrides(
        autoRead: true,
        overrideFree: 0,
        overrideHome: 0,
        overrideFavored: new uint[] { 0, 0, 0, 0 },
        liveFree: 55,
        liveHome: 77,
        liveFavored: new uint[] { 1, 2, 3, 0 });

    if (!isLive || free != 55 || home != 77 || favored is not [1, 2, 3, 0])
        Fail($"auto-read passthrough: free={free} home={home} live={isLive} fav=[{string.Join(',', favored)}]");
    else
        Ok("ResolveOverrides returns live when overrides empty");
}

// ---- ResolveOverrides: home-only override keeps free/favored from live -----
{
    var (free, home, favored, isLive) = SlotLogic.ResolveOverrides(
        autoRead: true,
        overrideFree: 0,
        overrideHome: 99,
        overrideFavored: new uint[] { 0, 0, 0, 0 },
        liveFree: 55,
        liveHome: 77,
        liveFavored: new uint[] { 1, 2, 0, 0 });

    if (isLive || free != 55 || home != 99 || favored is not [1, 2, 0, 0])
        Fail($"home override merge: free={free} home={home} live={isLive} fav=[{string.Join(',', favored)}]");
    else
        Ok("ResolveOverrides merges home override without wiping live free/favored");
}

// ---- ResolveOverrides: identical override stays live -----------------------
{
    var (free, home, favored, isLive) = SlotLogic.ResolveOverrides(
        autoRead: true,
        overrideFree: 55,
        overrideHome: 77,
        overrideFavored: new uint[] { 1, 2, 3, 0 },
        liveFree: 55,
        liveHome: 77,
        liveFavored: new uint[] { 1, 2, 3, 0 });

    if (!isLive || free != 55 || home != 77)
        Fail($"matching override should stay live: live={isLive} free={free} home={home}");
    else
        Ok("ResolveOverrides treats matching override as live");
}

// ---- PriceFor: projected favored is never exact ----------------------------
{
    var (cost, exact, baseline) = SlotLogic.PriceFor(
        liveCost: 500,
        baseCost: 1000,
        isLive: false,
        isFree: false,
        isFavored: true);

    if (cost != 500 || exact || baseline != 1000)
        Fail($"projected favored: cost={cost} exact={exact} baseline={baseline}");
    else
        Ok("PriceFor marks projected favored prices as inexact");
}

{
    var (cost, exact, _) = SlotLogic.PriceFor(
        liveCost: 250,
        baseCost: 500,
        isLive: true,
        isFree: false,
        isFavored: true);

    if (cost != 250 || !exact)
        Fail($"live favored should keep exact live cost: cost={cost} exact={exact}");
    else
        Ok("PriceFor keeps live costs exact");
}

// ---- Walk-in candidates use zone name, not region --------------------------
{
    var aetherytes = new (uint Id, uint TerritoryId, string ZoneName, bool IsAetheryte, bool Invisible)[]
    {
        (1, 100, "Limsa Lominsa", true, false),
        (2, 101, "Limsa Lominsa", true, false),
        (3, 200, "Middle La Noscea", true, false), // same region vibe, different zone
        (4, 100, "Limsa Lominsa", false, false),   // aethernet shard, not aetheryte
        (5, 102, "Limsa Lominsa", true, true),     // invisible
    };

    var ids = SlotLogic.WalkInCandidateIds(100, "Limsa Lominsa", aetherytes);
    if (!ids.SetEquals(new HashSet<uint> { 1, 2 }))
        Fail($"WalkInCandidateIds expected {{1,2}}, got {{{string.Join(',', ids.OrderBy(x => x))}}}");
    else
        Ok("WalkInCandidateIds uses PlaceNameZone, skips region peers / shards / invisible");
}

Console.WriteLine(failures == 0 ? "ALL CHECKS PASSED" : $"{failures} CHECK(S) FAILED");
return failures == 0 ? 0 : 1;
