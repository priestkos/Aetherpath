using System;
using System.Collections.Generic;

using Dalamud.Configuration;

namespace Aetherpath;

/// <summary>
/// Everything here is a *preference*: which options Aetherpath considers, and the manual
/// override values for your travel slots. Nothing in this file is ever written back into
/// the game.
/// </summary>
[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    // ---- slot source ------------------------------------------------------
    /// <summary>Read free destination + favored destinations from the game instead of the overrides below.</summary>
    public bool AutoReadSlots { get; set; } = true;

    /// <summary>Manual override: your OTP free destination (0 = none / unknown).</summary>
    public uint OverrideFreeDestination { get; set; }

    /// <summary>Manual override: favored destinations (0 = empty). Four slots once the Companion app unlock is on your account.</summary>
    public List<uint> OverrideFavored { get; set; } = new() { 0, 0, 0, 0 };

    /// <summary>Manual override: home point for the Return leg (0 = none / unknown).</summary>
    public uint OverrideHomePoint { get; set; }

    // ---- travel options considered ---------------------------------------
    public bool ConsiderDirect { get; set; } = true;
    public bool ConsiderFreeSlot { get; set; } = true;
    public bool ConsiderFavoredSlots { get; set; } = true;
    public bool ConsiderReturn { get; set; } = true;
    public bool ConsiderTickets { get; set; } = true;
    public bool ConsiderEstates { get; set; } = true;
    public bool ConsiderMultiLeg { get; set; } = true;

    /// <summary>How many aetheryte tickets you're carrying. Manual on purpose - we don't touch your bags.</summary>
    public int AetheryteTickets { get; set; }

    /// <summary>Include zones that need a walk-in / aethernet leg in the destination list.</summary>
    public bool IncludeWalkInDestinations { get; set; } = true;

    // ---- display ----------------------------------------------------------
    public bool ShowDiagnostics { get; set; }
    public bool CompactNumbers { get; set; } = true;
    public bool HideTravelAbove999 { get; set; }
    public int MaxRoutesShown { get; set; } = 8;

    public void Save() => Plugin.PluginInterface.SavePluginConfig(this);
}