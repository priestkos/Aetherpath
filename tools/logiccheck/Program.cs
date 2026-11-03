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
        isFavored: true,
        baseIsExact: true);

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
        isFavored: true,
        baseIsExact: false);

    if (cost != 250 || !exact)
        Fail($"live favored should keep exact live cost: cost={cost} exact={exact}");
    else
        Ok("PriceFor keeps live costs exact");
}

// ---- PriceFor: live-free entry (no base) moved off the free slot is not 0 ---
{
    var (cost, exact, baseline) = SlotLogic.PriceFor(
        liveCost: 0,
        baseCost: null,
        isLive: false,
        isFree: false,
        isFavored: false,
        baseIsExact: false);

    if (cost == 0 || exact || baseline != null)
        Fail($"unpriced override entry ranked as free: cost={cost} exact={exact} baseline={baseline}");
    else
        Ok("PriceFor estimates (not 0) an old free destination in override mode");
}

// ---- PriceFor: estimated base (favored-derived) is not exact in override ----
{
    var (cost, exact, _) = SlotLogic.PriceFor(
        liveCost: 400,
        baseCost: 800,
        isLive: false,
        isFree: false,
        isFavored: false,
        baseIsExact: false);

    if (cost != 800 || exact)
        Fail($"estimated base reported exact: cost={cost} exact={exact}");
    else
        Ok("PriceFor keeps an estimated base inexact");
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

// ---- Configuration list defaults are replaced, not appended, on load --------
// Source check because Configuration.cs needs Dalamud (and Newtonsoft), which this harness avoids.
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Configuration.cs")))
        dir = dir.Parent;

    if (dir == null)
    {
        Fail("Configuration.cs not found above the harness output");
    }
    else
    {
        var lines = File.ReadAllLines(Path.Combine(dir.FullName, "Configuration.cs"));
        var unguarded = lines
            .Select((line, i) => (line, i))
            .Where(x => x.line.Contains("public List<") && x.line.Contains("= new")
                        && (x.i == 0 || !lines[x.i - 1].Contains("ObjectCreationHandling.Replace")))
            .Select(x => x.line.Trim())
            .ToList();

        if (unguarded.Count > 0)
            Fail($"list with defaults lacks ObjectCreationHandling.Replace (grows on every load): {string.Join(" | ", unguarded)}");
        else
            Ok("Configuration list defaults use ObjectCreationHandling.Replace");
    }
}

Console.WriteLine(failures == 0 ? "ALL CHECKS PASSED" : $"{failures} CHECK(S) FAILED");
return failures == 0 ? 0 : 1;
