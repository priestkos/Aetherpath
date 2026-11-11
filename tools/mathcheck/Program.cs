// Reproducible check of the discount maths in Core/SlotLogic.cs (InvertBase / ApplyFavoured).
// OldApplyFavoured / OldInvertBase are the pre-move CostModel copies, kept to prove the move
// changed nothing.
//
//   cd tools/mathcheck && dotnet run -c Release
//
// Verifies: round-trip stability, the documented +-1 gil rounding on favored prices,
// and that undiscounted entries invert exactly.
using Aetherpath.Core;

static uint OldApplyFavoured(long baseCost) => (uint)(baseCost / 2);

static long? OldInvertBase(uint liveCost, bool favoured, bool isFree, out bool exact)
{
    exact = true;

    if (isFree)
    {
        if (liveCost == 0)
        {
            exact = false;
            return null;
        }

        return liveCost;
    }

    if (!favoured)
        return liveCost;

    exact = false;
    return (long)liveCost * 2;
}

var failures = 0;
long worstDelta = 0;

// Old vs new across the input space the game can hand us.
var compared = 0;
for (long b = 0; b <= 200_000; b++)
{
    compared++;
    if (SlotLogic.ApplyFavoured(b) != OldApplyFavoured(b))
    {
        Console.WriteLine($"FAIL: ApplyFavoured drifted at {b}");
        failures++;
    }

    foreach (var fav in new[] { false, true })
    foreach (var free in new[] { false, true })
    {
        compared++;
        var n = SlotLogic.InvertBase((uint)b, fav, free, out var ne);
        var o = OldInvertBase((uint)b, fav, free, out var oe);
        if (n != o || ne != oe)
        {
            Console.WriteLine($"FAIL: InvertBase drifted at live={b} fav={fav} free={free}");
            failures++;
        }
    }
}

Console.WriteLine($"old vs new: {compared} comparisons");

for (long b = 0; b <= 5000; b++)
{
    var favoured = SlotLogic.ApplyFavoured(b);
    var recovered = SlotLogic.InvertBase(favoured, favoured: true, isFree: false, out _);

    if (recovered == null)
    {
        Console.WriteLine($"FAIL: no recovery for base {b}");
        failures++;
        continue;
    }

    var delta = Math.Abs(recovered.Value - b);
    worstDelta = Math.Max(worstDelta, delta);

    // The favored price must be reproducible from the recovered base: that is what the UI does
    // when it projects "what would this cost if I favored it".
    if (SlotLogic.ApplyFavoured(recovered.Value) != favoured)
    {
        Console.WriteLine($"FAIL: not inverse-stable at base {b} (fav {favoured} -> {recovered} -> {SlotLogic.ApplyFavoured(recovered.Value)})");
        failures++;
    }

    // Halving loses at most the odd gil; anything more means the model is wrong.
    if (delta > 1)
    {
        Console.WriteLine($"FAIL: rounding error {delta} gil at base {b}");
        failures++;
    }

    var plain = SlotLogic.InvertBase((uint)b, favoured: false, isFree: false, out var plainExact);
    if (plain != b || !plainExact)
    {
        Console.WriteLine($"FAIL: plain entry base {b} -> {plain} exact={plainExact}");
        failures++;
    }

    // A free destination reporting 0 gil must be reported as unpriced, not as 0 gil base.
    var free = SlotLogic.InvertBase(0, favoured: false, isFree: true, out var freeExact);
    if (free != null || freeExact)
    {
        Console.WriteLine("FAIL: free destination with 0 gil was priced instead of flagged unknown");
        failures++;
    }
}

// Spot checks against real-world teleport prices.
foreach (var (live, expectBase) in new (uint, long)[] { (100, 200), (500, 1000), (750, 1500), (999, 1998), (1000, 2000), (1250, 2500) })
{
    var got = SlotLogic.InvertBase(live, favoured: true, isFree: false, out var exact);
    var ok = got == expectBase && !exact;
    Console.WriteLine($"favored {live,5} -> base {got,5} (expected {expectBase,5}, flagged projected: {!exact}) {(ok ? "ok" : "MISMATCH")}");
    if (!ok)
        failures++;
}

Console.WriteLine($"worst rounding delta: {worstDelta} gil");
Console.WriteLine(failures == 0 ? "ALL CHECKS PASSED" : $"{failures} CHECK(S) FAILED");
return failures == 0 ? 0 : 1;
