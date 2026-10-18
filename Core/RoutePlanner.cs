using System;
using System.Collections.Generic;
using System.Linq;

namespace Aetherpath.Core;

/// <summary>
/// Turns "I want to get to X" into a ranked list of priced ways to do it, using the live prices
/// the game hands us plus your slot configuration. It never teleports anything.
/// </summary>
public sealed class RoutePlanner
{
    public List<TravelOption> Plan(Configuration config, CostModel model, SlotState slots, PlanRequest request)
    {
        var options = new List<TravelOption>();
        if (!model.HasLiveData)
            return options;

        var targets = this.ResolveTargets(model, request);
        if (targets.Count == 0)
            return options;

        // A zone with several aetherytes would otherwise flood the list with four payment rows each:
        // keep the payment-mode rows for the cheapest couple and price the rest as plain teleports.
        var paymentTargets = targets
            .OrderBy(t => t.BaseCost ?? t.LiveCost)
            .Take(2)
            .Select(t => t.AetheryteId)
            .ToHashSet();

        foreach (var target in targets)
        {
            var (cost, exact, baseline) = PriceFor(target, slots, config, model);
            var fullPrice = baseline ?? target.LiveCost;
            var isPaymentTarget = paymentTargets.Contains(target.AetheryteId);

            if (config.ConsiderDirect)
            {
                var direct = new TravelOption
                {
                    Mode = TravelMode.Direct,
                    GilCost = cost,
                    CostIsExact = exact,
                    BaselineCost = fullPrice,
                    SelectionLabel = target.Label,
                    SelectionAetheryteId = target.AetheryteId,
                    Requirement = slots.IsFree(target.AetheryteId)
                        ? "uses your free destination"
                        : slots.IsFavored(target.AetheryteId)
                            ? "uses a favored slot"
                            : "no slots needed",
                };
                direct.Steps.Add($"Open Teleport and pick {target.Label}.");
                if (target.IsFreeAetheryte)
                    direct.Steps.Add("This is your OTP free destination, so the fee is already 0.");
                options.Add(direct);
            }

            if (config.ConsiderFreeSlot && isPaymentTarget)
                options.Add(BuildFreeSlotOption(target, slots, model, fullPrice));

            if (config.ConsiderFavoredSlots && isPaymentTarget)
                options.Add(BuildFavoredOption(target, slots, model, fullPrice));

            if (config.ConsiderTickets && isPaymentTarget)
                options.Add(BuildTicketOption(target, config, fullPrice));

            if (config.ConsiderReturn && isPaymentTarget && slots.HomePointId is { } home)
                options.AddRange(BuildReturnOptions(target, slots, home, fullPrice));

            if (config.ConsiderEstates)
                options.AddRange(this.BuildEstateOptions(target, slots, config, model));

            if (config.ConsiderMultiLeg)
                options.AddRange(this.BuildWalkInOptions(target, slots, config, model, fullPrice));
        }

        // Anything that lands in the destination zone is a real answer; keep the list tight but
        // never hide a cheaper option behind an expensive primary pick.
        var ranked = options
            .GroupBy(o => (o.Mode, o.SelectionAetheryteId, o.GilCost))
            .Select(g => g.First())
            .OrderBy(o => o.Feasible ? 0 : 1)
            .ThenBy(o => o.IsPartialLeg ? 1 : 0)
            .ThenBy(o => o.GilCost)
            .ThenBy(o => o.CostIsExact ? 0 : 1)
            .ToList();

        if (config.HideTravelAbove999)
            ranked = ranked.Where(o => o.GilCost < 1000 || o.IsPartialLeg).ToList();

        return ranked.Take(Math.Max(1, config.MaxRoutesShown)).ToList();
    }

    /// <summary>Everything the destination could mean: one aetheryte, or every aetheryte in a zone.</summary>
    private List<CostModel.Entry> ResolveTargets(CostModel model, PlanRequest request)
    {
        if (request.AetheryteId is { } id)
        {
            var entry = model.Get(id);
            return entry == null ? new List<CostModel.Entry>() : new List<CostModel.Entry> { entry };
        }

        if (request.TerritoryId is not { } territoryId)
            return new List<CostModel.Entry>();

        var inZone = model.Entries
            .Where(e => e.TerritoryId == territoryId && !e.IsEstate)
            .ToList();

        if (inZone.Count > 0)
            return inZone;

        // No aetheryte of its own: reachable by teleporting into the same PlaceNameZone and walking.
        // Use zone name (not the broader PlaceNameRegion) so La Noscea does not pull in every coast.
        var zoneName = Plugin.GameData.ZoneName(territoryId);
        var ids = SlotLogic.WalkInCandidateIds(
            territoryId,
            zoneName,
            Plugin.GameData.AllAetherytes.Select(a => (a.Id, a.TerritoryId, a.ZoneName, a.IsAetheryte, a.Invisible)));

        return model.Entries
            .Where(e => ids.Contains(e.AetheryteId) && !e.IsEstate)
            .OrderBy(e => e.BaseCost ?? long.MaxValue)
            .ToList();
    }

    private static TravelOption BuildFreeSlotOption(CostModel.Entry target, SlotState slots, CostModel model, long fullPrice)
    {
        var alreadyFree = slots.IsFree(target.AetheryteId);
        var currentFree = slots.FreeDestinationId is { } current && current != 0 ? model.Get(current) : null;

        var option = new TravelOption
        {
            Mode = TravelMode.FreeSlot,
            GilCost = 0,
            CostIsExact = true,
            BaselineCost = fullPrice,
            SelectionLabel = target.Label,
            SelectionAetheryteId = target.AetheryteId,
            Requirement = alreadyFree
                ? "already your free destination"
                : "move your OTP free transfer here",
        };

        option.Steps.Add($"Set {target.Label} as your free destination, then teleport: 0 gil.");
        if (alreadyFree)
        {
            option.Note = "Nothing to change - this is where your free transfer already points.";
        }
        else if (currentFree != null)
        {
            var holderBase = currentFree.BaseCost ?? currentFree.LiveCost;
            option.Note = $"Currently on {currentFree.Label}; that trip would go back to {holderBase:N0} gil.";
        }
        else
        {
            option.Note = "No free destination detected - check your OTP settings or type it in the override field.";
        }

        return option;
    }

    private static TravelOption BuildFavoredOption(CostModel.Entry target, SlotState slots, CostModel model, long fullPrice)
    {
        var alreadyFavored = slots.IsFavored(target.AetheryteId);
        var projected = CostModel.ApplyFavoured(fullPrice);

        var option = new TravelOption
        {
            Mode = TravelMode.FavoredSlot,
            GilCost = alreadyFavored && slots.IsLive ? target.LiveCost : projected,
            CostIsExact = alreadyFavored && slots.IsLive,
            BaselineCost = fullPrice,
            SelectionLabel = target.Label,
            SelectionAetheryteId = target.AetheryteId,
            Requirement = alreadyFavored
                ? "already favored"
                : slots.FreeFavoredSlots > 0
                    ? $"spend one of your {slots.FreeFavoredSlots} open favored slot(s)"
                    : "replace one of your three favored destinations",
        };

        option.Steps.Add($"Register {target.Label} as a favored destination, then teleport: {projected:N0} gil.");

        if (alreadyFavored)
            option.Note = "This destination is already favored.";
        else if (slots.FreeFavoredSlots == 0)
        {
            var holders = slots.Favored
                .Where(id => id != 0)
                .Select(id => model.Get(id))
                .Where(e => e != null)
                .Select(e => $"{e!.Label} ({(e.BaseCost ?? e.LiveCost):N0} gil full price)")
                .ToList();
            if (holders.Count > 0)
                option.Note = $"All {SlotState.FavoredSlotCount} slots are taken by {string.Join(", ", holders)}.";
        }
        else
        {
            option.Note = "Favored destinations bill at 50% of the fee - the biggest single saving after the free transfer.";
        }

        return option;
    }

    private static TravelOption BuildTicketOption(CostModel.Entry target, Configuration config, long fullPrice)
    {
        var hasTickets = config.AetheryteTickets > 0;

        var option = new TravelOption
        {
            Mode = TravelMode.Ticket,
            GilCost = 0,
            CostIsExact = true,
            BaselineCost = fullPrice,
            SelectionLabel = target.Label,
            SelectionAetheryteId = target.AetheryteId,
            Feasible = hasTickets,
            ConsumesTicket = true,
            Requirement = hasTickets ? $"consumes 1 of {config.AetheryteTickets} ticket(s)" : "no tickets counted",
        };

        option.Steps.Add("Set your ticket allowance in the game's teleport settings to 'use a ticket above N gil', then teleport normally.");
        option.Note = hasTickets
            ? "Aetheryte tickets spend an item instead of gil - the ticket count is whatever you typed into settings."
            : "Type how many aetheryte tickets you are carrying into Aetherpath's settings to see this option priced.";

        return option;
    }

    private static IEnumerable<TravelOption> BuildReturnOptions(CostModel.Entry target, SlotState slots, uint homeId, long fullPrice)
    {
        var home = Plugin.GameData.Get(homeId);
        var homeLabel = home?.Name ?? $"aetheryte {homeId}";

        if (homeId == target.AetheryteId)
        {
            var same = new TravelOption
            {
                Mode = TravelMode.Return,
                GilCost = 0,
                CostIsExact = true,
                BaselineCost = fullPrice,
                SelectionLabel = target.Label,
                SelectionAetheryteId = target.AetheryteId,
                Requirement = "Return off cooldown",
            };
            same.Steps.Add($"Return to {homeLabel} - free, no gil at all.");
            same.Note = "Return has a 15 minute recast and ignores distance, so it is always the cheapest way to your home point.";
            yield return same;
            yield break;
        }

        // Home is in the same zone: Return for free, then walk - a genuinely free route.
        var sameZone = home != null
                       && target.ZoneName.Length > 0
                       && string.Equals(home.ZoneName, target.ZoneName, StringComparison.OrdinalIgnoreCase);

        if (sameZone)
        {
            var walk = new TravelOption
            {
                Mode = TravelMode.Return,
                GilCost = 0,
                CostIsExact = true,
                BaselineCost = fullPrice,
                SelectionLabel = target.Label,
                SelectionAetheryteId = target.AetheryteId,
                Requirement = "Return off cooldown",
            };
            walk.Steps.Add($"Return to {homeLabel} (free), then walk / aethernet to {target.Label}.");
            walk.Note = "Both aetherytes sit in the same zone, so the whole trip costs nothing but the recast.";
            yield return walk;
            yield break;
        }

        // Otherwise Return is only the first leg - prices from home are not knowable from here.
        var partial = new TravelOption
        {
            Mode = TravelMode.Return,
            GilCost = 0,
            CostIsExact = true,
            BaselineCost = fullPrice,
            SelectionLabel = target.Label,
            SelectionAetheryteId = target.AetheryteId,
            IsPartialLeg = true,
            Requirement = "Return off cooldown",
        };
        partial.Steps.Add($"Return to {homeLabel} for free, then teleport onward to {target.Label}.");
        partial.Note = "Only the first leg is priced: teleport fees depend on where you are standing, so Aetherpath will re-price this the moment you land.";
        yield return partial;
    }

    private IEnumerable<TravelOption> BuildEstateOptions(CostModel.Entry target, SlotState slots, Configuration config, CostModel model)
    {
        var candidates = model.Entries
            .Where(e => e.IsEstate && !e.IsFreeAetheryte && e.AetheryteId != target.AetheryteId)
            .Where(e => e.TerritoryId == target.TerritoryId
                        || (target.ZoneName.Length > 0 && string.Equals(e.ZoneName, target.ZoneName, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        foreach (var estate in candidates)
        {
            var (cost, exact, baseline) = PriceFor(estate, slots, config, model);
            var option = new TravelOption
            {
                Mode = TravelMode.EstateTeleport,
                GilCost = cost,
                CostIsExact = exact,
                BaselineCost = baseline ?? estate.LiveCost,
                SelectionLabel = estate.Label,
                SelectionAetheryteId = estate.AetheryteId,
                Requirement = estate.IsApartment ? "apartment teleport" : "estate teleport",
            };
            option.Steps.Add($"Teleport to {estate.Label}, then walk to {target.Label}.");
            option.Note = "Estate and apartment teleports share the same fee curve as aetherytes.";
            yield return option;
        }
    }

    private IEnumerable<TravelOption> BuildWalkInOptions(CostModel.Entry target, SlotState slots, Configuration config, CostModel model, long fullPrice)
    {
        foreach (var neighbourId in Plugin.GameData.WalkNeighbours(target.AetheryteId))
        {
            var neighbour = model.Get(neighbourId);
            if (neighbour == null || neighbour.IsEstate)
                continue;

            var (cost, exact, baseline) = PriceFor(neighbour, slots, config, model);
            if (cost >= fullPrice)
                continue;

            var option = new TravelOption
            {
                Mode = TravelMode.WalkIn,
                GilCost = cost,
                CostIsExact = exact,
                BaselineCost = fullPrice,
                SelectionLabel = neighbour.Label,
                SelectionAetheryteId = neighbour.AetheryteId,
                Requirement = "walk / aethernet leg is free",
            };
            option.Steps.Add($"Teleport to {neighbour.Label} ({cost:N0} gil), then walk or take the aethernet to {target.Label}.");
            option.Note = neighbour.TerritoryId == target.TerritoryId
                ? "Same zone, so the ground leg costs nothing but your time."
                : "Neighbouring aetheryte in the same region - on foot, no gil.";
            yield return option;
        }
    }

    /// <summary>Price a destination under a given slot configuration.</summary>
    public static (uint Cost, bool Exact, long? Baseline) PriceFor(
        CostModel.Entry entry,
        SlotState slots,
        Configuration config,
        CostModel model)
    {
        _ = config;
        _ = model;

        return SlotLogic.PriceFor(
            entry.LiveCost,
            entry.BaseCost,
            slots.IsLive,
            slots.IsFree(entry.AetheryteId),
            slots.IsFavored(entry.AetheryteId));
    }
}

/// <summary>One destination's worth to a travel slot.</summary>
public sealed class SlotCandidate
{
    public uint AetheryteId { get; init; }
    public string Label { get; init; } = string.Empty;
    public long BaseCost { get; init; }
    public bool IsFree { get; init; }
    public bool IsFavored { get; init; }

    public long FreeSaving => this.BaseCost;
    public long FavoredSaving => this.BaseCost - CostModel.ApplyFavoured(this.BaseCost);
}

/// <summary>The "best use of my slots" verdict for one destination.</summary>
public sealed class SlotAdvice
{
    public List<string> Lines { get; } = new();
    public List<SlotCandidate> BestFreeHolders { get; } = new();
    public List<SlotCandidate> BestFavoredHolders { get; } = new();
    public string Headline { get; set; } = string.Empty;
}

public static class SlotOptimizer
{
    public static SlotAdvice Analyse(CostModel model, SlotState slots, Configuration config, PlanRequest? request)
    {
        var advice = new SlotAdvice();

        var priced = model.Entries
            .Where(e => !e.IsEstate && e.BaseCost is > 0)
            .Select(e => new SlotCandidate
            {
                AetheryteId = e.AetheryteId,
                Label = e.Label,
                BaseCost = e.BaseCost!.Value,
                IsFree = slots.IsFree(e.AetheryteId),
                IsFavored = slots.IsFavored(e.AetheryteId),
            })
            .ToList();

        advice.BestFreeHolders.AddRange(priced.OrderByDescending(c => c.FreeSaving).Take(5));

        // A favored slot on the destination already holding the free transfer would be wasted.
        advice.BestFavoredHolders.AddRange(priced
            .Where(c => !c.IsFree)
            .OrderByDescending(c => c.FavoredSaving)
            .Take(5));

        if (priced.Count == 0)
        {
            advice.Headline = "No priced destinations yet - log in and attune to a few aetherytes.";
            return advice;
        }

        var currentFree = slots.FreeDestinationId is { } f ? model.Get(f) : null;
        advice.Lines.Add(currentFree != null
            ? $"Free transfer: {currentFree.Label} (0 gil trips there)."
            : "Free transfer: not detected - type it in the override field if you have one.");
        advice.Lines.Add(slots.Favored.Any(id => id != 0)
            ? $"Favored: {string.Join(", ", slots.Favored.Where(id => id != 0).Select(id => model.Get(id)?.Label ?? $"#{id}"))}."
            : "Favored: no destinations registered.");

        var target = request?.AetheryteId is { } tid ? model.Get(tid) : null;
        if (target != null)
        {
            if (target.BaseCost == null)
            {
                advice.Headline = $"{target.Label} is your free destination, so its full price is not shown - the trip is already 0 gil.";
                return advice;
            }

            var baseCost = target.BaseCost.Value;
            var favoredCost = CostModel.ApplyFavoured(baseCost);

            if (slots.IsFree(target.AetheryteId))
            {
                advice.Headline = $"{target.Label} already holds your free transfer: 0 gil. Best possible.";
            }
            else
            {
                var displaced = currentFree != null && currentFree.AetheryteId != target.AetheryteId
                    ? $" {currentFree.Label} would go back to {(currentFree.BaseCost ?? currentFree.LiveCost):N0} gil a trip."
                    : string.Empty;
                advice.Headline = $"Move the free transfer to {target.Label}: 0 gil instead of {baseCost:N0} (saves {baseCost:N0} a trip).{displaced}";
            }

            if (slots.IsFavored(target.AetheryteId))
            {
                advice.Lines.Add($"{target.Label} is already favored at {target.LiveCost:N0} gil.");
            }
            else
            {
                advice.Lines.Add(slots.FreeFavoredSlots > 0
                    ? $"Favoring {target.Label} costs {favoredCost:N0} gil (saves {baseCost - favoredCost:N0}) and you have {slots.FreeFavoredSlots} open slot(s)."
                    : $"Favoring {target.Label} would cost {favoredCost:N0} gil (saves {baseCost - favoredCost:N0}) but all three slots are taken.");
            }

            var betterFree = advice.BestFreeHolders.FirstOrDefault();
            if (betterFree != null && betterFree.AetheryteId != target.AetheryteId && betterFree.FreeSaving > baseCost)
                advice.Lines.Add($"Note: {betterFree.Label} is worth more to the free transfer ({betterFree.FreeSaving:N0} gil a trip vs {baseCost:N0}).");
        }
        else
        {
            var top = advice.BestFreeHolders.FirstOrDefault();
            advice.Headline = top != null
                ? $"Your free transfer is worth most on {top.Label} ({top.FreeSaving:N0} gil a trip)."
                : "Pick a destination to see how your slots should move.";
        }

        if (advice.BestFavoredHolders.Count > 0)
        {
            var names = string.Join(", ", advice.BestFavoredHolders.Take(3).Select(c => $"{c.Label} ({c.FavoredSaving:N0})"));
            advice.Lines.Add($"Best favored-slot value right now: {names}.");
        }

        return advice;
    }
}