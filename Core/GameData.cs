using System;
using System.Collections.Generic;
using System.Linq;

using Dalamud.Plugin.Services;

using Lumina.Excel.Sheets;

namespace Aetherpath.Core;

/// <summary>
/// Sheet-level lookups: aetheryte names, the zone/region a territory belongs to, and which
/// aetherytes sit in which zone. Everything here is static game data - no memory reads.
/// </summary>
public sealed class GameData
{
    private readonly Dictionary<uint, AetheryteInfo> byId = new();
    private readonly Dictionary<uint, ZoneInfo> zonesByTerritory = new();
    private readonly List<AetheryteInfo> all = new();
    private readonly List<ZoneInfo> zones = new();

    private readonly Dictionary<uint, string> territoryNames = new();
    private readonly Dictionary<ushort, string> zoneNamesByTerritory = new();
    private readonly Dictionary<uint, string> regionNamesByTerritory = new();

    public IReadOnlyList<AetheryteInfo> AllAetherytes => this.all;
    public IReadOnlyList<ZoneInfo> AllZones => this.zones;

    public GameData()
    {
        this.EnsureLoaded();
    }

    private bool built;

    /// <summary>
    /// Sheet data may not be readable the instant the plugin loads, so this retries on demand
    /// instead of leaving an empty index for the rest of the session.
    /// </summary>
    public void EnsureLoaded()
    {
        if (this.built)
            return;

        try
        {
            this.Build();
            this.built = true;
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "[Aetherpath] Failed to build sheet index; will retry.");
        }
    }

    public AetheryteInfo? Get(uint aetheryteId) => this.byId.GetValueOrDefault(aetheryteId);

    public ZoneInfo? GetZone(uint territoryId) => this.zonesByTerritory.GetValueOrDefault(territoryId);

    public string TerritoryName(uint territoryId) => this.territoryNames.GetValueOrDefault(territoryId, $"Territory {territoryId}");

    public string ZoneName(uint territoryId) => this.zoneNamesByTerritory.GetValueOrDefault((ushort)territoryId, this.TerritoryName(territoryId));

    public string RegionName(uint territoryId) => this.regionNamesByTerritory.GetValueOrDefault(territoryId, string.Empty);

    /// <summary>Aetherytes that live in the same zone or the same territory - i.e. reachable on foot / by aethernet.</summary>
    public IEnumerable<uint> WalkNeighbours(uint aetheryteId)
    {
        var self = this.Get(aetheryteId);
        if (self == null)
            return Array.Empty<uint>();

        return this.all
            .Where(a => a.Id != aetheryteId
                        && a.IsAetheryte
                        && (a.TerritoryId == self.TerritoryId
                            || (self.ZoneName.Length > 0 && a.ZoneName == self.ZoneName)))
            .Select(a => a.Id);
    }

    private void Build()
    {
        this.all.Clear();
        this.byId.Clear();
        this.zones.Clear();
        this.zonesByTerritory.Clear();

        var aetheryteSheet = Plugin.DataManager.GetExcelSheet<Aetheryte>();
        var zoneOfTerritory = new Dictionary<uint, string>();
        var regionOfTerritory = new Dictionary<uint, string>();

        // 1. territory -> zone / region names (zone == the big map area, region == the city/region grouping)
        var territorySheet = Plugin.DataManager.GetExcelSheet<TerritoryType>();
        foreach (var tt in territorySheet)
        {
            var tId = tt.RowId;
            var tName = SafeName(tt.PlaceName);
            var zoneName = SafeName(tt.PlaceNameZone);
            var regionName = SafeName(tt.PlaceNameRegion);

            if (tName.Length > 0)
                this.territoryNames[tId] = tName;
            if (zoneName.Length > 0)
                zoneOfTerritory[tId] = zoneName;
            if (regionName.Length > 0)
                regionOfTerritory[tId] = regionName;

            this.zoneNamesByTerritory[(ushort)tId] = zoneName.Length > 0 ? zoneName : tName;
            this.regionNamesByTerritory[tId] = regionName;
        }

        // 2. aetherytes
        foreach (var row in aetheryteSheet)
        {
            if (row.RowId == 0)
                continue;

            var territory = row.Territory;
            var territoryId = territory.RowId;
            var name = SafeName(row.PlaceName);
            if (name.Length == 0)
                continue;

            var info = new AetheryteInfo
            {
                Id = row.RowId,
                Name = name,
                TerritoryId = territoryId,
                TerritoryName = territoryId != 0 ? this.TerritoryName(territoryId) : string.Empty,
                ZoneName = territoryId != 0 ? zoneOfTerritory.GetValueOrDefault(territoryId, string.Empty) : string.Empty,
                RegionName = territoryId != 0 ? regionOfTerritory.GetValueOrDefault(territoryId, string.Empty) : string.Empty,
                IsAetheryte = row.IsAetheryte,
                Invisible = row.Invisible,
                AetherstreamX = row.AetherstreamX,
                AetherstreamY = row.AetherstreamY,
                IsCityAetheryte = row.AethernetGroup != 0,
            };

            this.byId[row.RowId] = info;
            this.all.Add(info);

            if (!info.IsAetheryte || info.Invisible || territoryId == 0)
                continue;

            if (!this.zonesByTerritory.TryGetValue(territoryId, out var zone))
            {
                zone = new ZoneInfo
                {
                    TerritoryId = territoryId,
                    Name = this.TerritoryName(territoryId),
                    RegionName = this.regionNamesByTerritory.GetValueOrDefault(territoryId, string.Empty),
                };
                this.zonesByTerritory[territoryId] = zone;
                this.zones.Add(zone);
            }

            zone.AetheryteIds.Add(row.RowId);
        }

        this.zones.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));

        Plugin.Log.Debug($"[Aetherpath] indexed {this.all.Count} aetheryte rows across {this.zones.Count} zones.");
    }

    private static string SafeName(Lumina.Excel.RowRef<PlaceName> reference)
    {
        try
        {
            var row = reference.ValueNullable;
            return row?.Name.ToString() ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }
}