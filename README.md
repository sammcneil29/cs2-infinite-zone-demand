# Infinite Zone Demand

A Cities: Skylines II code mod that keeps zone demand at maximum so zoned lots keep growing buildings. Each zone type can be switched on or off in **Options → Infinite Zone Demand**.

| Option | Default | Effect |
| --- | --- | --- |
| Low / Medium / High density housing | On | Residential building demand held at 100 for that density |
| Fill empty homes | On | Household arrivals sped up in proportion to empty homes; back to normal once homes are full |
| Commercial | On | Commercial demand plus per-product shop demand |
| Industrial | On | Manufacturing demand plus per-product factory demand |
| Office | On | Office demand plus per-product office demand |
| Fill empty businesses | On | New shops, factories and offices keep arriving for the zone types above until no buildings are empty |
| Warehouses | Off | Warehouse demand on industrial zones |
| Build anywhere | Off | Game's built-in fast-spawn mode: ignores demand and location for all zones |
| Write diagnostics to log | Off | Logs demand values and empty-home counts every few seconds of game time |

## How it works

Every 16 simulation frames the game's demand systems recompute demand in background jobs. Four game systems then consume the results, each on its own frame of the cycle:

| Game system | What it does | What it reads | What the mod forces, just before it runs |
| --- | --- | --- | --- |
| `ZoneSpawnSystem` | Grows buildings on zoned lots | Headline demand (the bars), plus **per-product building demand** used to choose a shop, factory or office | `ForceBuildingDemandSystem`: both, to maximum |
| `HouseholdSpawnSystem` | Brings new households into the city | Household demand (capped at 200 by the game) | `ForceHouseholdDemandSystem`: 2 × free homes, up to 2000; untouched when no homes are free |
| `CommercialSpawnSystem` | Creates one shop company per product every 128 frames, if none is already waiting for a building | Per-product company demand | `ForceCommercialCompanyDemandSystem`: every product above 0 |
| `IndustrialSpawnSystem` | Creates one factory or office company per product every 128 frames on a random roll (at best 1 in 10 in vanilla), if none is already waiting | Per-product company demand | `ForceIndustrialCompanyDemandSystem`: high enough that the roll always succeeds |

Forcing only the bars fills them but leaves commercial and industrial zones empty. Forcing only building demand grows buildings faster than households and companies arrive, so they stand empty. Each forcing system runs in the same frame as its consumer, immediately before it. It inherits the consumer's update slot via `UpdateBefore`, because the demand jobs overwrite these values on their own frames. The game recomputes everything from scratch each cycle, so switching an option off returns to vanilla within one cycle.

Arrivals stop on their own when the buildings are full. Households are only boosted while homes are free. Companies only spawn while no company of that type is already waiting for a building. The boost therefore fills empty buildings instead of creating homeless households or endless queues of companies.

There's no Harmony. Private game fields are read by reflection. If a game update renames one, it's logged once and only that part stops working; the game doesn't crash.

## Build and test (gaming PC)

Requirements:

- Cities: Skylines II (PC).
- The **Modding Toolchain**, installed from the game's **Options → Modding**. It installs the .NET SDK, Unity pieces and the `CSII_*` environment variables this project uses.
- Visual Studio is optional.

```bash
git clone https://github.com/sammcneil29/cs2-infinite-zone-demand.git
cd cs2-infinite-zone-demand/InfiniteZoneDemand
dotnet build -c Release
```

Close the game before building, because it locks the DLL. The build deploys the mod to `%USERPROFILE%\AppData\LocalLow\Colossal Order\Cities Skylines II\Mods\InfiniteZoneDemand`. The game loads it from there as a local mod.

In game:

1. Check that **Options → Infinite Zone Demand** appears.
2. Load a city with empty zoned lots and unpause.
3. Check the bars fill and buildings grow in every zone type, including commercial and industrial.
4. Check new buildings get occupied. Empty-building icons should clear over a few in-game hours, and the population should climb.
5. Check the log `%USERPROFILE%\AppData\LocalLow\Colossal Order\Cities Skylines II\Logs\InfiniteZoneDemand.Mod.log`. It should contain `All demand-system fields found.` and `Resources classified: ...`. Any `game fields not found` or `failed` lines mean a game update changed something. For bug reports, turn on **Write diagnostics to log**; it adds a line of demand values and empty-home counts every few seconds of game time.

This project was written without access to the game files, against the 1.6.2f1 game internals as documented by other open-source mods. The first build on the gaming PC is the real compile check.

## Publish to Paradox Mods

1. Launch the game once and log in to your Paradox account, then close it. The publisher reuses that login and never asks for a password.
2. Open your profile on [mods.paradoxplaza.com](https://mods.paradoxplaza.com) once so it has a display name.
3. Review `InfiniteZoneDemand/Properties/PublishConfiguration.xml` (title, descriptions, thumbnail, `AccessLevel`).
4. Publish:

   ```bash
   dotnet publish -c Release -p:PublishProfile=PublishNewMod
   ```

5. Copy the printed `Mod published with Id=NNNNNN` into `<ModId Value="NNNNNN" />` and commit it. Without it, the next publish creates a duplicate listing.
6. Delete the local copy in `...\Cities Skylines II\Mods\InfiniteZoneDemand`, so the subscribed version is the one that loads.

For later updates, bump `ModVersion`, set `ChangeLog`, then:

```bash
dotnet publish -c Release -p:PublishProfile=PublishNewVersion
```

To change only the description or images, without a new build:

```bash
dotnet publish -c Release -p:PublishProfile=UpdatePublishedConfiguration
```

## Compatibility

- Don't combine it with other demand overrides: Super Fast Building And Leveling's demand sliders, Unlimited Demand, InfiniteDemand.
- Mods that replace the vanilla demand or spawn systems may change the result: MapExt2 replaces the demand jobs, and Realistic Workplaces And Households replaces household spawning.

## License

MIT
