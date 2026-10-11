# Aetherpath

A Dalamud plugin that prices **every way** to get where you're going in FFXIV, and tells you where
your free transfer and favored destinations earn the most gil.

It is **read-only**: it never teleports, never spends an aetheryte ticket, never changes your
free/favored settings and never writes to game memory. It reads the teleport window the game has
already built for you and does the maths.

## What it answers

- **"What is the cheapest way to get to X?"** — direct teleport, your OTP free transfer, a favored
  slot, an aetheryte ticket, Return to your home point, an estate/apartment teleport, or teleporting
  to a cheaper neighbour and walking/aethernet-ing in.
- **"What is my free transfer / favored slots actually worth?"** — the slot optimizer prices what
  moving a slot to a destination would save, and what the destination holding it now would go back
  to costing.
- **"Where should my slots live?"** — a value table of the destinations where each perk saves the
  most gil.

## Reading vs. typing your slots

Aetherpath reads your **free destination** (the one that comes from registering a one-time-password
authenticator) and your **favored destinations** straight out of the game:

- favored flags and live prices come from Dalamud's `IAetheryteList`;
- the free-destination flag lives in `Telepo.TeleportList[].IsFreeAetheryte`, which Dalamud does not
  surface, so it is read from the client struct.

Three favored slots, or four if the Companion app unlock is on your account — auto-read picks up
however many the game reports.

Type values into **Settings → My travel slots** to override any of them. Overrides let you price
*what-if* setups ("what if I moved my free transfer to Revenant's Toll?"). If your override happens
to describe exactly what the game already has, Aetherpath uses the game's exact prices instead of
projections. Whenever an override is in force the window says so, because the numbers become
projections priced from your current location.

## Where the prices come from

| Number | Source | Accuracy |
| --- | --- | --- |
| What you'd pay right now | The game's teleport list, perks already applied | Exact |
| Full price of an un-discounted destination | Same number | Exact |
| Full price of a destination that is currently favored | Live price × 2 (favored destinations are billed at 50%; the fee cap applies before discounts) | ±1 gil, shown as projected |
| Full price of the destination that is currently free | Not recoverable — the game reports 0 | Shown as unpriced, never invented |
| Favored price of a destination that isn't favored | Full price ÷ 2 | ±1 gil, shown as projected |

Notes:

- The ">1000 gil growth is halved" step in the game's cost curve belongs to the base distance
  calculation, not to the discount, so it is already baked into the live numbers read from the game.
- Free company actions and squadron manuals move the live price; a base derived from a live number
  inherits whatever modifiers were in force when it was read.
- Return is free but shares a 15 minute recast, so it is only ever offered as a full route to your
  home point or to something in the same zone — otherwise it appears as a first leg only, and
  Aetherpath says so rather than pretending the rest of the trip is priced.
- Aetheryte tickets are counted manually (the plugin does not read your inventory): type how many
  you carry into settings and ticket routes get priced at 0 gil + 1 ticket.

## Build

Requires the .NET 10 SDK and the Dalamud dev distribution (normally
`%AppData%\XIVLauncher\addon\Hooks\dev`, or set `DALAMUD_HOME`).

```
dotnet build -c Debug
```

Output: `bin/Debug/Aetherpath.dll` plus the generated `Aetherpath.json` manifest (the manifest is
produced from the MSBuild properties in `Aetherpath.csproj` — never hand-edit a manifest).

## Install into the game

1. `/xlsettings` → **Experimental** → **Dev Plugin Locations** → add the full path to
   `bin\Debug\Aetherpath.dll`.
2. `/xlplugins` → **Dev Tools** → enable **Aetherpath**.
3. `/aetherpath` opens the window, `/aetherpath cfg` opens settings. `/ap` is an alias.
4. Hot-reload works: rebuild and the game picks up the new DLL.

## Verification

- `dotnet build` is clean (0 warnings, 0 errors) against Dalamud API 15.
- `tools/mathcheck` exercises the discount maths exactly as written in `Core/CostModel.cs`
  (round-trip stability, the ±1 gil rounding on favored prices, unpriced free destinations):

  ```
  cd tools/mathcheck && dotnet run -c Release
  ```

- Zone grouping used for walk-in suggestions was checked against the game's sheets: Ul'dah's two
  territories both report `PlaceNameZone = "Ul'dah"`, Limsa Lominsa Lower Decks reports
  `"Limsa Lominsa"` (its `PlaceNameRegion` is the broader `"La Noscea"`), so same-zone neighbours
  are the ones you can actually walk/aethernet between.

**Not verified:** in-game behaviour. Aetherpath has never been run inside FFXIV from this
repository — the compile, the maths and the sheet assumptions are verified, but the live read of
`Telepo.TeleportList` and the window layout need a session in the game. If a price looks wrong,
compare it with the Teleport window: the free/favored flags are shown in the destination list, and
the diagnostics counters in settings say how many entries were priced exactly vs projected.

## Layout

```
Aetherpath.csproj          MSBuild manifest properties + SDK reference
Plugin.cs                  entry point, services, /aetherpath command
Configuration.cs           preferences and the manual slot overrides
Core/Models.cs             sheet-level aetheryte and zone records
Core/GameData.cs           sheet index: names, zones, regions, walk neighbours
Core/CostModel.cs          live prices, discount inversion, free-destination flag
Core/SlotState.cs          free/favored/home slot state, live or overridden
Core/RoutePlanner.cs       ranks every travel option; slot optimizer
Windows/MainWindow.cs      destination picker, route table, slot advice
Windows/ConfigWindow.cs    settings
tools/mathcheck/           standalone harness for the discount maths
```
