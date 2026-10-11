using System;
using System.Collections.Generic;
using System.Linq;

namespace Aetherpath.Core;

/// <summary>Static sheet data for one aetheryte the player could conceivably use.</summary>
public sealed class AetheryteInfo
{
    public uint Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string TerritoryName { get; init; } = string.Empty;
    public string ZoneName { get; init; } = string.Empty;
    public string RegionName { get; init; } = string.Empty;
    public uint TerritoryId { get; init; }
    public bool IsCityAetheryte { get; init; }
    public int AetherstreamX { get; init; }
    public int AetherstreamY { get; init; }
    public bool IsAetheryte { get; init; }
    public bool Invisible { get; init; }

    /// <summary>"Limsa Lominsa Lower Decks (La Noscea)" style label.</summary>
    public string Label => this.ZoneName.Length > 0 && this.ZoneName != this.TerritoryName
        ? $"{this.Name} ({this.ZoneName})"
        : this.Name;

    public override string ToString() => this.Label;
}

/// <summary>A zone (territory) that has at least one aetheryte, used for zone-level destinations.</summary>
public sealed class ZoneInfo
{
    public uint TerritoryId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string RegionName { get; init; } = string.Empty;
    public List<uint> AetheryteIds { get; } = new();

    public string Label => this.RegionName.Length > 0 ? $"{this.Name} ({this.RegionName})" : this.Name;

    public override string ToString() => this.Label;
}